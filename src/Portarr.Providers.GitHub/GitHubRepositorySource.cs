using System.Net;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;
using Portarr.Core.Workloads;

namespace Portarr.Providers.GitHub;

/// <summary>
/// Scans the repositories of the catalog's GitHub owners (D-64): security alerts, latest release, Dependabot
/// pull requests and the CI state of the default branch. GET requests only, with a token read from the
/// environment variable the catalog names. A fine-grained, read-only token is enough: Metadata, Contents,
/// Dependabot alerts, Code scanning alerts, Secret scanning alerts, Actions and Pull requests, all <i>Read</i>.
/// </summary>
public sealed partial class GitHubRepositorySource : ICodeRepositorySource
{
    public const string HttpClientName = "github";

    public static readonly Uri ApiBaseAddress = new("https://api.github.com/");

    public const string TokenMissing = ReasonCodes.TokenMissing;

    /// <summary>Pages read per list (100 items each): repositories of an owner, alerts of a repository.</summary>
    private const int MaxPages = 10;

    private const int Parallelism = 4;

    private const string DependabotLogin = "dependabot[bot]";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, GitHubClient> _clients = new(StringComparer.Ordinal);

    public GitHubRepositorySource(IHttpClientFactory httpClientFactory, IConfiguration configuration, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _timeProvider = timeProvider;
    }

    public bool Supports(CodeSource source) => source is GitHubSource;

    public string? GetConfigurationError(CodeSource source)
    {
        return string.IsNullOrWhiteSpace(_configuration[((GitHubSource)source).TokenVariable]) ? TokenMissing : null;
    }

    /// <summary><c>https://github.com/owner/name</c> (also <c>.git</c>, a trailing slash, any case), as <c>owner/name</c>; null for any other address.</summary>
    public string? RepositoryOfSourceLabel(string label) =>
        UrlPattern().Match(label) is { Success: true } match ? $"{match.Groups["owner"].Value}/{match.Groups["name"].Value}" : null;

    /// <summary>A GHCR or Docker Hub image is published under the name of its repository (<c>ghcr.io/owner/name</c>).</summary>
    public string? RepositoryOfImage(ImageReference image) => image.Registry is "ghcr.io" or ImageReference.DockerHub ? image.Repository : null;

    [GeneratedRegex("^https?://github\\.com/(?<owner>[A-Za-z0-9-]+)/(?<name>[A-Za-z0-9._-]+?)(\\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    public async Task<IReadOnlyList<CodeRepository>> ScanAsync(CodeSource codeSource, CancellationToken cancellationToken)
    {
        var source = (GitHubSource)codeSource;
        if (GetConfigurationError(source) is { } error)
        {
            throw new InvalidOperationException($"GitHub source is not configured: {error}.");
        }

        // One client per API host: the rate-limit window belongs to the host, and github.com's must not stop a Server's.
        var apiBase = source.ApiUrl ?? _httpClientFactory.CreateClient(HttpClientName).BaseAddress ?? ApiBaseAddress;
        var session = new Session(this, _configuration[source.TokenVariable]!, apiBase, _clients.GetOrAdd(apiBase.Authority, _ => new GitHubClient(_timeProvider)));
        var repositories = await ListRepositoriesAsync(session, source, cancellationToken);

        var results = new CodeRepository[repositories.Count];
        await Parallel.ForEachAsync(
            repositories.Select((repository, index) => (repository, index)),
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
            async (item, token) => results[item.index] = await DescribeOrDegradeAsync(session, item.repository, token));

        return results;
    }

    private async Task<List<GitHubRepository>> ListRepositoriesAsync(Session session, GitHubSource source, CancellationToken cancellationToken)
    {
        var since = source.PushedWithinMonths == 0 ? (DateTimeOffset?)null : _timeProvider.GetUtcNow().AddMonths(-source.PushedWithinMonths);
        var viewer = (await session.GetAsync("user", cancellationToken)).Read(GitHubJsonContext.Default.GitHubAccount)?.Login;
        var found = new Dictionary<string, GitHubRepository>(StringComparer.OrdinalIgnoreCase);

        foreach (var owner in source.Owners)
        {
            var account = await session.GetAsync($"users/{Uri.EscapeDataString(owner)}", cancellationToken);
            if (!account.IsSuccess)
            {
                throw new HttpRequestException($"GitHub owner {owner} could not be read.", null, account.Status);
            }

            var path = RepositoriesPath(owner, viewer, account.Read(GitHubJsonContext.Default.GitHubAccount)?.Type == "Organization");

            foreach (var repository in await session.ListAsync(path, GitHubJsonContext.Default.ListGitHubRepository, cancellationToken))
            {
                var kept = string.Equals(repository.Owner.Login, owner, StringComparison.OrdinalIgnoreCase)
                    && (source.Archived || !repository.Archived)
                    && (source.Forks || !repository.Fork)
                    && (since is null || repository.PushedAt >= since);
                if (kept && !source.IsExcluded(repository.FullName))
                {
                    found.TryAdd(repository.FullName, repository);
                }
            }
        }

        // Included repositories bypass the activity and ownership filters, not the exclusions.
        foreach (var name in source.Include.Where(n => !found.ContainsKey(n) && !source.IsExcluded(n)))
        {
            var answer = await session.GetAsync($"repos/{name}", cancellationToken);
            if (answer.Read(GitHubJsonContext.Default.GitHubRepository) is { } repository)
            {
                found.TryAdd(repository.FullName, repository);
            }
        }

        return found.Values.OrderByDescending(r => r.PushedAt).ToList();
    }

    /// <summary>Where an owner's repositories are listed: an organization's, the token owner's own (private ones included), or another user's.</summary>
    private static string RepositoriesPath(string owner, string? viewer, bool organization)
    {
        if (organization)
        {
            return $"orgs/{Uri.EscapeDataString(owner)}/repos?type=all&sort=pushed&per_page=100";
        }

        return string.Equals(owner, viewer, StringComparison.OrdinalIgnoreCase)
            ? "user/repos?affiliation=owner&visibility=all&sort=pushed&per_page=100"
            : $"users/{Uri.EscapeDataString(owner)}/repos?type=owner&sort=pushed&per_page=100";
    }

    /// <summary>One repository whose answers cannot be read is listed with what is known and its scans unknown, instead of failing the whole scan.</summary>
    private static async Task<CodeRepository> DescribeOrDegradeAsync(Session session, GitHubRepository repository, CancellationToken cancellationToken)
    {
        try
        {
            return await DescribeAsync(session, repository, cancellationToken);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException)
        {
            var unknown = AlertScan.Unknown(ReasonCodes.From(exception));
            return new CodeRepository(
                repository.Owner.Login,
                repository.Name,
                new Uri(repository.HtmlUrl),
                repository.Visibility ?? (repository.Private ? "private" : "public"),
                repository.Language,
                repository.Description,
                repository.PushedAt,
                repository.DefaultBranch ?? "main",
                repository.Archived,
                repository.Fork,
                unknown,
                unknown,
                unknown,
                null,
                null,
                null,
                PullsUrl(repository));
        }
    }

    /// <summary>Where the open pull requests of Dependabot are listed on the host's web page of the repository.</summary>
    private static Uri PullsUrl(GitHubRepository repository)
    {
        var page = new Uri(repository.HtmlUrl);
        return new Uri(page, $"{page.AbsolutePath.TrimEnd('/')}/pulls/app%2Fdependabot");
    }

    private static async Task<CodeRepository> DescribeAsync(Session session, GitHubRepository repository, CancellationToken cancellationToken)
    {
        var path = $"repos/{repository.FullName}";
        var branch = repository.DefaultBranch ?? "main";

        var dependabot = await ScanAsync(session, $"{path}/dependabot/alerts?state=open&per_page=100", GitHubJsonContext.Default.ListDependabotAlert, alerts => alerts.Select(a => a.SecurityAdvisory?.Severity), cancellationToken);
        var codeScanning = await ScanAsync(session, $"{path}/code-scanning/alerts?state=open&per_page=100", GitHubJsonContext.Default.ListCodeScanningAlert, alerts => alerts.Select(a => a.Rule?.SecuritySeverityLevel), cancellationToken);
        var secrets = await ScanAsync(session, $"{path}/secret-scanning/alerts?state=open&per_page=100&hide_secret=true", GitHubJsonContext.Default.ListSecretScanningAlert, alerts => alerts.Select(_ => (string?)null), cancellationToken);

        return new CodeRepository(
            repository.Owner.Login,
            repository.Name,
            new Uri(repository.HtmlUrl),
            repository.Visibility ?? (repository.Private ? "private" : "public"),
            repository.Language,
            repository.Description,
            repository.PushedAt,
            branch,
            repository.Archived,
            repository.Fork,
            dependabot,
            codeScanning,
            secrets,
            await ReleaseAsync(session, path, cancellationToken),
            await DependabotPullRequestsAsync(session, path, cancellationToken),
            await CiAsync(session, path, branch, cancellationToken),
            PullsUrl(repository));
    }

    /// <summary>
    /// One security feature: its open alerts by severity, <c>Off</c> when GitHub says it is disabled or was
    /// never set up (no analysis), <c>Unknown</c> when the token may not read it.
    /// </summary>
    private static async Task<AlertScan> ScanAsync<T>(Session session, string path, JsonTypeInfo<List<T>> type, Func<List<T>, IEnumerable<string?>> severities, CancellationToken cancellationToken)
    {
        try
        {
            var first = await session.GetAsync(path, cancellationToken);
            if (!first.IsSuccess)
            {
                return Classify(first);
            }

            var alerts = await session.ListAsync(first, type, cancellationToken);
            return AlertScan.On(AlertCounts.From(severities(alerts)));
        }
        catch (HttpRequestException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return AlertScan.Unknown(Reason(exception));
        }
    }

    /// <summary>
    /// GitHub says a feature is off in words: "Dependabot alerts are disabled", "no analysis found", "Code
    /// Security must be enabled", "Secret scanning is disabled". Anything else is an access problem.
    /// </summary>
    internal static AlertScan Classify(GitHubResponse response)
    {
        var message = response.Message ?? string.Empty;
        string[] off = ["disabled", "not enabled", "must be enabled", "no analysis found", "not available"];
        if (response.Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound && off.Any(m => message.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            return AlertScan.Off;
        }

        return AlertScan.Unknown(response.Status == HttpStatusCode.TooManyRequests ? "rate-limited" : $"http-{(int)response.Status}");
    }

    private static async Task<ReleaseInfo?> ReleaseAsync(Session session, string path, CancellationToken cancellationToken)
    {
        try
        {
            var release = (await session.GetAsync($"{path}/releases/latest", cancellationToken)).Read(GitHubJsonContext.Default.GitHubRelease);
            if (release is not null)
            {
                return new ReleaseInfo(release.TagName, release.PublishedAt, Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var url) ? url : null, false);
            }

            var tag = (await session.GetAsync($"{path}/tags?per_page=1", cancellationToken)).Read(GitHubJsonContext.Default.ListGitHubTag)?.FirstOrDefault();
            return tag is null ? null : new ReleaseInfo(tag.Name, null, null, true);
        }
        catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<int?> DependabotPullRequestsAsync(Session session, string path, CancellationToken cancellationToken)
    {
        try
        {
            var pulls = (await session.GetAsync($"{path}/pulls?state=open&per_page=100", cancellationToken)).Read(GitHubJsonContext.Default.ListPullRequest);
            return pulls?.Count(p => p.User?.Login == DependabotLogin);
        }
        catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The last workflow run started by a push to the default branch (Dependabot and pull request runs say nothing about it).</summary>
    private static async Task<CiRun?> CiAsync(Session session, string path, string branch, CancellationToken cancellationToken)
    {
        try
        {
            var runs = (await session.GetAsync($"{path}/actions/runs?branch={Uri.EscapeDataString(branch)}&event=push&per_page=1&exclude_pull_requests=true", cancellationToken))
                .Read(GitHubJsonContext.Default.WorkflowRuns);
            var run = runs?.Runs?.FirstOrDefault();
            if (run is null)
            {
                return null;
            }

            Uri.TryCreate(run.HtmlUrl, UriKind.Absolute, out var url);
            return new CiRun(run.Name, run.Status ?? "unknown", run.Conclusion, run.CreatedAt, url);
        }
        catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string Reason(HttpRequestException exception)
    {
        return exception.StatusCode == HttpStatusCode.TooManyRequests ? "rate-limited" : ReasonCodes.From(exception);
    }

    /// <summary>The calls of one scan, with its token.</summary>
    private sealed class Session(GitHubRepositorySource owner, string token, Uri apiBase, GitHubClient client)
    {
        public Task<GitHubResponse> GetAsync(string path, CancellationToken cancellationToken)
        {
            return client.GetAsync(owner._httpClientFactory.CreateClient(HttpClientName), apiBase, token, path, cancellationToken);
        }

        public async Task<List<T>> ListAsync<T>(string path, JsonTypeInfo<List<T>> type, CancellationToken cancellationToken)
        {
            var first = await GetAsync(path, cancellationToken);
            if (!first.IsSuccess)
            {
                throw new HttpRequestException($"GitHub list {path} failed.", null, first.Status);
            }

            return await ListAsync(first, type, cancellationToken);
        }

        public async Task<List<T>> ListAsync<T>(GitHubResponse first, JsonTypeInfo<List<T>> type, CancellationToken cancellationToken)
        {
            var items = first.Read(type) ?? [];
            var next = first.Next;
            for (var page = 1; next is not null && page < MaxPages; page++)
            {
                var answer = await GetAsync(next, cancellationToken);
                if (!answer.IsSuccess)
                {
                    break;
                }

                items.AddRange(answer.Read(type) ?? []);
                next = answer.Next;
            }

            return items;
        }
    }
}
