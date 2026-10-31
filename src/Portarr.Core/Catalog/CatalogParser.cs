using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Portarr.Core.Catalog;

public sealed record CatalogParseResult(Catalog Catalog, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Reads catalog.yaml strictly: unknown keys, missing ids, duplicates and dangling references
/// are errors with a message that names the faulty entry, never silently ignored.
/// </summary>
public static partial class CatalogParser
{
    private const string DefaultTokenVariable = "CLOUDFLARE_API_TOKEN";
    private const string DefaultKubernetesTokenVariable = "K8S_TOKEN";

    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(60);

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new CheckDocumentConverter())
        .Build();

    public static CatalogParseResult Parse(string yaml)
    {
        CatalogDocument? document;
        try
        {
            document = Deserializer.Deserialize<CatalogDocument?>(yaml);
        }
        catch (YamlException exception)
        {
            return new CatalogParseResult(Catalog.Empty, [$"Invalid YAML at line {exception.Start.Line}: {Innermost(exception).Message}"]);
        }

        return new Builder().Build(document ?? new CatalogDocument());
    }

    private static Exception Innermost(Exception exception)
    {
        return exception.InnerException is null ? exception : Innermost(exception.InnerException);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();

    /// <summary>A GitHub login: letters, digits and single dashes, at most 39 characters.</summary>
    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$")]
    private static partial Regex OwnerPattern();

    /// <summary>A repository: <c>owner/name</c>.</summary>
    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex RepositoryPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex VariableNamePattern();

    /// <summary>A Kubernetes namespace: a DNS label.</summary>
    [GeneratedRegex("^[a-z0-9]([-a-z0-9]{0,61}[a-z0-9])?$")]
    private static partial Regex NamespacePattern();

    /// <summary><c>60</c> (seconds), <c>30s</c>, <c>5m</c>, <c>1h</c>.</summary>
    [GeneratedRegex("^(?<value>[0-9]{1,6})(?<unit>[smh]?)$")]
    private static partial Regex DurationPattern();

    [GeneratedRegex("^(?<from>[1-5][0-9]{2})(-(?<to>[1-5][0-9]{2}))?$")]
    private static partial Regex StatusRangePattern();

    private sealed class Builder
    {
        private readonly List<string> _errors = [];

        public CatalogParseResult Build(CatalogDocument document)
        {
            var environments = BuildEnvironments(document.Environments ?? []);
            var environmentsById = environments.ToDictionary(e => e.Id);

            var appDocuments = document.Apps ?? [];
            var apps = BuildApps(appDocuments, environmentsById);
            var appIds = apps.Select(a => a.Id).ToHashSet();

            var stacks = BuildStacks(document.Stacks ?? [], appDocuments, appIds);
            var gitHub = BuildGitHub(document.Sources?.GitHub);

            return _errors.Count == 0
                ? new CatalogParseResult(new Catalog(environments, stacks, apps, gitHub), [])
                : new CatalogParseResult(Catalog.Empty, _errors);
        }

        private List<CatalogEnvironment> BuildEnvironments(List<EnvironmentDocument> documents)
        {
            var result = new List<CatalogEnvironment>();

            foreach (var (document, index) in documents.Select((d, i) => (d, i)))
            {
                var id = RequireId(document.Id, $"environments[{index}]", result.Select(e => e.Id));
                if (id is null)
                {
                    continue;
                }

                if (!ValidRuntime(id, document))
                {
                    continue;
                }

                var docker = BuildDocker(id, document.Docker);
                var cloudflare = BuildCloudflare(id, document);
                var kubernetes = BuildKubernetes(id, document);

                if (new object?[] { docker, cloudflare, kubernetes }.Count(c => c is not null) > 1)
                {
                    _errors.Add($"Environment '{id}': declare one connector (docker, cloudflare or kubernetes), not several.");
                }

                result.Add(new CatalogEnvironment(
                    id,
                    document.Name ?? id,
                    docker,
                    string.IsNullOrWhiteSpace(document.Address) ? null : document.Address.Trim(),
                    cloudflare,
                    kubernetes,
                    string.IsNullOrWhiteSpace(document.Host) ? null : document.Host.Trim(),
                    BuildHostMetrics(id, document, docker)));
            }

            ValidateHosts(result);
            return result;
        }

        /// <summary>
        /// <c>host</c> and <c>nodeHosts</c> (D-59) name the environment a machine is a VM on: an existing Docker
        /// or launcher-only environment (one machine: a cluster or a CDN is not a hypervisor), never the
        /// machine itself, and without a loop.
        /// </summary>
        private void ValidateHosts(List<CatalogEnvironment> environments)
        {
            var byId = environments.ToDictionary(e => e.Id, StringComparer.Ordinal);
            foreach (var environment in environments)
            {
                var hosts = (environment.Kubernetes?.NodeHosts?.Values ?? []).Append(environment.Host).OfType<string>().Distinct(StringComparer.Ordinal);
                foreach (var host in hosts)
                {
                    if (!byId.TryGetValue(host, out var target))
                    {
                        _errors.Add($"Environment '{environment.Id}': host '{host}' is not an environment of the catalog.");
                    }
                    else if (target.Id == environment.Id)
                    {
                        _errors.Add($"Environment '{environment.Id}': an environment cannot be its own host.");
                    }
                    else if (target.Runtime is RuntimeNames.Kubernetes or RuntimeNames.Cloudflare)
                    {
                        _errors.Add($"Environment '{environment.Id}': host '{host}' must be a single machine (a docker or launcher-only environment), not a {target.Runtime} environment.");
                    }
                    else if (HostChainLoops(target, environment.Id, byId))
                    {
                        _errors.Add($"Environment '{environment.Id}': host '{host}' runs on '{environment.Id}' itself (a loop).");
                    }
                }
            }
        }

        private static bool HostChainLoops(CatalogEnvironment start, string guestId, Dictionary<string, CatalogEnvironment> byId)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var current = start; current.Host is { } next && seen.Add(current.Id); current = byId.GetValueOrDefault(next) ?? current)
            {
                if (next == guestId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary><c>runtime:</c> is optional, but when given it must agree with the connector block.</summary>
        private bool ValidRuntime(string environmentId, EnvironmentDocument document)
        {
            if (document.Runtime is null)
            {
                return true;
            }

            if (document.Runtime is not (RuntimeNames.Docker or RuntimeNames.Cloudflare or RuntimeNames.Kubernetes))
            {
                _errors.Add($"Environment '{environmentId}': runtime must be docker, cloudflare or kubernetes.");
                return false;
            }

            var other = (document.Runtime, document.Docker, document.Cloudflare, document.Kubernetes) switch
            {
                (not RuntimeNames.Docker, { }, _, _) => RuntimeNames.Docker,
                (not RuntimeNames.Cloudflare, _, { }, _) => RuntimeNames.Cloudflare,
                (not RuntimeNames.Kubernetes, _, _, { }) => RuntimeNames.Kubernetes,
                _ => null,
            };
            if (other is not null)
            {
                _errors.Add($"Environment '{environmentId}': a {document.Runtime} environment cannot declare {other}.");
                return false;
            }

            return true;
        }

        private DockerEndpoint? BuildDocker(string environmentId, DockerDocument? document)
        {
            if (document is null)
            {
                return null;
            }

            if (Uri.TryCreate(document.Url, UriKind.Absolute, out var url) && url.Scheme is "unix" or "http" or "https" or "tcp")
            {
                return new DockerEndpoint(url);
            }

            _errors.Add($"Environment '{environmentId}': docker.url must be unix:///path/to/docker.sock or http(s)://host:port.");
            return null;
        }

        /// <summary>
        /// A Cloudflare account (D-53). The account id is only required here: a placeholder still loads,
        /// and the connector reports the environment as not connected until it is a real id.
        /// </summary>
        private CloudflareAccount? BuildCloudflare(string environmentId, EnvironmentDocument document)
        {
            if (document.Runtime != RuntimeNames.Cloudflare && document.Cloudflare is null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(document.Cloudflare?.AccountId))
            {
                _errors.Add($"Environment '{environmentId}': cloudflare.accountId is required (the id in the Cloudflare dashboard URL).");
                return null;
            }

            var variable = document.Cloudflare.TokenEnv ?? DefaultTokenVariable;
            if (!VariableNamePattern().IsMatch(variable))
            {
                _errors.Add($"Environment '{environmentId}': cloudflare.tokenEnv must be the name of an environment variable (e.g. {DefaultTokenVariable}), never the token itself.");
                return null;
            }

            return new CloudflareAccount(document.Cloudflare.AccountId.Trim(), variable);
        }

        /// <summary>
        /// A Kubernetes API server (D-55). Like Cloudflare, the token comes from an environment variable: an
        /// environment without it still loads and is shown as not connected.
        /// </summary>
        private KubernetesCluster? BuildKubernetes(string environmentId, EnvironmentDocument document)
        {
            if (document.Runtime != RuntimeNames.Kubernetes && document.Kubernetes is null)
            {
                return null;
            }

            var cluster = document.Kubernetes;
            if (!Uri.TryCreate(cluster?.Server, UriKind.Absolute, out var server) || server.Scheme is not ("https" or "http") || server.UserInfo.Length > 0)
            {
                _errors.Add($"Environment '{environmentId}': kubernetes.server must be the API server URL (e.g. https://10.0.0.10:6443), without credentials.");
                return null;
            }

            var variable = cluster.TokenEnv ?? DefaultKubernetesTokenVariable;
            if (!VariableNamePattern().IsMatch(variable))
            {
                _errors.Add($"Environment '{environmentId}': kubernetes.tokenEnv must be the name of an environment variable (e.g. {DefaultKubernetesTokenVariable}), never the token itself.");
                return null;
            }

            if (cluster.CaFile is { } caFile && string.IsNullOrWhiteSpace(caFile))
            {
                _errors.Add($"Environment '{environmentId}': kubernetes.caFile must be the path of the cluster CA certificate (relative to the catalog), or be omitted.");
                return null;
            }

            var nodeHosts = cluster.NodeHosts?
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                .ToDictionary(entry => entry.Key.Trim(), entry => entry.Value.Trim(), StringComparer.Ordinal);
            if (nodeHosts is not null && nodeHosts.Count != cluster.NodeHosts!.Count)
            {
                _errors.Add($"Environment '{environmentId}': kubernetes.nodeHosts maps node names to environment ids (e.g. {{ node-1: server-1 }}).");
                return null;
            }

            if (!TryBuildNodeExporter(environmentId, cluster.NodeExporter, out var nodeExporter))
            {
                return null;
            }

            return new KubernetesCluster(server, variable, cluster.CaFile?.Trim(), nodeHosts is { Count: > 0 } ? nodeHosts : null, nodeExporter);
        }

        /// <summary>
        /// <c>hostMetrics:</c> (D-72) names the node_exporter of a Docker host, the only single machine with a
        /// card; a cluster declares <c>kubernetes.nodeExporter</c> instead.
        /// </summary>
        private HostMetricsEndpoint? BuildHostMetrics(string environmentId, EnvironmentDocument document, DockerEndpoint? docker)
        {
            if (document.HostMetrics is null)
            {
                return null;
            }

            if (docker is null)
            {
                _errors.Add($"Environment '{environmentId}': hostMetrics is for a docker environment; a kubernetes environment declares kubernetes.nodeExporter.");
                return null;
            }

            if (MetricsUrl(document.HostMetrics.NodeExporter) is not { } url)
            {
                _errors.Add($"Environment '{environmentId}': hostMetrics.nodeExporter must be node_exporter's metrics URL (e.g. http://10.0.0.10:9100/metrics), without credentials.");
                return null;
            }

            return new HostMetricsEndpoint(url);
        }

        /// <summary>
        /// <c>kubernetes.nodeExporter:</c> (D-72): a port and an access mode for every node, and per-node
        /// overrides (direct, api-server, off, or a URL). <c>namespace</c> and <c>selector</c> find the exporter's
        /// pod for api-server access.
        /// </summary>
        private bool TryBuildNodeExporter(string environmentId, NodeExporterDocument? document, out KubernetesNodeExporter? nodeExporter)
        {
            nodeExporter = null;
            if (document is null)
            {
                return true;
            }

            var port = document.Port ?? KubernetesNodeExporter.DefaultPort;
            var access = ParseAccess(document.Access ?? "direct");
            if (port is < 1 or > 65535 || access is not (NodeExporterAccess.Direct or NodeExporterAccess.ApiServer))
            {
                _errors.Add($"Environment '{environmentId}': kubernetes.nodeExporter needs a port (1-65535, default {KubernetesNodeExporter.DefaultPort}) and an access of direct or api-server.");
                return false;
            }

            var podNamespace = (document.Namespace ?? KubernetesNodeExporter.DefaultNamespace).Trim();
            var selector = (document.Selector ?? KubernetesNodeExporter.DefaultSelector).Trim();
            if (!NamespacePattern().IsMatch(podNamespace) || selector.Length is 0 or > 512)
            {
                _errors.Add($"Environment '{environmentId}': kubernetes.nodeExporter.namespace must be a namespace name (default {KubernetesNodeExporter.DefaultNamespace}) and selector a pod label selector (default {KubernetesNodeExporter.DefaultSelector}).");
                return false;
            }

            var nodes = new Dictionary<string, NodeExporterRoute>(StringComparer.Ordinal);
            foreach (var (node, value) in document.Nodes ?? [])
            {
                NodeExporterRoute? route = null;
                if (ParseAccess(value) is { } nodeAccess)
                {
                    route = new NodeExporterRoute(nodeAccess);
                }
                else if (MetricsUrl(value) is { } url)
                {
                    route = new NodeExporterRoute(NodeExporterAccess.Url, url);
                }

                if (string.IsNullOrWhiteSpace(node) || route is null)
                {
                    _errors.Add($"Environment '{environmentId}': kubernetes.nodeExporter.nodes maps a node name to direct, api-server, off or a metrics URL (e.g. {{ node-2: api-server }}).");
                    return false;
                }

                nodes[node.Trim()] = route;
            }

            nodeExporter = new KubernetesNodeExporter(port, access.Value, nodes.Count > 0 ? nodes : null, podNamespace, selector);
            return true;
        }

        private static NodeExporterAccess? ParseAccess(string? value) => value?.Trim() switch
        {
            "direct" => NodeExporterAccess.Direct,
            "api-server" => NodeExporterAccess.ApiServer,
            "off" => NodeExporterAccess.Off,
            _ => null,
        };

        /// <summary>An http(s) URL without credentials: read with GET only, so it must not carry a secret.</summary>
        private static Uri? MetricsUrl(string? value)
        {
            return Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.UserInfo.Length == 0 ? url : null;
        }

        private List<CatalogApp> BuildApps(List<AppDocument> documents, Dictionary<string, CatalogEnvironment> environments)
        {
            var result = new List<CatalogApp>();

            foreach (var (document, index) in documents.Select((d, i) => (d, i)))
            {
                var id = RequireId(document.Id, $"apps[{index}]", result.Select(a => a.Id));
                if (id is null)
                {
                    continue;
                }

                if (document.Icon is not null && !IconNames.IsValid(document.Icon))
                {
                    _errors.Add($"App '{id}': icon '{document.Icon}' must be an icon name (lowercase letters, digits, dashes), not a URL; put custom icons in the icons folder next to the catalog.");
                }

                var type = AppType.Web;
                if (document.Type is not null && !Enum.TryParse(document.Type, ignoreCase: true, out type))
                {
                    _errors.Add($"App '{id}': type must be web, desktop or service.");
                }

                result.Add(new CatalogApp(
                    id,
                    document.Name ?? id,
                    type,
                    document.Icon,
                    document.Description,
                    document.Tags ?? [],
                    BuildUrls(id, document.Urls ?? []),
                    BuildSelectors(id, document.Workloads ?? [], environments),
                    BuildCheck(id, type, document.Check),
                    BuildRepository(id, document.Repository)));
            }

            return result;
        }

        private string? BuildRepository(string appId, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (!RepositoryPattern().IsMatch(value.Trim()))
            {
                _errors.Add($"App '{appId}': repository must be owner/name (e.g. example/my-app), not a URL.");
                return null;
            }

            return value.Trim();
        }

        /// <summary>
        /// <c>sources: github:</c> (D-64): the accounts whose repositories are scanned. Like the other connectors,
        /// the token comes from an environment variable: without it the section still loads, shown as not connected.
        /// </summary>
        private GitHubSource? BuildGitHub(GitHubDocument? document)
        {
            if (document is null)
            {
                return null;
            }

            var owners = (document.Owners ?? []).Select(o => o?.Trim() ?? string.Empty).ToList();
            var include = (document.Include ?? []).Select(r => r?.Trim() ?? string.Empty).ToList();
            var exclude = (document.Exclude ?? []).Select(r => r?.Trim() ?? string.Empty).ToList();
            if (owners.Count == 0 && include.Count == 0)
            {
                _errors.Add("sources.github: list the owners to scan (users or organizations), or repositories in include.");
            }

            foreach (var owner in owners.Where(o => !OwnerPattern().IsMatch(o)))
            {
                _errors.Add($"sources.github: owner '{owner}' must be a GitHub user or organization login.");
            }

            foreach (var repository in include.Where(r => !RepositoryPattern().IsMatch(r)))
            {
                _errors.Add($"sources.github: include entry '{repository}' must be owner/name.");
            }

            foreach (var pattern in exclude.Where(r => !RepositoryPattern().IsMatch(r.Replace("*", "x", StringComparison.Ordinal))))
            {
                _errors.Add($"sources.github: exclude entry '{pattern}' must be owner/name, * allowed (e.g. example/test-*).");
            }

            var variable = document.TokenEnv ?? GitHubSource.DefaultTokenVariable;
            if (!VariableNamePattern().IsMatch(variable))
            {
                _errors.Add($"sources.github: tokenEnv must be the name of an environment variable (e.g. {GitHubSource.DefaultTokenVariable}), never the token itself.");
            }

            var months = document.PushedWithinMonths ?? GitHubSource.DefaultPushedWithinMonths;
            if (months is < 0 or > 120)
            {
                _errors.Add("sources.github: pushedWithinMonths must be between 0 (no limit) and 120.");
            }

            return new GitHubSource(
                owners.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                variable,
                months,
                include.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                exclude,
                document.Forks ?? false,
                document.Archived ?? false);
        }

        /// <summary>Web apps are checked by default (D-50); <c>check: false</c> turns it off, a mapping overrides it.</summary>
        private CheckDefinition? BuildCheck(string appId, AppType type, CheckDocument? document)
        {
            if (document?.InvalidScalar is not null)
            {
                _errors.Add($"App '{appId}': check must be false, true or a mapping (url, expect, keyword, interval, timeout, critical).");
                return null;
            }

            if (document is null)
            {
                return type == AppType.Web ? CheckDefinition.Default : null;
            }

            if (!document.Enabled)
            {
                return null;
            }

            return document.Body is null ? CheckDefinition.Default : BuildCheckBody(appId, document.Body);
        }

        private CheckDefinition? BuildCheckBody(string appId, CheckBodyDocument body)
        {
            var errors = _errors.Count;

            Uri? url = null;
            if (body.Url is not null && (!Uri.TryCreate(body.Url, UriKind.Absolute, out url) || url.Scheme is not ("http" or "https") || url.UserInfo.Length > 0))
            {
                _errors.Add($"App '{appId}': check.url '{Redact(body.Url)}' must be an absolute http(s) URL without credentials.");
            }

            var interval = ParseDuration(appId, "interval", body.Interval, CheckDefinition.DefaultInterval);
            var timeout = ParseDuration(appId, "timeout", body.Timeout, CheckDefinition.DefaultTimeout);

            if (interval < MinInterval || interval > MaxInterval)
            {
                _errors.Add($"App '{appId}': check.interval must be between 10s and 24h.");
            }

            if (timeout <= TimeSpan.Zero || timeout > MaxTimeout || timeout > interval)
            {
                _errors.Add($"App '{appId}': check.timeout must be between 1s and 60s, and not longer than the interval.");
            }

            var expect = body.Expect is null ? CheckDefinition.DefaultExpect : body.Expect.Select(e => ParseStatusRange(appId, e)).OfType<StatusCodeRange>().ToList();
            if (body.Expect is { Count: 0 })
            {
                _errors.Add($"App '{appId}': check.expect must list at least one status code.");
            }

            if (body.Keyword is { Length: 0 })
            {
                _errors.Add($"App '{appId}': check.keyword must not be empty.");
            }

            return _errors.Count > errors ? null : new CheckDefinition(url, expect, body.Keyword, interval, timeout, body.Critical ?? true);
        }

        private TimeSpan ParseDuration(string appId, string key, string? value, TimeSpan fallback)
        {
            if (value is null)
            {
                return fallback;
            }

            var match = DurationPattern().Match(value.Trim());
            if (!match.Success)
            {
                _errors.Add($"App '{appId}': check.{key} '{value}' must be a number of seconds or a duration such as 30s or 5m.");
                return fallback;
            }

            var amount = int.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
            return match.Groups["unit"].Value switch
            {
                "m" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                _ => TimeSpan.FromSeconds(amount),
            };
        }

        private StatusCodeRange? ParseStatusRange(string appId, string value)
        {
            var match = StatusRangePattern().Match(value.Trim());
            if (match.Success)
            {
                var from = int.Parse(match.Groups["from"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var to = match.Groups["to"].Success ? int.Parse(match.Groups["to"].Value, System.Globalization.CultureInfo.InvariantCulture) : from;
                if (to >= from)
                {
                    return new StatusCodeRange(from, to);
                }
            }

            _errors.Add($"App '{appId}': check.expect '{value}' must be a status code (200) or a range (200-299).");
            return null;
        }

        private List<AppUrl> BuildUrls(string appId, List<UrlDocument> documents)
        {
            var result = new List<AppUrl>();

            foreach (var document in documents)
            {
                if (!Uri.TryCreate(document.Href, UriKind.Absolute, out var href) || href.Scheme is not ("http" or "https"))
                {
                    _errors.Add($"App '{appId}': url '{Redact(document.Href)}' must be an absolute http(s) URL.");
                    continue;
                }

                // App URLs are served to anonymous visitors: credentials in them would leak.
                if (href.UserInfo.Length > 0)
                {
                    _errors.Add($"App '{appId}': url '{Redact(document.Href)}' must not contain credentials (user:password@); every visitor would see them.");
                    continue;
                }

                var scope = UrlScopes.Infer(href);
                if (document.Scope is not null && !Enum.TryParse(document.Scope, ignoreCase: true, out scope))
                {
                    _errors.Add($"App '{appId}': scope of '{Redact(document.Href)}' must be public or lan.");
                }

                result.Add(new AppUrl(document.Label ?? href.Host, href, scope));
            }

            return result;
        }

        private List<WorkloadSelector> BuildSelectors(string appId, List<SelectorDocument> documents, Dictionary<string, CatalogEnvironment> environments)
        {
            var result = new List<WorkloadSelector>();

            foreach (var document in documents)
            {
                var environment = ResolveSelectorEnvironment(appId, document.Runtime ?? string.Empty, environments);
                if (environment is null)
                {
                    continue;
                }

                var selector = BuildMatch(appId, document, environment);
                if (selector is null)
                {
                    continue;
                }

                var role = MemberRole.Critical;
                if (document.Role is not null && !Enum.TryParse(document.Role, ignoreCase: true, out role))
                {
                    _errors.Add($"App '{appId}': role of workload '{selector.Description}' must be critical or optional.");
                }

                result.Add(selector with { Role = role });
            }

            return result;
        }

        /// <summary><c>&lt;kind&gt;@&lt;environment id&gt;</c>, where the kind is the environment's connector.</summary>
        private CatalogEnvironment? ResolveSelectorEnvironment(string appId, string runtime, Dictionary<string, CatalogEnvironment> environments)
        {
            var separator = runtime.IndexOf('@', StringComparison.Ordinal);
            var kind = separator < 0 ? string.Empty : runtime[..separator];
            if (kind is not (RuntimeNames.Docker or RuntimeNames.Cloudflare or RuntimeNames.Kubernetes))
            {
                _errors.Add($"App '{appId}': workload runtime '{runtime}' must be docker@<environment id>, cloudflare@<environment id> or kubernetes@<environment id>.");
                return null;
            }

            var environmentId = runtime[(separator + 1)..];
            if (!environments.TryGetValue(environmentId, out var environment))
            {
                _errors.Add($"App '{appId}': environment '{environmentId}' is not declared in environments.");
                return null;
            }

            // An environment without a connector yet (D-32) accepts docker selectors, as before.
            var expected = environment.Runtime == RuntimeNames.None ? RuntimeNames.Docker : environment.Runtime;
            if (kind != expected)
            {
                _errors.Add($"App '{appId}': environment '{environmentId}' is a {expected} environment; use {expected}@{environmentId}.");
                return null;
            }

            return environment;
        }

        /// <summary>
        /// Docker and Cloudflare match by name. Kubernetes also matches by namespace, <c>app</c> label and name
        /// prefix (D-56), and needs a name, a prefix or an app: a namespace alone would match all its pods.
        /// </summary>
        private WorkloadSelector? BuildMatch(string appId, SelectorDocument document, CatalogEnvironment environment)
        {
            var match = document.Match ?? new MatchDocument();
            var name = Blank(match.Name);
            var ns = Blank(match.Namespace);
            var app = Blank(match.App);
            var prefix = Blank(match.NamePrefix);

            if (environment.Runtime != RuntimeNames.Kubernetes)
            {
                if (ns is not null || app is not null || prefix is not null)
                {
                    _errors.Add($"App '{appId}': match.namespace, match.app and match.namePrefix only apply to kubernetes workloads.");
                    return null;
                }

                if (name is null)
                {
                    _errors.Add($"App '{appId}': workload on {document.Runtime} needs match.name (the container, Pages project or Worker name).");
                    return null;
                }

                return new WorkloadSelector(environment.Id, name);
            }

            if (name is null && app is null && prefix is null)
            {
                _errors.Add($"App '{appId}': workload on {document.Runtime} needs match.app (the pod's app label), match.name or match.namePrefix, usually with match.namespace.");
                return null;
            }

            return new WorkloadSelector(environment.Id, name, Namespace: ns, App: app, NamePrefix: prefix);
        }

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private List<CatalogStack> BuildStacks(List<StackDocument> documents, List<AppDocument> appDocuments, HashSet<string> appIds)
        {
            var result = new List<CatalogStack>();

            foreach (var (document, index) in documents.Select((d, i) => (d, i)))
            {
                var id = RequireId(document.Id, $"stacks[{index}]", result.Select(s => s.Id));
                if (id is null)
                {
                    continue;
                }

                var members = BuildMembers(id, document.Members ?? [], appIds);

                // "stack: <id>" on an app is a shorthand for a critical membership.
                foreach (var app in appDocuments.Where(a => a.Stack == id && a.Id is not null && members.All(m => m.AppId != a.Id)))
                {
                    members.Add(new StackMember(app.Id!, MemberRole.Critical));
                }

                result.Add(new CatalogStack(id, document.Name ?? id, document.Description, members));
            }

            // An app has one stack: its role and its place in the launcher must not be ambiguous.
            foreach (var app in result.SelectMany(s => s.Members.Select(m => (Stack: s.Id, m.AppId))).GroupBy(m => m.AppId).Where(g => g.Count() > 1))
            {
                _errors.Add($"App '{app.Key}' belongs to several stacks ({string.Join(", ", app.Select(m => m.Stack))}); an app has one stack.");
            }

            var stackIds = result.Select(s => s.Id).ToHashSet();
            foreach (var app in appDocuments.Where(a => a.Stack is not null && !stackIds.Contains(a.Stack)))
            {
                _errors.Add($"App '{app.Id}': stack '{app.Stack}' is not declared in stacks.");
            }

            return result;
        }

        private List<StackMember> BuildMembers(string stackId, List<MemberDocument> documents, HashSet<string> appIds)
        {
            var members = new List<StackMember>();

            foreach (var member in documents)
            {
                if (member.App is not null && members.Exists(m => m.AppId == member.App))
                {
                    _errors.Add($"Stack '{stackId}': member '{member.App}' is listed twice.");
                    continue;
                }

                if (member.App is null || !appIds.Contains(member.App))
                {
                    _errors.Add($"Stack '{stackId}': member '{member.App}' is not a declared app.");
                    continue;
                }

                var role = MemberRole.Critical;
                if (member.Role is not null && !Enum.TryParse(member.Role, ignoreCase: true, out role))
                {
                    _errors.Add($"Stack '{stackId}': role of '{member.App}' must be critical or optional.");
                }

                members.Add(new StackMember(member.App, role));
            }

            return members;
        }

        // Never echo credentials from a URL into an error message.
        private static string? Redact(string? url)
        {
            var at = url?.IndexOf('@', StringComparison.Ordinal) ?? -1;
            var scheme = url?.IndexOf("://", StringComparison.Ordinal) ?? -1;
            return at > scheme && scheme >= 0 ? string.Concat(url.AsSpan(0, scheme + 3), "***", url.AsSpan(at)) : url;
        }

        private string? RequireId(string? id, string location, IEnumerable<string> existing)
        {
            if (string.IsNullOrWhiteSpace(id) || !IdPattern().IsMatch(id))
            {
                _errors.Add($"{location}: id '{id}' must be lowercase letters, digits and dashes (e.g. media-server).");
                return null;
            }

            if (existing.Contains(id))
            {
                _errors.Add($"{location}: id '{id}' is used twice.");
                return null;
            }

            return id;
        }
    }
}
