namespace Portarr.Core.Catalog;

/// <summary>
/// The HTTP check of an app (D-50). <c>Url</c> null: the app's public URL, else its LAN URL. Up when the
/// status is in <c>Expect</c> (and the body contains <c>Keyword</c> when set). A failing critical check
/// turns the app Down; a non-critical one only degrades it.
/// </summary>
public sealed record CheckDefinition(
    Uri? Url,
    IReadOnlyList<StatusCodeRange> Expect,
    string? Keyword,
    TimeSpan Interval,
    TimeSpan Timeout,
    bool Critical)
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>2xx and 3xx: redirects are not followed, an SSO redirect means the app answers.</summary>
    public static readonly IReadOnlyList<StatusCodeRange> DefaultExpect = [new(200, 399)];

    /// <summary>What every web app gets without a <c>check:</c> entry.</summary>
    public static CheckDefinition Default { get; } = new(null, DefaultExpect, null, DefaultInterval, DefaultTimeout, true);

    public bool Accepts(int statusCode) => Expect.Any(range => range.Contains(statusCode));

    // By value, Expect included: a catalog reload that changes nothing keeps the check history.
    public bool Equals(CheckDefinition? other)
    {
        return other is not null
            && Url == other.Url
            && Expect.SequenceEqual(other.Expect)
            && Keyword == other.Keyword
            && Interval == other.Interval
            && Timeout == other.Timeout
            && Critical == other.Critical;
    }

    public override int GetHashCode() => HashCode.Combine(Url, Expect.Count, Keyword, Interval, Timeout, Critical);
}

/// <summary>An inclusive range of HTTP status codes; a single code is a range of one.</summary>
public sealed record StatusCodeRange(int From, int To)
{
    public bool Contains(int statusCode) => statusCode >= From && statusCode <= To;
}
