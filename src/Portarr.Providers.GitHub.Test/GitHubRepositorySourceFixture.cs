using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;

namespace Portarr.Providers.GitHub.Test;

/// <summary>The GitHub connector (D-64): GET only, filters, security features on or off, conditional requests.</summary>
public sealed class GitHubRepositorySourceFixture
{
    private const string Api = "https://api.github.com";

    private static readonly GitHubSource Source = new(["example"], "GH_TOKEN", 6, [], ["example/test-*"], false, false);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly Dictionary<string, Answer> _answers = [];
    private readonly Dictionary<string, string?> _configuration = new() { ["GH_TOKEN"] = "test-token" };

    public GitHubRepositorySourceFixture()
    {
        Ok("/user", """{ "login": "someone" }""");
        Ok("/users/example", """{ "login": "example", "type": "Organization" }""");
        Ok("/orgs/example/repos?type=all&sort=pushed&per_page=100", $"[{Repo("shop", "2026-01-20T00:00:00Z")}, {Repo("old", "2025-01-01T00:00:00Z")}, {Repo("archived", "2026-01-20T00:00:00Z", archived: true)}, {Repo("fork", "2026-01-20T00:00:00Z", fork: true)}, {Repo("test-x", "2026-01-20T00:00:00Z")}]");
        Ok("/repos/example/shop/dependabot/alerts?state=open&per_page=100", """[{ "security_advisory": { "severity": "critical" } }, { "security_advisory": { "severity": "medium" } }]""");
        _answers["/repos/example/shop/code-scanning/alerts?state=open&per_page=100"] = new(HttpStatusCode.NotFound, """{ "message": "no analysis found" }""");
        _answers["/repos/example/shop/secret-scanning/alerts?state=open&per_page=100&hide_secret=true"] = new(HttpStatusCode.NotFound, """{ "message": "Secret scanning is disabled on this repository." }""");
        Ok("/repos/example/shop/tags?per_page=1", """[{ "name": "v1.2.0" }]""");
        Ok("/repos/example/shop/pulls?state=open&per_page=100", """[{ "user": { "login": "dependabot[bot]" } }, { "user": { "login": "someone" } }]""");
        Ok("/repos/example/shop/actions/runs?branch=main&event=push&per_page=1&exclude_pull_requests=true", """{ "workflow_runs": [{ "name": "CI", "status": "completed", "conclusion": "failure", "html_url": "https://github.com/example/shop/actions/runs/1" }] }""");
    }

    [Fact]
    public async Task should_scan_the_active_repositories_of_an_owner_with_get_requests_and_a_bearer_token()
    {
        var repositories = await Subject().ScanAsync(Source, TestContext.Current.CancellationToken);

        var shop = repositories.Should().ContainSingle("old, archived, forked and excluded repositories are filtered out").Subject;
        shop.Dependabot.Should().Be(AlertScan.On(new AlertCounts(1, 0, 1, 0, 0)));
        shop.CodeScanning.State.Should().Be(ScanningState.Off, "no analysis means code scanning was never set up");
        shop.SecretScanning.State.Should().Be(ScanningState.Off);
        shop.Release.Should().Be(new ReleaseInfo("v1.2.0", null, null, true));
        shop.DependabotPullRequests.Should().Be(1);
        shop.Ci!.Failed.Should().BeTrue();
        _requests.Should().AllSatisfy(r =>
        {
            r.Method.Should().Be(HttpMethod.Get);
            r.Headers.Authorization!.ToString().Should().Be("Bearer test-token");
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Dependabot alerts are disabled for this repository.", ScanningState.Off, null)]
    [InlineData(HttpStatusCode.Forbidden, "Code Security must be enabled for this repository to use code scanning.", ScanningState.Off, null)]
    [InlineData(HttpStatusCode.Forbidden, "Resource not accessible by personal access token", ScanningState.Unknown, "http-403")]
    [InlineData(HttpStatusCode.TooManyRequests, "API rate limit exceeded", ScanningState.Unknown, "rate-limited")]
    public void should_tell_a_feature_that_is_off_from_one_the_token_cannot_read(HttpStatusCode status, string message, ScanningState state, string? reason)
    {
        var scan = GitHubRepositorySource.Classify(new GitHubResponse(status, [], null, message));

        scan.State.Should().Be(state);
        scan.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task should_ask_again_with_the_etag_and_reuse_an_unchanged_answer()
    {
        var source = Subject();
        await source.ScanAsync(Source, TestContext.Current.CancellationToken);
        _requests.Clear();

        var repositories = await source.ScanAsync(Source, TestContext.Current.CancellationToken);

        repositories.Should().ContainSingle().Which.Dependabot.Counts.Critical.Should().Be(1);
        _requests.Where(r => r.RequestUri!.AbsolutePath.EndsWith("/dependabot/alerts", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Headers.IfNoneMatch.Should().ContainSingle();
    }

    [Fact]
    public async Task should_follow_next_pages_on_the_api_host_only()
    {
        _answers["/repos/example/shop/dependabot/alerts?state=open&per_page=100"] = new(HttpStatusCode.OK, """[{ "security_advisory": { "severity": "high" } }]""", $"<{Api}/repositories/1/dependabot/alerts?after=x>; rel=\"next\"");
        _answers["/repositories/1/dependabot/alerts?after=x"] = new(HttpStatusCode.OK, """[{ "security_advisory": { "severity": "low" } }]""", "<https://elsewhere.example/steal>; rel=\"next\"");

        var shop = (await Subject().ScanAsync(Source, TestContext.Current.CancellationToken)).Single();

        shop.Dependabot.Counts.Should().Be(new AlertCounts(0, 1, 0, 1, 0));
        _requests.Should().OnlyContain(r => r.RequestUri!.Host == "api.github.com");
    }

    [Fact]
    public async Task should_never_ask_for_the_secrets_themselves()
    {
        await Subject().ScanAsync(Source, TestContext.Current.CancellationToken);

        _requests.Where(r => r.RequestUri!.AbsolutePath.EndsWith("/secret-scanning/alerts", StringComparison.Ordinal))
            .Should().ContainSingle().Which.RequestUri!.Query.Should().Contain("hide_secret=true", "a leaked secret in clear text would sit in the ETag cache");
    }

    [Fact]
    public async Task should_replace_a_cached_answer_that_changed_even_when_it_has_the_same_length()
    {
        var source = Subject();
        await source.ScanAsync(Source, TestContext.Current.CancellationToken);

        _answers["/repos/example/shop/dependabot/alerts?state=open&per_page=100"] = new(HttpStatusCode.OK, """[{ "security_advisory": { "severity": "high" } }, { "security_advisory": { "severity": "low" } }]""");
        var shop = (await source.ScanAsync(Source, TestContext.Current.CancellationToken)).Single();

        shop.Dependabot.Counts.Should().Be(new AlertCounts(0, 1, 0, 1, 0), "the new answer, not the cached one");
    }

    [Fact]
    public async Task should_stop_when_the_token_is_rejected()
    {
        _answers["/user"] = new(HttpStatusCode.Unauthorized, """{ "message": "Bad credentials" }""");
        _answers["/users/example"] = new(HttpStatusCode.Unauthorized, """{ "message": "Bad credentials" }""");

        var scan = () => Subject().ScanAsync(Source, TestContext.Current.CancellationToken);

        (await scan.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_list_the_private_repositories_of_the_token_owner_and_leave_other_owners_alone()
    {
        Ok("/users/someone", """{ "login": "someone", "type": "User" }""");
        Ok("/user/repos?affiliation=owner&visibility=all&sort=pushed&per_page=100", """[{ "name": "notes", "full_name": "someone/notes", "owner": { "login": "someone" }, "html_url": "https://github.com/someone/notes", "private": true, "pushed_at": "2026-01-20T00:00:00Z", "default_branch": "main", "archived": false, "fork": false }]""");
        var mine = Source with { Owners = ["someone"] };

        var repositories = await Subject().ScanAsync(mine, TestContext.Current.CancellationToken);

        repositories.Should().ContainSingle().Which.Should().Match<CodeRepository>(r => r.FullName == "someone/notes" && r.Visibility == "private");
    }

    [Fact]
    public async Task should_scan_an_included_repository_that_no_owner_covers_unless_it_is_excluded()
    {
        Ok("/repos/other/lib", """{ "name": "lib", "full_name": "other/lib", "owner": { "login": "other" }, "html_url": "https://github.com/other/lib", "private": false, "pushed_at": "2020-01-01T00:00:00Z", "default_branch": "main", "archived": false, "fork": false }""");
        var source = Source with { Include = ["other/lib", "example/test-y"] };

        var repositories = await Subject().ScanAsync(source, TestContext.Current.CancellationToken);

        repositories.Select(r => r.FullName).Should().BeEquivalentTo("example/shop", "other/lib");
        _requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == "/repos/example/test-y", "an excluded repository is not even read");
    }

    [Fact]
    public async Task should_scan_forks_and_archived_repositories_when_asked_to()
    {
        var everything = Source with { Forks = true, Archived = true };

        var repositories = await Subject().ScanAsync(everything, TestContext.Current.CancellationToken);

        repositories.Select(r => r.FullName).Should().BeEquivalentTo("example/shop", "example/archived", "example/fork");
    }

    [Fact]
    public async Task should_list_a_repository_whose_answers_cannot_be_read_with_its_scans_unknown_and_scan_the_others()
    {
        Ok("/orgs/example/repos?type=all&sort=pushed&per_page=100", $"[{Repo("shop", "2026-01-20T00:00:00Z")}, {Repo("broken", "2026-01-19T00:00:00Z")}]");
        Ok("/repos/example/broken/dependabot/alerts?state=open&per_page=100", "this is not json");

        var repositories = await Subject().ScanAsync(Source, TestContext.Current.CancellationToken);

        repositories.Select(r => r.FullName).Should().BeEquivalentTo("example/shop", "example/broken");
        repositories.Single(r => r.Name == "shop").Dependabot.State.Should().Be(ScanningState.On);
        repositories.Single(r => r.Name == "broken").Should().Match<CodeRepository>(r => r.Dependabot.State == ScanningState.Unknown && r.CodeScanning.State == ScanningState.Unknown);
    }

    [Fact]
    public void should_report_a_missing_token()
    {
        _configuration.Clear();

        Subject().GetConfigurationError(Source).Should().Be(GitHubRepositorySource.TokenMissing);
    }

    private void Ok(string path, string body) => _answers[path] = new Answer(HttpStatusCode.OK, body);

    private static string Repo(string name, string pushedAt, bool archived = false, bool fork = false) => $$"""
        { "name": "{{name}}", "full_name": "example/{{name}}", "owner": { "login": "example" }, "html_url": "https://github.com/example/{{name}}",
          "private": false, "visibility": "public", "language": "C#", "pushed_at": "{{pushedAt}}", "default_branch": "main",
          "archived": {{(archived ? "true" : "false")}}, "fork": {{(fork ? "true" : "false")}} }
        """;

    private GitHubRepositorySource Subject()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(GitHubRepositorySource.HttpClientName)
            .Returns(_ => new HttpClient(new Handler(this)) { BaseAddress = GitHubRepositorySource.ApiBaseAddress });

        return new GitHubRepositorySource(factory, new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build(), _clock);
    }

    private sealed record Answer(HttpStatusCode Status, string Body, string? Link = null);

    private sealed class Handler(GitHubRepositorySourceFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (fixture._requests)
            {
                fixture._requests.Add(request);
            }

            var path = request.RequestUri!.PathAndQuery;
            if (!fixture._answers.TryGetValue(path, out var answer))
            {
                answer = new Answer(HttpStatusCode.NotFound, """{ "message": "Not Found" }""");
            }

            var etag = $"\"{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(answer.Body)))[..16]}\"";
            if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == etag))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }

            var response = new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Body, Encoding.UTF8, "application/json") };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
            if (answer.Link is not null)
            {
                response.Headers.Add("Link", answer.Link);
            }

            return Task.FromResult(response);
        }
    }
}
