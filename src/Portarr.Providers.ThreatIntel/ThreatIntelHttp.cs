using System.Net;
using System.Text;
using Portarr.Core.Maintenance;

namespace Portarr.Providers.ThreatIntel;

/// <summary>How the exploit intelligence is fetched (D-133): one named client, GET only, https only, a bounded time and size, https redirects only.</summary>
internal static class ThreatIntelHttp
{
    public const string ClientName = "threat-intel";

    /// <summary>The time a whole read may take, body included: the client's own timeout stops at the response headers.</summary>
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The largest KEV catalog read: it is about 1.5 MiB for some 1,500 entries.</summary>
    public const long MaxKevBytes = 16 * 1024 * 1024;

    /// <summary>The largest EPSS file read as sent (compressed): it is a few MiB for some 300,000 rows.</summary>
    public const long MaxEpssCompressedBytes = 16 * 1024 * 1024;

    /// <summary>The largest EPSS file read once decompressed: bounds a file that inflates far beyond what it was sent as.</summary>
    public const long MaxEpssBytes = 256 * 1024 * 1024;

    /// <summary>The longest line of the EPSS file kept; a longer one is skipped, never held whole.</summary>
    public const int MaxLineLength = 1024;

    /// <summary>A CDN sends a file through a redirect or two; more is a loop.</summary>
    public const int MaxRedirects = 3;

    /// <summary>
    /// Sends a GET to <paramref name="url"/> and follows redirects by hand, to https addresses only (the handler follows none): a redirect to http, or
    /// to an address with credentials, is refused before anything is sent there. Answers the response once it is a success, headers read.
    /// </summary>
    public static async Task<HttpResponseMessage> GetAsync(HttpClient client, Uri url, CancellationToken cancellationToken)
    {
        if (!IsSafe(url))
        {
            throw new ExploitIntelligenceUnavailableException(ExploitIntelligenceUnavailableException.InsecureUrl);
        }

        var current = url;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!IsRedirect(response.StatusCode))
            {
                if (!response.IsSuccessStatusCode)
                {
                    var status = response.StatusCode;
                    response.Dispose();
                    throw new HttpRequestException($"GET answered {(int)status}.", null, status);
                }

                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            current = Target(current, location);
        }

        throw new ExploitIntelligenceUnavailableException(ExploitIntelligenceUnavailableException.TooManyRedirects);
    }

    /// <summary>The address a redirect points at, resolved against the one that answered; refused unless it is https without credentials.</summary>
    private static Uri Target(Uri current, Uri? location)
    {
        if (location is null)
        {
            throw new ExploitIntelligenceUnavailableException(ExploitIntelligenceUnavailableException.InsecureRedirect);
        }

        // On Unix a path alone ("/file") reads as an absolute file address: it is a path relative to the source.
        var target = location.IsAbsoluteUri && !location.IsFile ? location : new Uri(current, location.OriginalString);
        return IsSafe(target) ? target : throw new ExploitIntelligenceUnavailableException(ExploitIntelligenceUnavailableException.InsecureRedirect);
    }

    private static bool IsSafe(Uri url) => url is { IsAbsoluteUri: true, UserInfo.Length: 0 } && url.Scheme == Uri.UriSchemeHttps;

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// The lines of a text stream, read in chunks: a line longer than <paramref name="maxLength"/> is skipped, never held whole, so a file without line
    /// breaks costs no more memory than a chunk. The line breaks (<c>\n</c> or <c>\r\n</c>) are not part of the lines.
    /// </summary>
    public static async IAsyncEnumerable<string> ReadLinesAsync(Stream stream, int maxLength, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: true);
        var buffer = new char[64 * 1024];
        var line = new BoundedLine(maxLength);
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            var chunk = buffer.AsMemory(0, read);
            int newline;
            while ((newline = chunk.Span.IndexOf('\n')) >= 0)
            {
                line.Append(chunk[..newline]);
                chunk = chunk[(newline + 1)..];
                if (line.Take() is { } text)
                {
                    yield return text;
                }
            }

            line.Append(chunk);
        }

        if (line.Take() is { Length: > 0 } last)
        {
            yield return last;
        }
    }

    /// <summary>A line being read: forgotten as soon as it grows past its bound.</summary>
    private sealed class BoundedLine(int maxLength)
    {
        private readonly StringBuilder _text = new();
        private bool _overlong;

        public void Append(ReadOnlyMemory<char> part)
        {
            if (_overlong)
            {
                return;
            }

            if (_text.Length + part.Length > maxLength)
            {
                _overlong = true;
                _text.Clear();
                return;
            }

            _text.Append(part.Span);
        }

        /// <summary>The line, or null when it was too long; the next line starts empty.</summary>
        public string? Take()
        {
            var text = _overlong ? null : _text.ToString().TrimEnd('\r');
            _text.Clear();
            _overlong = false;
            return text;
        }
    }
}
