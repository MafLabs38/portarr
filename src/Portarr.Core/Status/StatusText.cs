using Portarr.Core.Checks;

namespace Portarr.Core.Status;

/// <summary>
/// The status reasons Core gives as codes, said in English for the assistants that read the knowledge base and the digest. The
/// interface translates the same codes itself; a reason that is not a code is already a sentence and stays as it is.
/// </summary>
public static class StatusText
{
    public static string Of(ResolvedStatus status)
    {
        string Arg(int index) => status.Args is { } args && index < args.Count ? args[index] : string.Empty;

        return status.Reason switch
        {
            StatusReasons.NotChecked => "Not checked yet",
            StatusReasons.CheckFailed => "Portarr could not run this check",
            CheckErrors.Timeout => "Timeout",
            CheckErrors.DnsFailure => "DNS lookup failed",
            CheckErrors.ConnectionFailed => "Connection failed",
            CheckErrors.TlsInvalid => "Invalid certificate",
            CheckErrors.KeywordMissing => "Keyword not found",
            CheckErrors.InvalidResponse => "Unexpected response",
            StatusReasons.HttpStatus => $"HTTP {Arg(0)}",
            StatusReasons.CertificateExpired => "Certificate expired",
            StatusReasons.CertificateExpiresToday => "Certificate expires today",
            StatusReasons.CertificateExpiresInOneDay => "Certificate expires in 1 day",
            StatusReasons.CertificateExpiresInDays => $"Certificate expires in {Arg(0)} days",
            StatusReasons.CheckUp => $"HTTP {Arg(0)} in {Arg(1)} ms",
            _ => status.Reason,
        };
    }
}
