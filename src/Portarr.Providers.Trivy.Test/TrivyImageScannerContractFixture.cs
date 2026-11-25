using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;
using Portarr.Test.Common;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Trivy.Test;

/// <summary>The Trivy scanner connector against the image scanner capability's contract.</summary>
public sealed class TrivyImageScannerContractFixture : ImageScannerContract
{
    private const string Secret = "never-shown-secret-never-shown-secret";

    protected override IReadOnlySet<string> AskPaths { get; } = new HashSet<string> { "/v1/scans" };

    protected override ImageScannerHarness Arrange()
    {
        var companion = new FakeCompanion();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(TrivyImageScanner.HttpClientName).Returns(_ => new HttpClient(companion));
        var catalogService = Substitute.For<ICatalogService>();
        var parsed = TestCatalog.Parse("imageScanner: { url: \"http://scanner.homelab.example:8489\", allowInsecure: true }\n");
        catalogService.Current.Returns(new CatalogState(parsed.Catalog, "catalog.yaml", true, [], null));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [ImageScannerSource.DefaultSecretVariable] = Secret }).Build();

        return new ImageScannerHarness(
            new TrivyImageScanner(factory, catalogService, configuration, new FakeTimeProvider(Content().GeneratedAt)),
            () => companion.Failing = true,
            () => companion.Requests.ToList(),
            () => companion.Asked.ToList(),
            [Secret]);
    }

    private sealed class FakeCompanion : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public List<string> Asked { get; } = [];

        public bool Failing { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath));
            if (Failing)
            {
                throw new HttpRequestException("connection refused");
            }

            if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: Secret })
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/v1/scans")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Asked.AddRange(body.RootElement.GetProperty("targets").EnumerateArray().Where(e => e.GetProperty("kind").GetString() == "image").Select(e => e.GetProperty("ref").GetString()!));
                return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("""{ "queued": 2 }""", Encoding.UTF8, "application/json") };
            }

            return request.RequestUri.AbsolutePath == "/v1/results"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Render(Content()), Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        /// <summary>The held content in the companion's own wire format (scanner/src/scanner.ts).</summary>
        private static string Render(ScannerReport report)
        {
            var images = report.Images.Select(image => new Dictionary<string, object?>
            {
                ["kind"] = image.Kind,
                ["ref"] = image.Image,
                ["state"] = image.State.ToString().ToLowerInvariant(),
                ["reason"] = image.Reason,
                ["scannedAt"] = image.ScannedAt,
                ["counts"] = Counts(image.Counts),
                ["fixable"] = Counts(image.Fixable),
                ["findings"] = image.Findings.Select(f => new Dictionary<string, object?>
                {
                    ["id"] = f.Id,
                    ["package"] = f.Package,
                    ["installedVersion"] = f.InstalledVersion,
                    ["fixedVersion"] = f.FixedVersion,
                    ["severity"] = f.Severity,
                    ["title"] = f.Title,
                    ["url"] = f.Url?.AbsoluteUri,
                }),
            });
            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["generatedAt"] = report.GeneratedAt,
                ["database"] = new Dictionary<string, object?> { ["version"] = "2", ["updatedAt"] = report.DatabaseUpdatedAt },
                ["images"] = images,
            });
        }

        private static Dictionary<string, int> Counts(VulnerabilityCounts counts) => new()
        {
            ["critical"] = counts.Critical,
            ["high"] = counts.High,
            ["medium"] = counts.Medium,
            ["low"] = counts.Low,
            ["unknown"] = counts.Unknown,
        };
    }
}
