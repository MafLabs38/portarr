namespace Portarr.Core.Workloads;

/// <summary>
/// Why a connector call failed, as exposed by the API: a short code, never the raw message (it may name
/// internal hosts and ports, which stay in the logs).
/// </summary>
public static class ReasonCodes
{
    public const string Timeout = "timeout";
    public const string Unreachable = "unreachable";
    public const string InvalidResponse = "invalid-response";

    public static string From(Exception exception)
    {
        return exception switch
        {
            TaskCanceledException or TimeoutException => Timeout,
            HttpRequestException { StatusCode: { } status } => $"http-{(int)status}",
            HttpRequestException or IOException => Unreachable,
            _ => InvalidResponse,
        };
    }
}
