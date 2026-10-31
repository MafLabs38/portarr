namespace Portarr.Reader;

/// <summary>
/// The reader's settings, from <c>PORTARR_READER_*</c> environment variables (D-100). Only the token is required.
/// </summary>
public sealed class ReaderOptions
{
    public const string EnvironmentPrefix = "PORTARR_READER_";

    /// <summary>Shortest accepted token, in characters: it is the only thing between the network and a window on Docker.</summary>
    public const int MinimumTokenLength = 32;

    /// <summary>The bearer token Portarr must present (<c>PORTARR_READER_TOKEN</c>). Never logged or returned.</summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// The Docker Engine API to read: a Unix socket (<c>unix:///var/run/docker.sock</c>, the default) or the URL of a
    /// read-only socket proxy (<c>http://socket-proxy:2375</c>).
    /// </summary>
    public string Docker { get; init; } = "unix:///var/run/docker.sock";

    /// <summary>Where the reader listens (<c>PORTARR_READER_URLS</c>).</summary>
    public string Urls { get; init; } = "http://+:8485";

    /// <summary>The host's <c>/proc</c>, mounted read-only in the container.</summary>
    public string ProcPath { get; init; } = "/proc";

    /// <summary>The host's <c>/sys</c>, mounted read-only in the container (CPU temperature only).</summary>
    public string SysPath { get; init; } = "/sys";

    /// <summary>The name reported by <c>/v1/info</c>; the machine's host name when empty.</summary>
    public string? HostName { get; init; }

    /// <summary>Throws, naming the setting but never its value, when the settings cannot run the reader.</summary>
    public void Validate()
    {
        if (Token.Length < MinimumTokenLength)
        {
            throw new InvalidOperationException($"{EnvironmentPrefix}TOKEN must be set to at least {MinimumTokenLength} characters (for example the output of: openssl rand -hex 32).");
        }

        if (!Uri.TryCreate(Docker, UriKind.Absolute, out var docker) || docker.Scheme is not ("unix" or "http" or "https" or "tcp") || docker.UserInfo.Length > 0)
        {
            throw new InvalidOperationException($"{EnvironmentPrefix}DOCKER must be unix:///path/to/docker.sock or the http(s):// URL of a read-only Docker socket proxy, without credentials.");
        }
    }
}
