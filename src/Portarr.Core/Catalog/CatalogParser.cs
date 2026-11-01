using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
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

    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Reads a catalog that declares no connector section: what a test of the generic parts needs. Production reads go through <see cref="ICatalogParser"/>.</summary>
    internal static CatalogParseResult Parse(string yaml) => Parse(yaml, []);

    /// <summary>
    /// Reads a catalog. <paramref name="connectors"/> are the connector libraries' sections of an environment (D-105); a section
    /// nobody registered is an unknown key, like any other.
    /// </summary>
    public static CatalogParseResult Parse(string yaml, IEnumerable<IEnvironmentConnector> connectors)
    {
        var registered = EnvironmentConnectorRegistry.Validate(connectors);
        var (full, plain) = CatalogDeserializers.For(registered.Select(c => c.Section).ToHashSet(StringComparer.Ordinal));
        CatalogDocument? document;
        try
        {
            document = full.Deserialize<CatalogDocument?>(yaml);
        }
        catch (YamlException exception)
        {
            return new CatalogParseResult(Catalog.Empty, [$"Invalid YAML at line {exception.Start.Line}: {Innermost(exception).Message}"]);
        }

        return new Builder(registered, plain).Build(document ?? new CatalogDocument());
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

    private const string HttpScheme = "http";
    private const string HttpsScheme = "https";

    /// <summary>Where the reader serves the Docker API and the host metrics (<c>route()</c> in <c>reader/src/server.rs</c>), under its base URL.</summary>
    private const string ReaderDockerPath = "v1/docker/";

    private const string ReaderHostPath = "v1/host";

    /// <summary>The host agent section of an environment, which the <c>reader:</c> shortcut also stands for.</summary>
    private const string HostMetricsSectionName = "hostMetrics";

    /// <summary><c>60</c> (seconds), <c>30s</c>, <c>5m</c>, <c>1h</c>.</summary>
    [GeneratedRegex("^(?<value>[0-9]{1,6})(?<unit>[smh]?)$")]
    private static partial Regex DurationPattern();

    [GeneratedRegex("^(?<from>[1-5][0-9]{2})(-(?<to>[1-5][0-9]{2}))?$")]
    private static partial Regex StatusRangePattern();

    /// <summary>A duration: <c>30m</c>, <c>24h</c>, <c>7d</c>.</summary>
    [GeneratedRegex("^(?<value>[0-9]{1,4})(?<unit>[mhd])$")]
    private static partial Regex EveryPattern();

    /// <summary>A Backrest plan id.</summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$")]
    private static partial Regex PlanPattern();

    /// <summary>A namespaced Kubernetes object: <c>namespace/name</c>.</summary>
    [GeneratedRegex("^(?<namespace>[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)/(?<name>[a-z0-9]([a-z0-9.-]{0,251}[a-z0-9])?)$")]
    private static partial Regex NamespacedNamePattern();

    /// <summary>A data kind: <c>postgresql</c>, <c>sqlite-app-config</c>.</summary>
    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex KindPattern();

    private sealed partial class Builder
    {
        private readonly List<string> _errors = [];
        private readonly IReadOnlyList<IEnvironmentConnector> _connectors;
        private readonly IDeserializer _plain;

        public Builder(IReadOnlyList<IEnvironmentConnector> connectors, IDeserializer plain)
        {
            _connectors = connectors;
            _plain = plain;
        }

        public CatalogParseResult Build(CatalogDocument document)
        {
            var environments = BuildEnvironments(document.Environments ?? []);
            var environmentsById = environments.ToDictionary(e => e.Id);

            var appDocuments = document.Apps ?? [];
            var apps = BuildApps(appDocuments, environmentsById);
            var appIds = apps.Select(a => a.Id).ToHashSet();

            var stacks = BuildStacks(document.Stacks ?? [], appDocuments, appIds);
            var data = BuildData(document.Data ?? [], environmentsById, appIds);
            var sources = BuildSources(document.Sources?.GitHub);
            var links = BuildLinks(document.Links ?? [], environments);
            var providers = BuildProviders(document.Providers ?? [], environmentsById, appIds);
            var portal = BuildPortal(document.Portal);

            return _errors.Count == 0
                ? new CatalogParseResult(new Catalog(environments, stacks, apps, data, sources, links, providers, portal), [])
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

                var reader = BuildReader(id, document);
                if (document.Reader is not null && reader is null)
                {
                    continue;
                }

                // The reader is catalog syntax: it is read as the docker section it stands for (D-102, D-105).
                var sections = reader is null
                    ? document.Sections
                    : new Dictionary<string, IReadOnlyList<ParsingEvent>>(document.Sections) { [RuntimeNames.Docker] = reader.Value.Docker, [HostMetricsSectionName] = reader.Value.HostMetrics };
                var connectors = BuildConnectors(id, document, sections);

                if (connectors.Runtimes.Count > 1)
                {
                    _errors.Add($"Environment '{id}': declare one connector ({RuntimeList()}), not several.");
                }

                result.Add(new CatalogEnvironment(
                    id,
                    document.Name ?? id,
                    string.IsNullOrWhiteSpace(document.Address) ? null : document.Address.Trim(),
                    string.IsNullOrWhiteSpace(document.Host) ? null : document.Host.Trim(),
                    BuildNotes($"Environment '{id}'", document.Notes),
                    BuildAccess($"Environment '{id}'", "access", document.Access),
                    BuildMachines(id, document.Machines),
                    connectors.Settings,
                    connectors.MachineHosts,
                    connectors.Runtimes.FirstOrDefault(),
                    connectors.SingleMachine));
            }

            ValidateHosts(result);
            return result;
        }

        /// <summary>What the connector libraries made of an environment's sections: their settings, the hosts they know, the runtimes found.</summary>
        private sealed record ConnectorBuild(Dictionary<string, object> Settings, Dictionary<string, string>? MachineHosts, List<string> Runtimes, bool SingleMachine);

        private static readonly ParsingEvent[] EmptySection = [new MappingStart(AnchorName.Empty, TagName.Empty, true, MappingStyle.Flow), new MappingEnd()];

        /// <summary>
        /// Gives each section a connector library owns to that library (D-105), and gathers what they made of them. A runtime named
        /// by <c>runtime:</c> is read even without its section, as an empty one, so that it reports what it needs.
        /// </summary>
        private ConnectorBuild BuildConnectors(string environmentId, EnvironmentDocument document, IReadOnlyDictionary<string, IReadOnlyList<ParsingEvent>> sections)
        {
            var settings = new Dictionary<string, object>(StringComparer.Ordinal);
            var runtimes = new List<string>();
            Dictionary<string, string>? machineHosts = null;
            var owned = _connectors.Where(c => sections.ContainsKey(c.Section) || (c.IsRuntime && document.Runtime == c.Section)).ToList();
            var singleMachine = owned.Where(c => c.IsRuntime).All(c => c.SingleMachine);
            var hasRuntime = owned.Any(c => c.IsRuntime);
            foreach (var connector in owned)
            {
                var events = sections.GetValueOrDefault(connector.Section);
                if (events is null || (document.Runtime == connector.Section && IsNull(events)))
                {
                    events = EmptySection;
                }

                var context = new EnvironmentSectionContext(environmentId, connector.Section, singleMachine, hasRuntime, events, _plain, _errors.Add);
                if (connector.Parse(context) is not { } result)
                {
                    continue;
                }

                settings[connector.Section] = result.Settings;
                if (connector.IsRuntime)
                {
                    runtimes.Add(connector.Section);
                }

                foreach (var (machine, host) in result.MachineHosts ?? new Dictionary<string, string>())
                {
                    machineHosts ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    if (machineHosts.TryGetValue(machine, out var known) && known != host)
                    {
                        _errors.Add($"Environment '{environmentId}': machine '{machine}' is given the hosts '{known}' and '{host}' by different sections.");
                    }

                    machineHosts[machine] = host;
                }
            }

            return new ConnectorBuild(settings, machineHosts, runtimes, singleMachine);
        }

        /// <summary>A section with no value: <c>cloudflare:</c> followed by nothing, <c>~</c> or <c>null</c>.</summary>
        private static bool IsNull(IReadOnlyList<ParsingEvent> events)
        {
            return events is [Scalar { Value: "" or "~" or "null" }];
        }

        /// <summary>The runtimes an environment can have, as the message names them: those the connector libraries register.</summary>
        private IReadOnlyList<string> Runtimes()
        {
            return [.. _connectors.Where(c => c.IsRuntime).Select(c => c.Section)];
        }

        private string RuntimeList(string last = " or ", string suffix = "")
        {
            var runtimes = Runtimes().Select(runtime => runtime + suffix).ToList();
            return runtimes.Count == 1 ? runtimes[0] : $"{string.Join(", ", runtimes.Take(runtimes.Count - 1))}{last}{runtimes[^1]}";
        }

        /// <summary>The fields an app can select the workloads of this environment with: its runtime connector's, else the name only.</summary>
        private WorkloadMatchFields MatchFieldsOf(CatalogEnvironment environment)
        {
            return _connectors.FirstOrDefault(c => c.IsRuntime && c.Section == environment.Runtime)?.MatchFields ?? WorkloadMatchFields.Name;
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
                var hosts = (environment.MachineHosts?.Values ?? []).Append(environment.Host).OfType<string>().Distinct(StringComparer.Ordinal);
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
                    else if (!target.SingleMachine)
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

            if (!Runtimes().Contains(document.Runtime))
            {
                _errors.Add($"Environment '{environmentId}': runtime must be {RuntimeList()}.");
                return false;
            }

            var declared = Runtimes().Where(runtime => document.Sections.ContainsKey(runtime));
            if (declared.FirstOrDefault(runtime => runtime != document.Runtime) is { } other)
            {
                _errors.Add($"Environment '{environmentId}': a {document.Runtime} environment cannot declare {other}.");
                return false;
            }

            return true;
        }

        private (IReadOnlyList<ParsingEvent> Docker, IReadOnlyList<ParsingEvent> HostMetrics)? BuildReader(string environmentId, EnvironmentDocument document)
        {
            if (document.Reader is null)
            {
                return null;
            }

            if (document.Sections.ContainsKey(RuntimeNames.Docker) || document.Sections.ContainsKey(HostMetricsSectionName))
            {
                _errors.Add($"Environment '{environmentId}': reader replaces docker and hostMetrics; declare the reader, or docker and hostMetrics, not both.");
                return null;
            }

            if (document.Runtime is not null and not RuntimeNames.Docker)
            {
                _errors.Add($"Environment '{environmentId}': a {document.Runtime} environment cannot declare a reader (it reads Docker hosts).");
                return null;
            }

            var reader = document.Reader;
            if (!Uri.TryCreate(reader.Url, UriKind.Absolute, out var url) || url.Scheme is not (HttpScheme or HttpsScheme) || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0)
            {
                _errors.Add($"Environment '{environmentId}': reader.url must be the reader's address (e.g. http://10.0.0.10:8485, or https://proxy.homelab.example/reader-1 behind a reverse proxy), without credentials, query or fragment.");
                return null;
            }

            var variable = reader.TokenEnv?.Trim();
            if (string.IsNullOrEmpty(variable) || !CatalogValues.IsReaderTokenVariable(variable))
            {
                _errors.Add($"Environment '{environmentId}': reader.tokenEnv must be the name of the environment variable holding the reader's token, starting with {CatalogValues.ReaderTokenPrefix} (e.g. {CatalogValues.ReaderTokenPrefix}SERVER_1), never the token itself.");
                return null;
            }

            // A trailing slash makes the reader's own path a base: the routes are resolved under it, whatever it is.
            var baseUrl = url.AbsolutePath.EndsWith('/') ? url : new Uri(url.GetLeftPart(UriPartial.Path) + Path.AltDirectorySeparatorChar);
            return (DockerSection(new Uri(baseUrl, ReaderDockerPath), variable), HostMetricsSection(new Uri(baseUrl, ReaderHostPath), variable));
        }

        /// <summary>
        /// An optional <c>tokenEnv</c> on <c>docker:</c> or <c>hostMetrics:</c>: absent is fine, present must be a variable of the reader
        /// namespace, never a token and never another secret of the process (the catalog can be edited by someone who must not read them).
        /// </summary>
        private bool TryTokenVariable(string environmentId, string key, string? value, out string? variable)
        {
            if (CatalogValues.ReaderTokenError(key, value, out variable) is not { } error)
            {
                return true;
            }

            _errors.Add($"Environment '{environmentId}': {error}");
            return false;
        }

        /// <summary>The <c>hostMetrics:</c> section a reader stands for: <c>{ nodeExporter, tokenEnv }</c>, as the events of a flow mapping.</summary>
        private static ParsingEvent[] HostMetricsSection(Uri url, string variable)
        {
            return
            [
                new MappingStart(AnchorName.Empty, TagName.Empty, true, MappingStyle.Flow),
                new Scalar("nodeExporter"),
                new Scalar(url.ToString()),
                new Scalar("tokenEnv"),
                new Scalar(variable),
                new MappingEnd(),
            ];
        }

        /// <summary>The <c>docker:</c> section a reader stands for: <c>{ url, tokenEnv }</c>, as the events of a flow mapping.</summary>
        private static ParsingEvent[] DockerSection(Uri url, string variable)
        {
            return
            [
                new MappingStart(AnchorName.Empty, TagName.Empty, true, MappingStyle.Flow),
                new Scalar("url"),
                new Scalar(url.ToString()),
                new Scalar("tokenEnv"),
                new Scalar(variable),
                new MappingEnd(),
            ];
        }

        /// <summary>
        /// <c>kubernetes.nodeExporter:</c> (D-72): a port and an access mode for every node, and per-node
        /// overrides (direct, api-server, off, or a URL). <c>namespace</c> and <c>selector</c> find the exporter's
        /// pod for api-server access.
        /// </summary>
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
                    BuildRepository(id, document.Repository),
                    BuildNotes($"App '{id}'", document.Notes)));
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
        /// <c>sources: github:</c> (D-64): the accounts whose repositories are scanned, one mapping or a list, each account with
        /// its own token. Like the other connectors, the token comes from an environment variable: without it the section
        /// still loads, shown as not connected.
        /// </summary>
        private List<CodeSource>? BuildSources(GitHubSourcesDocument? document)
        {
            if (document is null)
            {
                return null;
            }

            var result = new List<CodeSource>();
            foreach (var (account, index) in document.Accounts.Select((a, i) => (a, i)))
            {
                var label = document.IsList ? $"sources.github[{index}]" : "sources.github";
                if (BuildGitHub(account, label) is { } source)
                {
                    result.Add(source);
                }
            }

            // One entry per owner: two accounts scanning the same owner would read it twice, with two tokens.
            foreach (var owner in result.OfType<GitHubSource>().SelectMany(s => s.Owners).GroupBy(o => o, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                _errors.Add($"sources.github: owner '{owner.Key}' is listed in more than one account: give each owner to one account.");
            }

            return result;
        }

        private GitHubSource BuildGitHub(GitHubDocument document, string label)
        {
            var owners = (document.Owners ?? []).Select(o => o?.Trim() ?? string.Empty).ToList();
            var include = (document.Include ?? []).Select(r => r?.Trim() ?? string.Empty).ToList();
            var exclude = (document.Exclude ?? []).Select(r => r?.Trim() ?? string.Empty).ToList();
            if (owners.Count == 0 && include.Count == 0)
            {
                _errors.Add($"{label}: list the owners to scan (users or organizations), or repositories in include.");
            }

            foreach (var owner in owners.Where(o => !OwnerPattern().IsMatch(o)))
            {
                _errors.Add($"{label}: owner '{owner}' must be a GitHub user or organization login.");
            }

            foreach (var repository in include.Where(r => !RepositoryPattern().IsMatch(r)))
            {
                _errors.Add($"{label}: include entry '{repository}' must be owner/name.");
            }

            foreach (var pattern in exclude.Where(r => !RepositoryPattern().IsMatch(r.Replace("*", "x", StringComparison.Ordinal))))
            {
                _errors.Add($"{label}: exclude entry '{pattern}' must be owner/name, * allowed (e.g. example/test-*).");
            }

            var variable = document.TokenEnv ?? GitHubSource.DefaultTokenVariable;
            if (!CatalogValues.IsVariableName(variable))
            {
                _errors.Add($"{label}: tokenEnv must be the name of an environment variable (e.g. {GitHubSource.DefaultTokenVariable}), never the token itself.");
            }

            var months = document.PushedWithinMonths ?? GitHubSource.DefaultPushedWithinMonths;
            if (months is < 0 or > 120)
            {
                _errors.Add($"{label}: pushedWithinMonths must be between 0 (no limit) and 120.");
            }

            Uri? apiUrl = null;
            if (document.ApiUrl is { } text)
            {
                apiUrl = CatalogValues.HttpUrl(text);
                if (apiUrl is not { Scheme: "https" } || apiUrl.Query.Length > 0 || apiUrl.Fragment.Length > 0)
                {
                    _errors.Add($"{label}: apiUrl must be the https address of a GitHub Enterprise Server's API (https://github.example/api/v3/), without credentials, query or fragment.");
                    apiUrl = null;
                }
                else if (!apiUrl.AbsoluteUri.EndsWith('/'))
                {
                    apiUrl = new Uri(apiUrl.AbsoluteUri + "/");
                }
            }

            return new GitHubSource(
                owners.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                variable,
                months,
                include.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                exclude,
                document.Forks ?? false,
                document.Archived ?? false,
                apiUrl);
        }

        /// <summary>Web apps are checked by default (D-50); <c>check: false</c> turns it off, a mapping overrides it.</summary>
        private CheckDefinition? BuildCheck(string appId, AppType type, CheckDocument? document)
        {
            if (document?.InvalidScalar is not null)
            {
                _errors.Add($"App '{appId}': check must be false, true or a mapping (type, url, expect, keyword, interval, timeout, critical).");
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

            if (body.Type is not null && !CheckTypes.All.Contains(body.Type))
            {
                _errors.Add($"App '{appId}': check.type must be {string.Join(" or ", CheckTypes.All)}.");
            }

            Uri? url = null;
            if (body.Url is not null && (!Uri.TryCreate(body.Url, UriKind.Absolute, out url) || url.Scheme is not (HttpScheme or HttpsScheme) || url.UserInfo.Length > 0))
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

            return _errors.Count > errors ? null : new CheckDefinition(url, expect, body.Keyword, interval, timeout, body.Critical ?? true, body.Type ?? CheckTypes.Http);
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
                if (!Uri.TryCreate(document.Href, UriKind.Absolute, out var href) || href.Scheme is not (HttpScheme or HttpsScheme))
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
            if (!Runtimes().Contains(kind))
            {
                _errors.Add($"App '{appId}': workload runtime '{runtime}' must be {RuntimeList(" or ", "@<environment id>")}.");
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
        /// A runtime matches by name; one whose connector says so (Kubernetes) also by namespace, <c>app</c> label and name
        /// prefix (D-56), and needs a name, a prefix or an app: a namespace alone would match all its pods.
        /// </summary>
        private WorkloadSelector? BuildMatch(string appId, SelectorDocument document, CatalogEnvironment environment)
        {
            var match = document.Match ?? new MatchDocument();
            var name = CatalogValues.Blank(match.Name);
            var ns = CatalogValues.Blank(match.Namespace);
            var app = CatalogValues.Blank(match.App);
            var prefix = CatalogValues.Blank(match.NamePrefix);
            var labels = match.Labels is { Count: > 0 } ? match.Labels : null;

            var fields = MatchFieldsOf(environment);
            if (!fields.HasFlag(WorkloadMatchFields.NamePrefix))
            {
                if (ns is not null || app is not null || prefix is not null || labels is not null)
                {
                    var labelled = string.Join(" or ", _connectors.Where(c => c.IsRuntime && c.MatchFields.HasFlag(WorkloadMatchFields.NamePrefix)).Select(c => c.Section));
                    _errors.Add($"App '{appId}': match.namespace, match.app, match.labels and match.namePrefix only apply to {labelled} workloads.");
                    return null;
                }

                if (name is null)
                {
                    _errors.Add($"App '{appId}': workload on {document.Runtime} needs match.name (the container, Pages project or Worker name).");
                    return null;
                }

                return new WorkloadSelector(environment.Id, name);
            }

            if (name is null && app is null && prefix is null && labels is null)
            {
                _errors.Add($"App '{appId}': workload on {document.Runtime} needs match.app (the pod's app label), match.labels, match.name or match.namePrefix, usually with match.namespace.");
                return null;
            }

            IReadOnlyDictionary<string, string>? checkedLabels = null;
            if (labels is not null && !TryBuildLabels(appId, labels, app, out checkedLabels))
            {
                return null;
            }

            return new WorkloadSelector(environment.Id, name, Namespace: ns, App: app, NamePrefix: prefix, Labels: checkedLabels);
        }

        /// <summary>The <c>labels</c> of a selector: Kubernetes label keys and values, and no disagreement with <c>app</c> (D-106).</summary>
        private bool TryBuildLabels(string appId, Dictionary<string, string> labels, string? app, out IReadOnlyDictionary<string, string>? result)
        {
            result = null;
            var valid = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in labels)
            {
                if (!CatalogValues.IsLabelKey(key.Trim()) || !CatalogValues.IsLabelValue(value?.Trim() ?? string.Empty) || string.IsNullOrWhiteSpace(value))
                {
                    _errors.Add($"App '{appId}': match.labels maps a label key to its value (e.g. {{ app.kubernetes.io/name: shop }}); '{key}' is not a Kubernetes label key and value.");
                    return false;
                }

                valid[key.Trim()] = value.Trim();
            }

            if (app is not null && valid.TryGetValue(WorkloadSelector.AppLabel, out var other) && other != app)
            {
                _errors.Add($"App '{appId}': match.app ('{app}') and match.labels.app ('{other}') disagree: give the app label once.");
                return false;
            }

            result = valid;
            return true;
        }

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

                result.Add(new CatalogStack(id, document.Name ?? id, document.Description, members, BuildNotes($"Stack '{id}'", document.Notes)));
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

        /// <summary>
        /// The data followed (D-77). Each asset belongs to a declared app, lives on a declared environment, has an
        /// importance (which gives its accepted loss window unless <c>maxAge</c> overrides it) and at least one
        /// target; a target names the sources that should prove the copy exists. Nothing is discovered.
        /// </summary>
        private List<DataAssetDefinition> BuildData(List<DataDocument> documents, Dictionary<string, CatalogEnvironment> environments, HashSet<string> appIds)
        {
            var result = new List<DataAssetDefinition>();

            foreach (var (document, index) in documents.Select((d, i) => (d, i)))
            {
                var id = RequireId(document.Id, $"data[{index}]", result.Select(d => d.Id));
                if (id is null)
                {
                    continue;
                }

                var errors = _errors.Count;
                var app = CatalogValues.Blank(document.App);
                if (app is null || !appIds.Contains(app))
                {
                    _errors.Add($"Data '{id}': app '{app}' is not a declared app; every followed data belongs to an app.");
                }

                var kind = CatalogValues.Blank(document.Kind) ?? string.Empty;
                if (!KindPattern().IsMatch(kind))
                {
                    _errors.Add($"Data '{id}': kind is required, lowercase letters, digits and dashes (e.g. postgresql, sqlite-app-config, volume).");
                }

                var where = BuildWhere(id, document.Where, environments);
                if (!Enum.TryParse(document.Importance, ignoreCase: true, out DataImportance importance) || !Enum.IsDefined(importance))
                {
                    _errors.Add($"Data '{id}': importance is required: critical, important or comfort.");
                    importance = DataImportance.Important;
                }

                var maxAge = document.MaxAge is null ? DataPolicy.DefaultMaxAge(importance) : ParseEvery(id, "maxAge", document.MaxAge);
                var targets = BuildTargets(id, document.Targets, where?.EnvironmentId, environments);

                if (_errors.Count == errors && where is not null)
                {
                    result.Add(new DataAssetDefinition(id, CatalogValues.Blank(document.Name) ?? id, app!, kind, where, importance, maxAge, targets));
                }
            }

            return result;
        }

        private DataLocation? BuildWhere(string dataId, DataWhereDocument? where, Dictionary<string, CatalogEnvironment> environments)
        {
            var environment = CatalogValues.Blank(where?.Environment);
            if (environment is null || !environments.ContainsKey(environment))
            {
                _errors.Add($"Data '{dataId}': where.environment '{environment}' is not an environment of the catalog.");
                return null;
            }

            return new DataLocation(environment, CatalogValues.Blank(where!.Namespace), CatalogValues.Blank(where.Workload), CatalogValues.Blank(where.Claim), CatalogValues.Blank(where.Path));
        }

        private TimeSpan ParseEvery(string dataId, string key, string value)
        {
            var match = EveryPattern().Match(value.Trim());
            if (!match.Success || match.Groups["value"].Value.All(c => c == '0'))
            {
                _errors.Add($"Data '{dataId}': {key} '{value}' must be a duration such as 30m, 24h or 7d.");
                return TimeSpan.Zero;
            }

            var amount = int.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
            return match.Groups["unit"].Value switch
            {
                "m" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                _ => TimeSpan.FromDays(amount),
            };
        }

        private List<DataTargetDefinition> BuildTargets(string dataId, Dictionary<string, TargetDocument?>? documents, string? dataEnvironment, Dictionary<string, CatalogEnvironment> environments)
        {
            var result = new List<DataTargetDefinition>();
            if (documents is null or { Count: 0 })
            {
                _errors.Add($"Data '{dataId}': targets must name the copies expected: local, offsite, or both.");
                return result;
            }

            foreach (var (name, document) in documents)
            {
                if (!Enum.TryParse<CopyTarget>(name, ignoreCase: true, out var target) || !Enum.IsDefined(target))
                {
                    _errors.Add($"Data '{dataId}': target '{name}' must be local or offsite.");
                    continue;
                }

                var sources = new List<BackupSourceReference>();
                AddSource(sources, BuildBackrestSource(dataId, name, document?.Backrest, dataEnvironment, environments));
                AddSource(sources, BuildK8upSource(dataId, name, document?.K8up, dataEnvironment, environments));
                AddSource(sources, BuildCronJobSource(dataId, name, document?.Cronjob, dataEnvironment, environments));
                result.Add(new DataTargetDefinition(target, sources, BuildManual(dataId, name, document?.Manual)));
            }

            return result;
        }

        private static void AddSource(List<BackupSourceReference> sources, BackupSourceReference? source)
        {
            if (source is not null)
            {
                sources.Add(source);
            }
        }

        private BackupSourceReference? BuildBackrestSource(string dataId, string target, BackrestSourceDocument? source, string? dataEnvironment, Dictionary<string, CatalogEnvironment> environments)
        {
            if (source is null)
            {
                return null;
            }

            var environment = SourceEnvironment(dataId, target, "backrest", source.Environment, dataEnvironment, environments, RuntimeNames.Kubernetes, forbidden: true);
            var plan = CatalogValues.Blank(source.Plan);
            if (plan is null || !PlanPattern().IsMatch(plan))
            {
                _errors.Add($"Data '{dataId}': {target}.backrest.plan must be the id of a Backrest plan (letters, digits, dashes, underscores).");
                return null;
            }

            return environment is null ? null : new BackupSourceReference(BackupSourceKinds.Backrest, environment, plan);
        }

        private BackupSourceReference? BuildK8upSource(string dataId, string target, K8UpSourceDocument? source, string? dataEnvironment, Dictionary<string, CatalogEnvironment> environments)
        {
            if (source is null)
            {
                return null;
            }

            var environment = SourceEnvironment(dataId, target, "k8up", source.Environment, dataEnvironment, environments, RuntimeNames.Kubernetes, forbidden: false);
            var schedule = CatalogValues.Blank(source.Schedule);
            if (schedule is null || !NamespacedNamePattern().IsMatch(schedule))
            {
                _errors.Add($"Data '{dataId}': {target}.k8up.schedule must be <namespace>/<schedule name>.");
                return null;
            }

            return environment is null ? null : new BackupSourceReference(BackupSourceKinds.K8up, environment, schedule, CatalogValues.Blank(source.Item));
        }

        private BackupSourceReference? BuildCronJobSource(string dataId, string target, string? value, string? dataEnvironment, Dictionary<string, CatalogEnvironment> environments)
        {
            var cronJob = CatalogValues.Blank(value);
            if (cronJob is null)
            {
                return null;
            }

            var environment = SourceEnvironment(dataId, target, "cronjob", null, dataEnvironment, environments, RuntimeNames.Kubernetes, forbidden: false);
            if (!NamespacedNamePattern().IsMatch(cronJob))
            {
                _errors.Add($"Data '{dataId}': {target}.cronjob must be <namespace>/<cronjob name>.");
                return null;
            }

            return environment is null ? null : new BackupSourceReference(BackupSourceKinds.CronJob, environment, cronJob);
        }

        /// <summary>
        /// The environment a source is read on: the one named, else the data's own. A Kubernetes source needs a
        /// Kubernetes environment; Backrest (<c>forbidden</c>) runs on a machine, never on a cluster.
        /// </summary>
        private string? SourceEnvironment(string dataId, string target, string tool, string? named, string? dataEnvironment, Dictionary<string, CatalogEnvironment> environments, string kubernetes, bool forbidden)
        {
            var id = CatalogValues.Blank(named) ?? dataEnvironment;
            if (id is null || !environments.TryGetValue(id, out var environment))
            {
                _errors.Add($"Data '{dataId}': {target}.{tool} environment '{id}' is not an environment of the catalog.");
                return null;
            }

            if ((environment.Runtime == kubernetes) == forbidden)
            {
                _errors.Add(forbidden
                    ? $"Data '{dataId}': {target}.{tool} is read on a machine's environment, not on '{id}' (a cluster): set environment on the source."
                    : $"Data '{dataId}': {target}.{tool} is read on a kubernetes environment, '{id}' is not one: set environment on the source.");
                return null;
            }

            return id;
        }

        private ManualCopy? BuildManual(string dataId, string target, string? value)
        {
            var text = CatalogValues.Blank(value);
            if (text is null || text.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return new ManualCopy(null);
            }

            if (DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
            {
                return new ManualCopy(date.ToUniversalTime());
            }

            _errors.Add($"Data '{dataId}': {target}.manual must be true, or the date of your last copy in ISO 8601 (e.g. 2026-01-31T03:00:00Z).");
            return null;
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
