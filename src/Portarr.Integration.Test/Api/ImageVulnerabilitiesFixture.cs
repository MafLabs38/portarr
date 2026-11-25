using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Maintenance;
using Portarr.Core.Workloads;
using Portarr.Providers.Trivy;

namespace Portarr.Integration.Test.Api;

/// <summary>The one pass MaintenanceVulnerabilitiesFixture reads: the scanner companion played by a fake server, the monitor run once, the report fetched once.</summary>
public sealed class ImageVulnerabilitiesScan : IAsyncLifetime
{
    public const string Secret = "integration-secret-integration-secret";

    private const string Catalog = """
        imageScanner: { url: "http://scanner.homelab.example:8489", allowInsecure: true }
        environments:
          - { id: server-1, name: Server 1, address: 10.0.0.10, docker: { url: "http://socket-proxy:2375" } }
        """;

    private readonly TestDatabase _database;
    private PortarrFactory _factory = null!;

    public ImageVulnerabilitiesScan(TestDatabase database)
    {
        _database = database;
    }

    public ToolServer Scanner { get; } = new();

    public JsonElement Report { get; private set; }

    public async ValueTask InitializeAsync()
    {
        Scanner
            .Serve("POST", "/v1/scans", """{ "queued": 2 }""", status: 202)
            .Serve("GET", "/v1/results", """
                { "generatedAt": "2026-11-25T10:00:00Z", "database": { "version": "2", "updatedAt": "2026-11-25T03:00:00Z" },
                  "images": [
                    { "kind": "image", "ref": "docker.io/library/idp:1", "state": "scanned", "scannedAt": "2026-11-25T09:00:00Z",
                      "counts": { "critical": 1, "high": 2, "medium": 3, "low": 0, "unknown": 0 }, "fixable": { "critical": 1, "high": 1, "medium": 0, "low": 0, "unknown": 0 },
                      "findings": [ { "id": "CVE-2026-0001", "package": "openssl", "installedVersion": "3.0.1", "fixedVersion": "3.0.2", "severity": "critical", "title": "A flaw", "url": "https://avd.example/cve-2026-0001" } ] },
                    { "kind": "image", "ref": "docker.io/library/local:1", "state": "failed", "reason": "not-found", "counts": {}, "fixable": {}, "findings": [] }
                  ] }
                """);

        var options = await _database.CreateOptionsAsync();
        await File.WriteAllTextAsync(Path.Combine(options.DataFolder, "catalog.yaml"), Catalog);
        _factory = new PortarrFactory(
            options,
            new Dictionary<string, string?> { ["PORTARR_SCANNER_SECRET"] = Secret },
            services =>
            {
                services.AddSingleton<IImageScanner, TrivyImageScanner>();
                // The fake server is on the loopback address, which the real client refuses (outbound address policy).
                services.AddHttpClient(TrivyImageScanner.HttpClientName)
                    .ConfigureHttpClient(client => client.BaseAddress = new Uri(Scanner.Url + "/"))
                    .ConfigurePrimaryHttpMessageHandler(() => new RedirectingHandler(new Uri(Scanner.Url)));
            });
        _factory.Workloads.Workloads["server-1"] = [FakeWorkloadSource.Container("server-1", "idp") with { Image = "idp:1" }, FakeWorkloadSource.Container("server-1", "local") with { Image = "local:1" }];

        await _factory.Services.GetRequiredService<IWorkloadInventory>().RefreshAsync(TestContext.Current.CancellationToken);
        await _factory.Services.GetRequiredService<IMaintenanceMonitor>().RefreshAsync(TestContext.Current.CancellationToken);
        using var client = await _factory.CreateSignedInClientAsync();
        Report = await client.GetFromJsonAsync<JsonElement>("/api/v1/maintenance", TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Scanner.Dispose();
    }

    /// <summary>Sends every request to the fake server, keeping the path the connector built from the catalog's address.</summary>
    private sealed class RedirectingHandler(Uri target) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new UriBuilder(request.RequestUri!) { Scheme = target.Scheme, Host = target.Host, Port = target.Port }.Uri;
            return base.SendAsync(request, cancellationToken);
        }
    }
}

/// <summary>
/// The vulnerabilities of the running images over the real HTTP pipeline (D-131): the catalog's <c>imageScanner:</c> and its secret variable, the
/// monitor telling the companion which images run, the report served with one state per image. The Core rules are unit-tested
/// (ImageVulnerabilityReportFixture, MaintenanceScannerFixture); this proves the wiring.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ImageVulnerabilitiesFixture : IClassFixture<ImageVulnerabilitiesScan>
{
    private readonly ImageVulnerabilitiesScan _scan;

    public ImageVulnerabilitiesFixture(ImageVulnerabilitiesScan scan)
    {
        _scan = scan;
    }

    [Fact]
    public void should_serve_the_vulnerabilities_of_each_running_image_with_their_state()
    {
        var images = _scan.Report.GetProperty("images").EnumerateArray().ToDictionary(i => i.GetProperty("reference").GetString()!);

        var idp = images["idp:1"].GetProperty("vulnerabilities");
        idp.GetProperty("state").GetString().Should().Be("scanned");
        idp.GetProperty("counts").GetProperty("high").GetInt32().Should().Be(2);
        idp.GetProperty("counts").GetProperty("total").GetInt32().Should().Be(6);
        idp.GetProperty("fixable").GetProperty("critical").GetInt32().Should().Be(1);
        idp.GetProperty("findings").EnumerateArray().Single().GetProperty("id").GetString().Should().Be("CVE-2026-0001");
        var local = images["local:1"].GetProperty("vulnerabilities");
        local.GetProperty("state").GetString().Should().Be("not-scanned");
        local.GetProperty("reason").GetString().Should().Be("not-found");
    }

    [Fact]
    public void should_raise_what_needs_attention_and_report_the_scanner()
    {
        var report = _scan.Report;

        report.GetProperty("attention").EnumerateArray().Select(a => a.GetProperty("kind").GetString()).Should().Contain("image-critical-vulnerabilities");
        report.GetProperty("summary").GetProperty("imagesScanned").GetInt32().Should().Be(1);
        var scanner = report.GetProperty("imageScanner");
        scanner.GetProperty("configured").GetBoolean().Should().BeTrue();
        scanner.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        scanner.GetProperty("databaseUpdatedAt").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 11, 25, 3, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void should_send_the_running_images_with_the_secret_and_never_call_the_scanner_any_other_way()
    {
        _scan.Scanner.Calls.Should().Contain("POST /v1/scans").And.Contain("GET /v1/results").And.OnlyContain(c => c == "POST /v1/scans" || c == "GET /v1/results");
        _scan.Scanner.Authorizations.Should().OnlyContain(a => a == "Bearer " + ImageVulnerabilitiesScan.Secret);
    }
}
