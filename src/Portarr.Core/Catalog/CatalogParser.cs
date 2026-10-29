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
    private const string DockerRuntimePrefix = "docker@";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
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

    private sealed class Builder
    {
        private readonly List<string> _errors = [];

        public CatalogParseResult Build(CatalogDocument document)
        {
            var environments = BuildEnvironments(document.Environments ?? []);
            var environmentIds = environments.Select(e => e.Id).ToHashSet();

            var appDocuments = document.Apps ?? [];
            var apps = BuildApps(appDocuments, environmentIds);
            var appIds = apps.Select(a => a.Id).ToHashSet();

            var stacks = BuildStacks(document.Stacks ?? [], appDocuments, appIds);

            return _errors.Count == 0
                ? new CatalogParseResult(new Catalog(environments, stacks, apps), [])
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

                DockerEndpoint? docker = null;
                if (document.Docker is not null)
                {
                    if (Uri.TryCreate(document.Docker.Url, UriKind.Absolute, out var url) && url.Scheme is "unix" or "http" or "https" or "tcp")
                    {
                        docker = new DockerEndpoint(url);
                    }
                    else
                    {
                        _errors.Add($"Environment '{id}': docker.url must be unix:///path/to/docker.sock or http(s)://host:port.");
                    }
                }

                result.Add(new CatalogEnvironment(id, document.Name ?? id, docker, string.IsNullOrWhiteSpace(document.Address) ? null : document.Address.Trim()));
            }

            return result;
        }

        private List<CatalogApp> BuildApps(List<AppDocument> documents, HashSet<string> environmentIds)
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
                    BuildSelectors(id, document.Workloads ?? [], environmentIds)));
            }

            return result;
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

        private List<WorkloadSelector> BuildSelectors(string appId, List<SelectorDocument> documents, HashSet<string> environmentIds)
        {
            var result = new List<WorkloadSelector>();

            foreach (var document in documents)
            {
                var runtime = document.Runtime ?? string.Empty;
                if (!runtime.StartsWith(DockerRuntimePrefix, StringComparison.Ordinal))
                {
                    _errors.Add($"App '{appId}': workload runtime '{runtime}' must be docker@<environment id>.");
                    continue;
                }

                var environmentId = runtime[DockerRuntimePrefix.Length..];
                if (!environmentIds.Contains(environmentId))
                {
                    _errors.Add($"App '{appId}': environment '{environmentId}' is not declared in environments.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(document.Match?.Name))
                {
                    _errors.Add($"App '{appId}': workload on {runtime} needs match.name (the container name).");
                    continue;
                }

                var role = MemberRole.Critical;
                if (document.Role is not null && !Enum.TryParse(document.Role, ignoreCase: true, out role))
                {
                    _errors.Add($"App '{appId}': role of workload '{document.Match.Name}' must be critical or optional.");
                }

                result.Add(new WorkloadSelector(environmentId, document.Match.Name, role));
            }

            return result;
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
