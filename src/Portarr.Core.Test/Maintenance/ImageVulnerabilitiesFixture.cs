using Portarr.Core.Maintenance;

namespace Portarr.Core.Test.Maintenance;

/// <summary>The safety rules every scanner connector gets from the Core (D-131): a closed vocabulary, https links only, clean and short texts, closed reasons.</summary>
public sealed class ImageVulnerabilitiesFixture
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("CRITICAL", "critical")]
    [InlineData(" High ", "high")]
    [InlineData("moderate", "medium")]
    [InlineData("MEDIUM", "medium")]
    [InlineData("low", "low")]
    [InlineData("unknown", null)]
    [InlineData("negligible", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void should_put_a_severity_in_the_closed_vocabulary(string? sent, string? expected)
    {
        Finding(severity: sent).Normalised().Severity.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://avd.example/cve-1", "https://avd.example/cve-1")]
    [InlineData("http://avd.example/cve-1", null)]
    [InlineData("https://user:secret@avd.example/", null)]
    [InlineData("ftp://avd.example/cve-1", null)]
    public void should_keep_a_link_only_when_it_is_https_without_credentials(string url, string? expected)
    {
        var kept = Finding(url: new Uri(url)).Normalised().Url;

        (kept?.AbsoluteUri).Should().Be(expected);
    }

    [Fact]
    public void should_drop_a_relative_link()
    {
        Finding(url: new Uri("/relative", UriKind.Relative)).Normalised().Url.Should().BeNull();
    }

    [Fact]
    public void should_clean_the_texts_a_third_party_wrote()
    {
        var finding = new ImageFinding("CVE-1\n", "open\u0000ssl", "3.0\u202E.1", "  3.0.2  ", "high", "Ignore​ this\r\nand \u202Edo that   now", null).Normalised();

        finding.Id.Should().Be("CVE-1");
        finding.Package.Should().Be("open ssl");
        finding.InstalledVersion.Should().Be("3.0.1");
        finding.FixedVersion.Should().Be("3.0.2");
        finding.Title.Should().Be("Ignore this and do that now");
    }

    [Fact]
    public void should_cut_a_long_text_by_characters_not_in_the_middle_of_one()
    {
        var finding = Finding(title: new string('x', 195) + "😀😀😀😀😀😀😀😀").Normalised();

        finding.Title!.EnumerateRunes().Count().Should().Be(ImageFinding.MaxTextLength);
        finding.Title.Should().EndWith("😀😀😀😀😀");
    }

    [Fact]
    public void should_say_nothing_for_a_text_that_is_nothing_once_cleaned()
    {
        ImageScanText.Clean("  ​ \n ", 10).Should().BeNull();
        ImageScanText.Clean(null, 10).Should().BeNull();
        Finding(title: "\u202E").Normalised().Title.Should().BeNull();
    }

    [Fact]
    public void should_keep_a_scanned_result_with_its_worst_findings_first_the_fixable_before_the_others_and_at_most_fifty()
    {
        var findings = Enumerable.Range(0, 70).Select(i => Finding($"CVE-{i:D3}", severity: "high", fixedVersion: i % 2 == 0 ? null : "2.0"))
            .Append(Finding("CVE-CRIT", severity: "critical", fixedVersion: null))
            .Append(Finding("CVE-LOW", severity: "low"))
            .ToList();

        var result = Scanned(findings).Normalised();

        result.Findings.Should().HaveCount(ImageScanResult.MaxFindings);
        result.Findings[0].Id.Should().Be("CVE-CRIT");
        result.Findings.Skip(1).Take(35).Should().OnlyContain(f => f.FixedVersion != null, "the ones that have a fix come before the others of the same severity");
        result.Findings.Select(f => f.Id).Should().NotContain("CVE-LOW");
    }

    [Fact]
    public void should_drop_a_finding_without_an_id_or_a_package()
    {
        var result = Scanned([Finding(" "), Finding("CVE-1", package: "​"), Finding("CVE-2")]).Normalised();

        result.Findings.Select(f => f.Id).Should().Equal("CVE-2");
    }

    [Fact]
    public void should_take_a_negative_count_as_zero()
    {
        var result = (Scanned([]) with { Counts = new VulnerabilityCounts(-1, 2, -3, 4, -5), Fixable = new VulnerabilityCounts(0, -2, 0, 0, 0) }).Normalised();

        result.Counts.Should().Be(new VulnerabilityCounts(0, 2, 0, 4, 0));
        result.Fixable.Should().Be(VulnerabilityCounts.None);
        result.Counts.Total.Should().Be(6);
        result.Counts.Severe.Should().Be(2);
    }

    [Theory]
    [InlineData(ImageScanState.Queued)]
    [InlineData(ImageScanState.Scanning)]
    [InlineData(ImageScanState.Failed)]
    public void should_keep_no_counts_findings_or_date_for_an_image_that_is_not_scanned(ImageScanState state)
    {
        var result = (Scanned([Finding()]) with { State = state, Reason = "timeout" }).Normalised();

        result.Counts.Should().Be(VulnerabilityCounts.None);
        result.Findings.Should().BeEmpty();
        result.ScannedAt.Should().BeNull();
        result.Reason.Should().Be(state == ImageScanState.Failed ? "timeout" : null, "only a failure has a reason");
    }

    [Theory]
    [InlineData("not-found", "not-found")]
    [InlineData("Unauthorized", "unauthorized")]
    [InlineData("TIMEOUT", "timeout")]
    [InlineData("unsupported", "unsupported")]
    [InlineData("error", "error")]
    [InlineData("<script>", "error")]
    [InlineData(null, "error")]
    public void should_close_the_reasons_of_a_failure(string? sent, string expected)
    {
        ImageScanReasons.Normalise(sent).Should().Be(expected);
        (Scanned([]) with { State = ImageScanState.Failed, Reason = sent }).Normalised().Reason.Should().Be(expected);
    }

    [Fact]
    public void should_normalise_a_report_once_per_reference_and_drop_an_image_without_a_name()
    {
        var report = new ScannerReport(Now, null, [Scanned([]), Scanned([]) with { Counts = new VulnerabilityCounts(9, 9, 9, 9, 9) }, Scanned([]) with { Image = " " }]).Normalised();

        report.Images.Should().ContainSingle().Which.Counts.Should().Be(new VulnerabilityCounts(1, 2, 3, 4, 5), "the first answer for a reference counts");
    }

    [Fact]
    public void should_tell_two_targets_of_different_kinds_with_the_same_reference_apart()
    {
        var report = new ScannerReport(Now, null, [Scanned([]), Scanned([]) with { Kind = "sbom" }]).Normalised();

        report.Images.Select(i => i.Kind).Should().Equal("image", "sbom");
    }

    [Theory]
    [InlineData("critical", 0)]
    [InlineData("HIGH", 1)]
    [InlineData("medium", 2)]
    [InlineData("low", 3)]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("nonsense", 1)]
    public void should_read_the_threshold_as_a_rank_high_when_it_says_nothing_known(string? setting, int rank)
    {
        VulnerabilitySeverities.ThresholdRank(setting).Should().Be(rank);
    }

    [Fact]
    public void should_build_the_reference_a_scanner_is_asked_to_read()
    {
        ImageReference.Parse("postgres:16")!.ScanReference.Should().Be("docker.io/library/postgres:16");
        ImageReference.Parse("ghcr.io/example/app@sha256:" + new string('a', 64))!.ScanReference.Should().Be("ghcr.io/example/app@sha256:" + new string('a', 64));
        ImageReference.Parse("lscr.io/linuxserver/sonarr:4.0.1@sha256:" + new string('b', 64))!.ScanReference.Should().Be("lscr.io/linuxserver/sonarr:4.0.1@sha256:" + new string('b', 64));
    }

    [Fact]
    public void should_carry_the_reason_of_a_scanner_that_cannot_be_called()
    {
        new ImageScannerUnavailableException(ImageScannerUnavailableException.SecretMissing).Reason.Should().Be("secret-missing");
        new ImageScannerUnavailableException().Reason.Should().Be("unavailable");
        new ImageScannerUnavailableException("unreachable", new IOException()).Reason.Should().Be("unreachable");
        new ImageScannerUnavailableException("http-502", new IOException()).Reason.Should().Be("http-502");
    }

    [Theory]
    [InlineData("connection refused by 10.0.0.1")]
    [InlineData("http-5xx")]
    [InlineData("")]
    public void should_make_an_error_of_a_free_text_reason(string text)
    {
        new ImageScannerUnavailableException(text, new IOException()).Reason.Should().Be("error");
    }

    private static ImageFinding Finding(string id = "CVE-2026-0001", string package = "openssl", string? severity = "high", string? fixedVersion = "3.0.2", string? title = "A flaw", Uri? url = null) =>
        new(id, package, "3.0.1", fixedVersion, severity, title, url);

    private static ImageScanResult Scanned(IReadOnlyList<ImageFinding> findings) =>
        new("docker.io/library/app:1", ImageScanState.Scanned, null, Now, new VulnerabilityCounts(1, 2, 3, 4, 5), new VulnerabilityCounts(1, 1, 0, 0, 0), findings);
}
