using Portarr.Api.V1.Catalog;
using Portarr.Core.Checks;
using Portarr.Core.Status;

namespace Portarr.Api.V1.Checks;

/// <summary>One probe. <c>Error</c> is a reason code (timeout, dns-failure, http-502…), never a raw message.</summary>
public sealed record CheckResultResource(
    DateTimeOffset At,
    bool Up,
    int? StatusCode,
    int? LatencyMs,
    string? Error,
    DateTimeOffset? CertificateExpiresAt);

/// <summary>The check of an app as shown on its tile: its own status and the latest result.</summary>
public sealed record AppCheckResource(string Url, bool Critical, StatusResource Status, CheckResultResource? Latest);

public static class CheckResourceMapper
{
    public static AppCheckResource ToResource(this AppCheckView check)
    {
        return new AppCheckResource(check.Target.ToString(), check.Definition.Critical, StatusResource.From(check.Status), check.State?.Latest?.ToResource());
    }

    public static CheckResultResource ToResource(this CheckResult result)
    {
        return new CheckResultResource(
            result.At,
            result.Up,
            result.StatusCode,
            result.Latency is { } latency ? (int)latency.TotalMilliseconds : null,
            result.Error,
            result.CertificateExpiresAt);
    }
}
