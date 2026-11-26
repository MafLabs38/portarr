using System.Text.Json;
using AwesomeAssertions;
using Portarr.Core.Maintenance;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>
/// An image scanner connector wired to a fake companion that holds <see cref="ImageScannerContract.Content"/>: the connector under test, how
/// to make the companion fail, what the connector called, the targets it asked to scan, and the secrets that must never come out.
/// </summary>
public sealed record ImageScannerHarness(
    IImageScanner Scanner,
    Action Fail,
    Func<IReadOnlyList<RecordedRequest>> Requests,
    Func<IReadOnlyList<ScanTarget>> Asked,
    IReadOnlyList<string> Secrets);

/// <summary>
/// The contract of the image scanner capability (<see cref="IImageScanner"/>, D-131), run against every connector that declares it: the same
/// scenarios whatever the scanner. The fake companion holds raw, untidy content (a severity in another vocabulary, a link in clear, a
/// control character in a title, sixty findings, a reason nobody defined); the connector must hand back what the Core's safety rules make of it
/// (<see cref="ScannerReport.Normalised"/>), fail loudly when the companion cannot be read, and only ask and read. A connector joins by
/// inheriting this class (CLAUDE.md rule 8).
/// </summary>
public abstract class ImageScannerContract
{
    public const string Scanned = "registry.example/team/app:1.0";
    public const string Waiting = "registry.example/team/waiting:1";
    public const string Local = "registry.example/team/local:1";
    public const string Private = "registry.example/team/private:1";
    public const string Released = "example/desktop-app";
    public const string Unpublished = "example/library";

    /// <summary>A low finding: never among the findings described, only in the full list of ids.</summary>
    public const string LowOnly = "CVE-2026-9999";

    public static readonly DateTimeOffset DatabaseAt = new(2026, 11, 25, 3, 0, 0, TimeSpan.Zero);

    /// <summary>The POST calls the connector may make to tell the companion which images to scan.</summary>
    protected abstract IReadOnlySet<string> AskPaths { get; }

    /// <summary>Wires the connector to a fake companion that holds <see cref="Content"/>.</summary>
    protected abstract ImageScannerHarness Arrange();

    /// <summary>What the companion holds, raw: a connector's fake renders it in the companion's own wire format.</summary>
    public static ScannerReport Content()
    {
        var findings = new List<ImageFinding>
        {
            new("CVE-2026-0001", "openssl", "3.0.1", "3.0.2", "CRITICAL", "A flaw\u202E in the library\nwith a break", new Uri("https://avd.example/cve-2026-0001")),
            new("CVE-2026-0002", "zlib", "1.2.0", null, "Moderate", "Insecure link", new Uri("http://insecure.example/cve")),
        };
        findings.AddRange(Enumerable.Range(0, 60).Select(i => new ImageFinding($"CVE-2026-1{i:D3}", $"pkg{i}", "1.0", "1.1", "high", $"Finding {i}", null)));
        // The ids of every finding (D-133): the sixty-two above, a low one never described, the same id twice, and two that are not plain ids.
        List<string> ids = [.. findings.Select(f => f.Id), LowOnly, "CVE-2026-0001", "CVE-2026-9998 <b>", "-leading-dash"];

        return new ScannerReport(
            new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero),
            DatabaseAt,
            [
                new ImageScanResult(Scanned, ImageScanState.Scanned, null, new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero), new VulnerabilityCounts(1, 61, 3, 4, 0), new VulnerabilityCounts(1, 60, 0, 0, 0), findings, VulnerabilityIds: ids),
                new ImageScanResult(Waiting, ImageScanState.Queued, null, null, new VulnerabilityCounts(9, 9, 9, 9, 9), VulnerabilityCounts.None, [], VulnerabilityIds: ["CVE-2026-0001"], VulnerabilityIdsTruncated: true),
                new ImageScanResult(Local, ImageScanState.Failed, ImageScanReasons.NotFound, null, VulnerabilityCounts.None, VulnerabilityCounts.None, []),
                new ImageScanResult(Private, ImageScanState.Failed, "something-nobody-defined", null, VulnerabilityCounts.None, VulnerabilityCounts.None, []),
                new ImageScanResult(Released, ImageScanState.Scanned, null, new DateTimeOffset(2026, 11, 25, 9, 30, 0, TimeSpan.Zero), new VulnerabilityCounts(0, 2, 0, 0, 0), new VulnerabilityCounts(0, 1, 0, 0, 0), [new ImageFinding("CVE-2026-2001", "Example.Package", "1.0.0", "1.0.1", "HIGH", "A flaw in a package", new Uri("https://avd.example/cve-2026-2001"))], ScanTargetKinds.Release, "v1.4.0", ["CVE-2026-2001", "CVE-2026-2002"], true),
                new ImageScanResult(Unpublished, ImageScanState.Failed, ImageScanReasons.NoSbom, null, VulnerabilityCounts.None, VulnerabilityCounts.None, [], ScanTargetKinds.Release, "v0.9.0"),
            ]);
    }

    [Fact]
    public async Task should_report_every_image_the_scanner_holds_and_how_fresh_its_database_is()
    {
        var harness = Arrange();

        var report = await harness.Scanner.ReadAsync(TestContext.Current.CancellationToken);

        report.Images.Select(i => i.Image).Should().BeEquivalentTo(Scanned, Waiting, Local, Private, Released, Unpublished);
        report.Images.Select(i => i.Image).Should().OnlyHaveUniqueItems();
        report.DatabaseUpdatedAt.Should().Be(DatabaseAt);
        report.Images.Single(i => i.Image == Scanned).Should().Match<ImageScanResult>(i => i.State == ImageScanState.Scanned && i.Counts.Critical == 1 && i.Counts.High == 61 && i.Fixable.High == 60);
        report.Images.Single(i => i.Image == Waiting).State.Should().Be(ImageScanState.Queued);
    }

    [Fact]
    public async Task should_hand_back_findings_in_the_closed_vocabulary_with_safe_links_and_clean_text_the_worst_first()
    {
        var harness = Arrange();

        var findings = (await harness.Scanner.ReadAsync(TestContext.Current.CancellationToken)).Images.Single(i => i.Image == Scanned).Findings;

        findings.Should().HaveCountLessThanOrEqualTo(ImageScanResult.MaxFindings);
        findings.Should().AllSatisfy(f =>
        {
            f.Severity.Should().BeOneOf(null, "critical", "high", "medium", "low");
            (f.Url is null || f.Url.Scheme == Uri.UriSchemeHttps).Should().BeTrue("a link in clear is dropped");
            (f.Title ?? string.Empty).Should().NotContainAny("\u202E", "\n");
        });
        findings[0].Id.Should().Be("CVE-2026-0001");
        findings[0].Severity.Should().Be("critical");
        findings[0].Title.Should().Be("A flaw in the library with a break");
        findings.Select(f => VulnerabilitySeverities.Rank(f.Severity)).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task should_give_no_counts_and_no_findings_to_an_image_that_is_not_scanned_and_a_reason_from_the_closed_set_to_a_failure()
    {
        var harness = Arrange();

        var images = (await harness.Scanner.ReadAsync(TestContext.Current.CancellationToken)).Images;

        var waiting = images.Single(i => i.Image == Waiting);
        waiting.Counts.Should().Be(VulnerabilityCounts.None);
        waiting.Findings.Should().BeEmpty();
        waiting.Reason.Should().BeNull();
        images.Single(i => i.Image == Local).Reason.Should().Be(ImageScanReasons.NotFound);
        images.Single(i => i.Image == Private).Reason.Should().Be(ImageScanReasons.Error, "a reason nobody defined is an error, never shown as given");
    }

    [Fact]
    public async Task should_fail_instead_of_answering_when_the_scanner_cannot_be_read()
    {
        var harness = Arrange();
        harness.Fail();

        var read = () => harness.Scanner.ReadAsync(TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<Exception>("an empty report would read as 'no vulnerability' and hide that the scanner is down");
    }

    [Fact]
    public async Task should_fail_instead_of_staying_silent_when_the_scanner_cannot_be_asked()
    {
        var harness = Arrange();
        harness.Fail();

        var ask = () => harness.Scanner.RequestAsync([Image(Scanned)], TestContext.Current.CancellationToken);

        await ask.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_tell_the_scanner_which_images_to_scan()
    {
        var harness = Arrange();

        await harness.Scanner.RequestAsync([Image(Scanned), Image(Waiting), Image(Scanned), Image("-x; rm -rf /")], TestContext.Current.CancellationToken);

        harness.Asked().Should().BeEquivalentTo(
            [new ScanTarget(ScanTargetKinds.Image, Scanned), new ScanTarget(ScanTargetKinds.Image, Waiting)],
            "each reference once, and nothing the scanner would refuse");
    }

    [Fact]
    public async Task should_tell_the_scanner_which_releases_to_scan_beside_the_images()
    {
        var harness = Arrange();

        await harness.Scanner.RequestAsync(
            [new(ScanTargetKinds.Image, Scanned), new(ScanTargetKinds.Release, Released), new(ScanTargetKinds.Release, Released), new(ScanTargetKinds.Release, "not a repository"), new(ScanTargetKinds.Release, "owner/name/extra"), new("sbom", "x/y")],
            TestContext.Current.CancellationToken);

        harness.Asked().Should().BeEquivalentTo(
            [new ScanTarget(ScanTargetKinds.Image, Scanned), new ScanTarget(ScanTargetKinds.Release, Released)],
            "a release is named by owner/name, once, and what the scanner would refuse is left out");
    }

    [Fact]
    public async Task should_report_the_version_of_a_scanned_release_and_a_reason_when_it_publishes_no_sbom()
    {
        var harness = Arrange();

        var images = (await harness.Scanner.ReadAsync(TestContext.Current.CancellationToken)).Images;

        images.Single(i => i.Image == Released).Should().Match<ImageScanResult>(i => i.Kind == ScanTargetKinds.Release && i.State == ImageScanState.Scanned && i.Version == "v1.4.0" && i.Fixable.High == 1);
        images.Single(i => i.Image == Scanned).Version.Should().BeNull("only a release has a version");
        images.Single(i => i.Image == Unpublished).Should().Match<ImageScanResult>(i => i.Reason == ImageScanReasons.NoSbom && i.Version == null);
    }

    [Fact]
    public async Task should_hand_back_the_ids_of_every_finding_plain_distinct_and_sorted_only_for_a_scanned_target()
    {
        var harness = Arrange();

        var images = (await harness.Scanner.ReadAsync(TestContext.Current.CancellationToken)).Images;

        var scanned = images.Single(i => i.Image == Scanned);
        scanned.VulnerabilityIds.Should().NotBeNull();
        scanned.VulnerabilityIds.Should().Contain([LowOnly, "CVE-2026-1059"], "an id is kept whether or not its finding is described");
        scanned.VulnerabilityIds.Should().HaveCount(63).And.OnlyHaveUniqueItems().And.BeInAscendingOrder(StringComparer.Ordinal);
        scanned.VulnerabilityIds.Should().AllSatisfy(id => PlainVulnerabilityIds.IsPlain(id).Should().BeTrue("text is never passed on as an id"));
        scanned.VulnerabilityIdsTruncated.Should().BeFalse();
        images.Single(i => i.Image == Released).Should().Match<ImageScanResult>(i => i.VulnerabilityIdsTruncated && i.VulnerabilityIds!.Count == 2, "a scanner with more ids than it sent says so");
        images.Single(i => i.Image == Waiting).Should().Match<ImageScanResult>(i => i.VulnerabilityIds == null && !i.VulnerabilityIdsTruncated, "a target not scanned has no ids");
        images.Single(i => i.Image == Local).VulnerabilityIds.Should().BeNull();
    }

    private static ScanTarget Image(string reference) => new(ScanTargetKinds.Image, reference);

    [Fact]
    public async Task should_only_ask_and_read()
    {
        var harness = Arrange();

        await harness.Scanner.RequestAsync([Image(Scanned)], TestContext.Current.CancellationToken);
        await harness.Scanner.ReadAsync(TestContext.Current.CancellationToken);

        var requests = harness.Requests();
        requests.Should().NotBeEmpty();
        requests.Where(r => r.Method != HttpMethod.Get).Should().AllSatisfy(r =>
        {
            r.Method.Should().Be(HttpMethod.Post, "no PUT, PATCH or DELETE: Portarr never changes a scanner");
            AskPaths.Should().Contain(r.Path, "a POST only tells the scanner which images to scan");
        });
    }

    [Fact]
    public async Task should_never_return_the_secret_the_scanner_is_called_with()
    {
        var harness = Arrange();

        var report = await harness.Scanner.ReadAsync(TestContext.Current.CancellationToken);

        var text = JsonSerializer.Serialize(report);
        foreach (var secret in harness.Secrets)
        {
            text.Should().NotContain(secret);
        }
    }
}
