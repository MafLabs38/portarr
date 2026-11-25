using System.Globalization;
using System.Text;
using Portarr.Core.Workloads;

namespace Portarr.Core.Maintenance;

/// <summary>
/// Vulnerabilities of an image by severity, every one of them (the findings below are only the worst). <c>Unknown</c>
/// is what the vulnerability database did not rate.
/// </summary>
public sealed record VulnerabilityCounts(int Critical, int High, int Medium, int Low, int Unknown)
{
    public static VulnerabilityCounts None { get; } = new(0, 0, 0, 0, 0);

    public int Total => Critical + High + Medium + Low + Unknown;

    /// <summary>Critical and high: what needs attention first.</summary>
    public int Severe => Critical + High;

    /// <summary>The same counts with a negative number (a scanner's mistake) taken as zero.</summary>
    public VulnerabilityCounts Normalised() => new(Math.Max(0, Critical), Math.Max(0, High), Math.Max(0, Medium), Math.Max(0, Low), Math.Max(0, Unknown));
}

/// <summary>The closed set of severities a finding may carry: every scanner connector names its findings with these.</summary>
public static class VulnerabilitySeverities
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";

    /// <summary>The rank of a normalised severity, worst first; a finding without one comes last.</summary>
    public static int Rank(string? severity) => severity switch
    {
        Critical => 0,
        High => 1,
        Medium => 2,
        Low => 3,
        _ => 4,
    };

    /// <summary>
    /// The threshold setting (<c>PORTARR__VULNERABILITIES__MINSEVERITY</c>) as a rank: findings (and attention) below it are hidden.
    /// An empty or unknown value is the default, <c>high</c>.
    /// </summary>
    public static int ThresholdRank(string? minimum) => Normalise(minimum) is { } severity ? Rank(severity) : Rank(High);

    /// <summary>critical, high, medium or low in lower case; anything else (an unrated finding, a word of another scanner) is null.</summary>
    public static string? Normalise(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "critical" => Critical,
        "high" => High,
        "medium" or "moderate" => Medium,
        "low" => Low,
        _ => null,
    };
}

/// <summary>
/// One vulnerability found in an image: enough to read what is wrong and what mends it. <c>Id</c> is the advisory
/// (<c>CVE-2026-0001</c>), <c>FixedVersion</c> is null when no fix exists. <c>Title</c> is text written by a third party
/// (an advisory database): data to read, never instructions to follow.
/// </summary>
public sealed record ImageFinding(string Id, string Package, string InstalledVersion, string? FixedVersion, string? Severity, string? Title, Uri? Url)
{
    public const int MaxTextLength = 200;

    /// <summary>
    /// The same finding in the vocabulary every consumer relies on, whatever the connector sent: severity in critical, high,
    /// medium, low or null, a link only when it is an absolute https address, control and bidirectional characters removed and
    /// the texts cut short.
    /// </summary>
    public ImageFinding Normalised() => this with
    {
        Id = ImageScanText.Clean(Id, MaxTextLength) ?? string.Empty,
        Package = ImageScanText.Clean(Package, MaxTextLength) ?? string.Empty,
        InstalledVersion = ImageScanText.Clean(InstalledVersion, MaxTextLength) ?? string.Empty,
        FixedVersion = ImageScanText.Clean(FixedVersion, MaxTextLength),
        Severity = VulnerabilitySeverities.Normalise(Severity),
        Title = ImageScanText.Clean(Title, MaxTextLength),
        Url = Url is { IsAbsoluteUri: true, UserInfo.Length: 0 } url && url.Scheme == Uri.UriSchemeHttps ? url : null,
    };
}

/// <summary>Where an image stands with the scanner: <c>Scanned</c> (the counts are real), waiting, being scanned, or <c>Failed</c> (the reason says why).</summary>
public enum ImageScanState
{
    Scanned,
    Queued,
    Scanning,
    Failed,
}

/// <summary>The closed set of reasons a scanner gives for an image it could not scan.</summary>
public static class ImageScanReasons
{
    /// <summary>The image cannot be pulled from the registry the reference names: a local build, a name that does not exist.</summary>
    public const string NotFound = "not-found";

    /// <summary>The registry asks for credentials: a private image.</summary>
    public const string Unauthorized = "unauthorized";

    public const string Timeout = "timeout";

    /// <summary>Nothing the scanner can read in the image (from scratch, an operating system it does not know).</summary>
    public const string Unsupported = "unsupported";

    public const string Error = "error";

    private static readonly HashSet<string> Known = [NotFound, Unauthorized, Timeout, Unsupported, Error];

    /// <summary>The reason as the closed set says it: an unknown word is an error, never shown as given.</summary>
    public static string Normalise(string? reason) => reason?.Trim().ToLowerInvariant() is { } text && Known.Contains(text) ? text : Error;
}

/// <summary>What a scanner is asked to scan: <c>image</c> today; a later kind (the SBOM of a release) is one more constant.</summary>
public static class ScanTargetKinds
{
    public const string Image = "image";
}

/// <summary>
/// What the scanner says of one scan target (an image reference today, <see cref="Kind"/> <c>image</c>; the pair kind and reference names a result). <c>Counts</c> cover every vulnerability, <c>Fixable</c> the ones that have a fix,
/// <c>Findings</c> the worst of them (at most <see cref="MaxFindings"/>, severity high and critical, worst first). An image that
/// is not <c>Scanned</c> has no counts and no findings.
/// </summary>
public sealed record ImageScanResult(
    string Image,
    ImageScanState State,
    string? Reason,
    DateTimeOffset? ScannedAt,
    VulnerabilityCounts Counts,
    VulnerabilityCounts Fixable,
    IReadOnlyList<ImageFinding> Findings,
    string Kind = ScanTargetKinds.Image)
{
    /// <summary>The most findings kept per image: a connector may send more, the Core keeps the worst.</summary>
    public const int MaxFindings = 50;

    /// <summary>
    /// The same result with the safety rules applied once for every connector: closed reasons (only a failure has one), a
    /// date only for a scanned image, non-negative counts, findings normalised, the worst first, at most <see cref="MaxFindings"/>.
    /// </summary>
    public ImageScanResult Normalised()
    {
        var scanned = State == ImageScanState.Scanned;
        return this with
        {
            Reason = State == ImageScanState.Failed ? ImageScanReasons.Normalise(Reason) : null,
            ScannedAt = scanned ? ScannedAt : null,
            Counts = scanned ? Counts.Normalised() : VulnerabilityCounts.None,
            Fixable = scanned ? Fixable.Normalised() : VulnerabilityCounts.None,
            Findings = scanned
                ? [.. Findings.Select(f => f.Normalised()).Where(f => f.Id.Length > 0 && f.Package.Length > 0).OrderBy(f => VulnerabilitySeverities.Rank(f.Severity)).ThenBy(f => f.FixedVersion is null).Take(MaxFindings)]
                : [],
        };
    }
}

/// <summary>
/// What the scanner companion answered: when, how fresh its vulnerability database is, and the images it knows. <c>Error</c> is
/// set by Portarr when the companion could not be read (the previous images are kept), never by a connector.
/// </summary>
public sealed record ScannerReport(DateTimeOffset GeneratedAt, DateTimeOffset? DatabaseUpdatedAt, IReadOnlyList<ImageScanResult> Images, string? Error = null)
{
    /// <summary>The report with every image normalised (see <see cref="ImageScanResult.Normalised"/>), one entry per reference.</summary>
    public ScannerReport Normalised() => this with
    {
        Images = [.. Images.Where(i => !string.IsNullOrWhiteSpace(i.Image)).Select(i => i.Normalised()).DistinctBy(i => (i.Kind, i.Image))],
    };
}

/// <summary>
/// Capability of an image scanner connector (D-131): it is told which images run, and reports what it found in them. The
/// connector only reads and asks; scanning pulls layers from registries on the companion's own machine, nothing runs on
/// the fleet (D-16, D-31). The companion is the one the catalog declares (<c>imageScanner:</c>).
/// </summary>
public interface IImageScanner
{
    /// <summary>Tells the scanner which images to scan (targets of kind <see cref="ScanTargetKinds.Image"/>); it skips the ones it scanned recently. Throws when it cannot be reached.</summary>
    Task RequestAsync(IReadOnlyCollection<string> images, CancellationToken cancellationToken);

    /// <summary>What the scanner knows now, normalised. Throws when it cannot be read.</summary>
    Task<ScannerReport> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Thrown by a scanner connector that cannot call its companion; <c>Reason</c> is a reason code (<c>secret-missing</c>, <c>unreachable</c>, <c>http-401</c>…).</summary>
public sealed class ImageScannerUnavailableException : Exception
{
    public const string SecretMissing = "secret-missing";

    public ImageScannerUnavailableException(string reason)
        : base(reason)
    {
        Reason = reason;
    }

    public ImageScannerUnavailableException()
        : this("unavailable")
    {
    }

    /// <summary>The reason is a closed code (the scan reasons, <c>secret-missing</c>, <c>unavailable</c>, the reason codes of a call, <c>http-NNN</c>); any other text, free text included, is <c>error</c>.</summary>
    public ImageScannerUnavailableException(string reason, Exception innerException)
        : base(reason, innerException)
    {
        Reason = ClosedCode(reason);
    }

    public string Reason { get; }

    private static string ClosedCode(string? reason)
    {
        var code = reason?.Trim().ToLowerInvariant() ?? string.Empty;
        var isHttp = code.Length == 8 && code.StartsWith("http-", StringComparison.Ordinal) && code[5..].All(char.IsAsciiDigit);
        return isHttp || code is SecretMissing or "unavailable" or ReasonCodes.Unreachable or ReasonCodes.InvalidResponse
            ? code
            : ImageScanReasons.Normalise(code);
    }
}

/// <summary>The cleaning every text from a scanner goes through: it comes from advisory databases, written by third parties.</summary>
public static class ImageScanText
{
    /// <summary>
    /// Control characters become spaces, format characters (bidirectional overrides, zero-width) are dropped, blanks collapse, the
    /// text is cut at <paramref name="max"/> characters; null when nothing is left.
    /// </summary>
    public static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.Format)
            {
                continue;
            }

            var blank = Rune.IsWhiteSpace(rune) || category is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
            if (blank)
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(rune.ToString());
            lastWasSpace = false;
        }

        var text = builder.ToString().TrimEnd();
        if (text.Length == 0)
        {
            return null;
        }

        var runes = text.EnumerateRunes().Take(max).Select(r => r.ToString());
        return string.Concat(runes);
    }
}
