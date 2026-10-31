namespace Portarr.Core.Checks;

/// <summary>
/// One probe of an app URL. <c>Error</c> is a reason code (see <see cref="CheckErrors"/>), null when the
/// check passed; never a raw exception message. <c>CertificateExpiresAt</c> is read from the TLS handshake.
/// </summary>
public sealed record CheckResult(
    DateTimeOffset At,
    int? StatusCode,
    TimeSpan? Latency,
    string? Error,
    DateTimeOffset? CertificateExpiresAt)
{
    public bool Up => Error is null;
}

/// <summary>Reason codes of a failed check, shared with the API and the UI.</summary>
public static class CheckErrors
{
    public const string Timeout = "timeout";
    public const string DnsFailure = "dns-failure";
    public const string ConnectionFailed = "connection-failed";
    public const string TlsInvalid = "tls-invalid";
    public const string TlsExpired = "tls-expired";
    public const string KeywordMissing = "keyword-missing";
    public const string InvalidResponse = "invalid-response";

    /// <summary><c>http-502</c>: a status outside the expected ones.</summary>
    public static string UnexpectedStatus(int statusCode) => $"http-{statusCode}";
}
