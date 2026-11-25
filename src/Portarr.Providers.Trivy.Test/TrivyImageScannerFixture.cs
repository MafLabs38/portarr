using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;
using Portarr.Test.Common;

namespace Portarr.Providers.Trivy.Test;

/// <summary>The scanner companion as Portarr calls it (D-131): the address and secret the catalog names, batches, refusals and what an odd answer becomes.</summary>
public sealed class TrivyImageScannerFixture
{
    private const string Secret = "example-secret-example-secret-123";

    private readonly List<(HttpMethod Method, Uri Uri, string? Authorization, string Body)> _calls = [];
    private Func<HttpRequestMessage, HttpResponseMessage> _answer = _ => new HttpResponseMessage(HttpStatusCode.Accepted);
    private string _catalog = "imageScanner: { url: \"http://scanner.homelab.example:8489\", allowInsecure: true }\n";
    private string? _secret = Secret;

    [Fact]
    public async Task should_send_the_secret_as_a_bearer_token_to_the_address_the_catalog_names_under_its_path_prefix()
    {
        _catalog = "imageScanner: { url: \"https://scanner.homelab.example/trivy\", secretEnv: PORTARR_SCANNER_STAGING }\n";
        var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PORTARR_SCANNER_STAGING"] = "another-secret-another-secret-12" }).Build();

        await Subject(configured).RequestAsync([Image("app:1")], TestContext.Current.CancellationToken);

        _calls.Should().ContainSingle().Which.Should().Match<(HttpMethod Method, Uri Uri, string? Authorization, string Body)>(
            c => c.Method == HttpMethod.Post && c.Uri.AbsoluteUri == "https://scanner.homelab.example/trivy/v1/scans" && c.Authorization == "Bearer another-secret-another-secret-12");
    }

    [Fact]
    public async Task should_send_the_references_in_batches_of_200_each_once_and_leave_out_what_the_scanner_would_refuse()
    {
        var images = Enumerable.Range(0, 450).Select(i => $"registry.example/team/app{i}:1").Append("registry.example/team/app0:1").Append($"registry.example/{new string('a', 300)}:1").Append("-option").Append("with space").Append(string.Empty).Select(Image).ToList();

        await Subject().RequestAsync(images, TestContext.Current.CancellationToken);

        _calls.Select(c => JsonDocument.Parse(c.Body).RootElement.GetProperty("targets").GetArrayLength()).Should().Equal(200, 200, 50);
    }

    [Fact]
    public async Task should_send_images_and_releases_in_one_request_with_the_kind_of_each()
    {
        await Subject().RequestAsync([Image("app:1"), new(ScanTargetKinds.Release, "example/desktop-app"), new(ScanTargetKinds.Release, "Example_Org/app.v2-x")], TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(_calls.Should().ContainSingle().Which.Body).RootElement.GetProperty("targets");
        body.EnumerateArray().Select(t => (t.GetProperty("kind").GetString(), t.GetProperty("ref").GetString())).Should().Equal(
            ("image", "app:1"), ("release", "example/desktop-app"), ("release", "Example_Org/app.v2-x"));
    }

    [Theory]
    [InlineData("no-slash")]
    [InlineData("owner/name/extra")]
    [InlineData("/name")]
    [InlineData("owner/")]
    [InlineData("owner/na me")]
    [InlineData("owner/na;me")]
    [InlineData("")]
    public async Task should_leave_out_a_release_that_is_not_a_repository_and_any_kind_it_does_not_know(string reference)
    {
        await Subject().RequestAsync([new(ScanTargetKinds.Release, reference), new("sbom", "example/desktop-app")], TestContext.Current.CancellationToken);

        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task should_leave_out_a_release_whose_parts_are_too_long()
    {
        await Subject().RequestAsync([new(ScanTargetKinds.Release, $"{new string('a', 101)}/name"), new(ScanTargetKinds.Release, $"owner/{new string('b', 101)}"), new(ScanTargetKinds.Release, $"{new string('a', 100)}/{new string('b', 100)}")], TestContext.Current.CancellationToken);

        JsonDocument.Parse(_calls.Should().ContainSingle().Which.Body).RootElement.GetProperty("targets").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task should_read_the_version_of_a_release_and_the_no_sbom_reason_and_take_a_result_without_a_kind_for_an_image()
    {
        _answer = _ => Json("""
            { "images": [
              { "ref": "app:1", "state": "scanned", "version": "v9" },
              { "kind": "release", "ref": "example/desktop-app", "state": "scanned", "version": "  v1.4.0\n", "scannedAt": "2026-11-25T09:00:00Z" },
              { "kind": "release", "ref": "example/library", "state": "failed", "reason": "no-sbom", "version": "v0.1" },
              { "kind": "release", "ref": "example/queued", "state": "queued", "version": "v0.2" },
              { "kind": "release", "ref": "example/long", "state": "scanned", "version": "__LONG__" }
            ] }
            """.Replace("__LONG__", new string('v', 300), StringComparison.Ordinal));

        var report = await Subject().ReadAsync(TestContext.Current.CancellationToken);

        report.Images.Select(i => (i.Kind, i.Image, i.Version)).Should().Equal(
            ("image", "app:1", null),
            ("release", "example/desktop-app", "v1.4.0"),
            ("release", "example/library", null),
            ("release", "example/queued", null),
            ("release", "example/long", new string('v', ImageScanResult.MaxVersionLength)));
        report.Images[2].Reason.Should().Be(ImageScanReasons.NoSbom);
    }

    [Fact]
    public async Task should_send_nothing_when_no_reference_is_worth_sending()
    {
        await Subject().RequestAsync([Image("-option"), Image("with space")], TestContext.Current.CancellationToken);

        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_call_the_scanner_without_its_secret_and_say_why()
    {
        _secret = null;
        var subject = Subject();

        var ask = () => subject.RequestAsync([Image("app:1")], TestContext.Current.CancellationToken);
        var read = () => subject.ReadAsync(TestContext.Current.CancellationToken);

        (await ask.Should().ThrowAsync<ImageScannerUnavailableException>()).Which.Reason.Should().Be(ImageScannerUnavailableException.SecretMissing);
        (await read.Should().ThrowAsync<ImageScannerUnavailableException>()).Which.Reason.Should().Be(ImageScannerUnavailableException.SecretMissing);
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_to_work_when_the_catalog_declares_no_scanner()
    {
        _catalog = "environments: []\n";

        var read = () => Subject().ReadAsync(TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task should_turn_a_refusal_into_a_failure_that_carries_the_status(HttpStatusCode status)
    {
        _answer = _ => new HttpResponseMessage(status);

        var ask = () => Subject().RequestAsync([Image("app:1")], TestContext.Current.CancellationToken);
        var read = () => Subject().ReadAsync(TestContext.Current.CancellationToken);

        (await ask.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(status);
        (await read.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task should_fail_on_an_answer_that_is_not_the_scanners()
    {
        _answer = _ => Json("null");
        var empty = () => Subject().ReadAsync(TestContext.Current.CancellationToken);
        await empty.Should().ThrowAsync<InvalidDataException>();

        _answer = _ => Json("<html>proxy error</html>");
        var html = () => Subject().ReadAsync(TestContext.Current.CancellationToken);
        await html.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task should_read_a_sparse_answer_without_inventing_vulnerabilities()
    {
        _answer = _ => Json("""
            { "images": [
              { "kind": "image", "ref": "app:1", "state": "scanning" },
              { "ref": "app:2", "state": "no-such-state", "reason": "timeout" },
              { "ref": "app:3", "state": "scanned", "counts": { "critical": -4, "high": 2 }, "findings": [ { "id": "CVE-1" }, { "id": "CVE-2", "package": "zlib", "severity": "HIGH", "url": "not a url" } ] },
              { "state": "scanned" },
              { "kind": "sbom", "ref": "release.json", "state": "scanned" }
            ] }
            """);

        var report = await Subject().ReadAsync(TestContext.Current.CancellationToken);

        report.GeneratedAt.Should().Be(new FakeTimeProvider(new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero)).GetUtcNow(), "no date from the scanner: the time of the read");
        report.DatabaseUpdatedAt.Should().BeNull();
        report.Images.Where(i => i.Kind == "image").Select(i => (i.Image, i.State)).Should().Equal(("app:1", ImageScanState.Scanning), ("app:2", ImageScanState.Failed), ("app:3", ImageScanState.Scanned));
        report.Images.Select(i => i.Kind).Should().Equal("image", "image", "image", "sbom");
        report.Images[1].Reason.Should().Be(ImageScanReasons.Timeout);
        report.Images[2].Counts.Should().Be(new VulnerabilityCounts(0, 2, 0, 0, 0), "a negative count is zero");
        report.Images[2].Findings.Should().ContainSingle().Which.Should().Match<ImageFinding>(f => f.Id == "CVE-2" && f.Severity == "high" && f.Url == null);
    }

    [Fact]
    public async Task should_register_itself_as_the_image_scanner_with_its_own_http_client()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICatalogService>());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddTrivyProvider();
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IImageScanner>().Should().BeOfType<TrivyImageScanner>();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TrivyImageScanner.HttpClientName);
        client.Timeout.Should().Be(TimeSpan.FromSeconds(20));
    }

    private static ScanTarget Image(string reference) => new(ScanTargetKinds.Image, reference);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private TrivyImageScanner Subject(IConfiguration? configuration = null)
    {
        var handler = new Handler(request =>
        {
            _calls.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty));
            return _answer(request);
        });
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(TrivyImageScanner.HttpClientName).Returns(_ => new HttpClient(handler));
        var catalogService = Substitute.For<ICatalogService>();
        catalogService.Current.Returns(new CatalogState(TestCatalog.Parse(_catalog).Catalog, "catalog.yaml", true, [], null));
        var values = new Dictionary<string, string?> { [ImageScannerSource.DefaultSecretVariable] = _secret };
        return new TrivyImageScanner(factory, catalogService, configuration ?? new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new FakeTimeProvider(new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero)));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(answer(request));
    }
}
