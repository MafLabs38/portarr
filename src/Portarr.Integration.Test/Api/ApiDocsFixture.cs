using System.Net;
using System.Text.Json;

namespace Portarr.Integration.Test.Api;

/// <summary>The OpenAPI document and the interactive reference (F-API-01).</summary>
[Trait("Category", "Integration")]
public sealed class ApiDocsFixture : IAsyncLifetime
{
    private readonly TestDatabase _database;
    private PortarrFactory _factory = null!;

    public ApiDocsFixture(TestDatabase database)
    {
        _database = database;
    }

    public async ValueTask InitializeAsync()
    {
        _factory = new PortarrFactory(await _database.CreateOptionsAsync());
    }

    public ValueTask DisposeAsync()
    {
        return _factory.DisposeAsync();
    }

    [Fact]
    public async Task should_serve_an_openapi_document_of_the_api_v1_endpoints_only()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        paths.Should().Contain(["/api/v1/maintenance", "/api/v1/maintenance/refresh", "/api/v1/apps/{id}"]);
        paths.Should().OnlyContain(path => path.StartsWith("/api/v1/", StringComparison.Ordinal));
        var refresh = document.RootElement.GetProperty("paths").GetProperty("/api/v1/maintenance/refresh").GetProperty("post");
        refresh.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
        refresh.GetProperty("responses").EnumerateObject().Select(r => r.Name).Should().BeEquivalentTo("202", "429");
    }

    [Fact]
    public async Task should_serve_the_reference_page_with_its_own_script_policy_and_no_third_party_host()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/docs/", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.Should().Contain("script-src 'self' 'nonce-").And.NotContain("script-src 'self' 'unsafe-inline'").And.NotContain("http");
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var nonce = policy.Split("'nonce-")[1].Split('\'')[0];
        html.Should().Contain($"nonce=\"{nonce}\"");
    }

    [Fact]
    public async Task should_keep_the_strict_global_policy_on_the_other_pages()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/apps", TestContext.Current.CancellationToken);

        response.Headers.GetValues("Content-Security-Policy").Single().Should().NotContain("unsafe-inline");
    }
}
