using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Maintenance;
using Portarr.Providers.GitHub;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// V4 over the real HTTP pipeline: the Maintenance report built from the GitHub API (played by WireMock) for the
/// owners the catalog names. Filters, alert counts and rate limits are unit-tested (GitHubRepositorySourceFixture,
/// MaintenanceServiceFixture); this proves the wiring from catalog and token to the report and the roles.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MaintenanceToolsFixture : IAsyncLifetime
{
    private const string Catalog = """
        sources:
          github:
            owners: [example-org]
            tokenEnv: GITHUB_TOKEN
        apps:
          - { id: shop, name: Shop, repository: example-org/shop }
        """;

    private readonly TestDatabase _database;
    private readonly ToolServer _github = new();
    private PortarrFactory _factory = null!;
    private HttpClient _client = null!;

    public MaintenanceToolsFixture(TestDatabase database)
    {
        _database = database;
    }

    public async ValueTask InitializeAsync()
    {
        _github
            .Serve("GET", "/user", """{ "login": "someone" }""")
            .Serve("GET", "/users/example-org", """{ "login": "example-org", "type": "Organization" }""")
            .Serve("GET", "/orgs/example-org/repos", $"[{Repo("shop")}, {Repo("tool")}]")
            .Serve("GET", "/repos/example-org/shop/dependabot/alerts", """[{ "security_advisory": { "severity": "critical" } }, { "security_advisory": { "severity": "medium" } }]""")
            .Serve("GET", "/repos/example-org/tool/dependabot/alerts", """{ "message": "Dependabot alerts are disabled for this repository." }""", status: 403)
            .Serve("GET", "/repos/example-org/shop/pulls", """[{ "user": { "login": "dependabot[bot]" } }]""")
            .Serve("GET", "/repos/example-org/shop/actions/runs", """{ "workflow_runs": [{ "name": "CI", "status": "completed", "conclusion": "failure" }] }""");

        var options = await _database.CreateOptionsAsync();
        await File.WriteAllTextAsync(Path.Combine(options.DataFolder, "catalog.yaml"), Catalog);
        _factory = new PortarrFactory(
            options,
            new Dictionary<string, string?> { ["GITHUB_TOKEN"] = "gh-token" },
            services =>
            {
                services.AddSingleton<ICodeRepositorySource, GitHubRepositorySource>();
                services.AddHttpClient(GitHubRepositorySource.HttpClientName).ConfigureHttpClient(client => client.BaseAddress = new Uri(_github.Url + "/"));
            });
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _github.Dispose();
    }

    [Fact]
    public async Task should_build_the_report_from_the_repositories_of_the_owners_in_the_catalog()
    {
        var report = await ScannedReportAsync();

        report.GetProperty("codeSource").GetProperty("configured").GetBoolean().Should().BeTrue();
        report.GetProperty("codeSource").GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        var repositories = report.GetProperty("repositories").EnumerateArray().ToDictionary(r => r.GetProperty("fullName").GetString()!);
        repositories.Keys.Should().BeEquivalentTo("example-org/shop", "example-org/tool");

        var shop = repositories["example-org/shop"];
        shop.GetProperty("deployed").GetBoolean().Should().BeTrue("the app declares this repository");
        shop.GetProperty("appIds").EnumerateArray().Select(a => a.GetString()).Should().Equal("shop");
        shop.GetProperty("dependabot").GetProperty("critical").GetInt32().Should().Be(1);
        shop.GetProperty("dependabotPullRequests").GetInt32().Should().Be(1);
        shop.GetProperty("ci").GetProperty("failed").GetBoolean().Should().BeTrue();
        repositories["example-org/tool"].GetProperty("deployed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task should_summarize_what_needs_attention_with_off_features_counted_apart_from_alerts()
    {
        var report = await ScannedReportAsync();

        var summary = report.GetProperty("summary");
        summary.GetProperty("repositories").GetInt32().Should().Be(2);
        summary.GetProperty("critical").GetInt32().Should().Be(1);
        summary.GetProperty("ciFailing").GetInt32().Should().Be(1);
        summary.GetProperty("dependabotOff").GetInt32().Should().Be(1, "GitHub says Dependabot alerts are disabled on the tool repository");
        report.GetProperty("attention").EnumerateArray().Select(a => a.GetProperty("kind").GetString()).Should().Contain(["severe-alerts", "ci-failing"]);
    }

    [Fact]
    public async Task should_only_read_github_with_the_token_of_the_environment()
    {
        await ScannedReportAsync();

        _github.Calls.Should().NotBeEmpty().And.OnlyContain(c => c.StartsWith("GET ", StringComparison.Ordinal), "Portarr never writes to GitHub");
        _github.Authorizations.Should().OnlyContain(a => a == "Bearer gh-token");
    }

    private async Task<JsonElement> ScannedReportAsync()
    {
        var accepted = await _client.PostAsync("/api/v1/maintenance/refresh", null, TestContext.Current.CancellationToken);
        accepted.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);

        // The scan runs in the background: wait for its result.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var report = await _client.GetFromJsonAsync<JsonElement>("/api/v1/maintenance", TestContext.Current.CancellationToken);
            if (report.GetProperty("repositories").GetArrayLength() > 0 || DateTime.UtcNow > deadline)
            {
                return report;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private static string Repo(string name) => $$"""
        { "full_name": "example-org/{{name}}", "name": "{{name}}", "owner": { "login": "example-org" }, "html_url": "https://github.com/example-org/{{name}}",
          "pushed_at": "2025-12-20T00:00:00Z", "default_branch": "main", "archived": false, "fork": false, "private": false, "visibility": "public" }
        """;
}
