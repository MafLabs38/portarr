using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using Portarr.Common;
using Portarr.Core.Catalog;

namespace Portarr.Core.Checks;

public interface IHttpCheckRunner
{
    /// <summary>Probes the URL once; never throws for a failing target, only when <paramref name="cancellationToken"/> is cancelled.</summary>
    Task<CheckResult> RunAsync(Uri target, CheckDefinition definition, CancellationToken cancellationToken);
}

/// <summary>
/// One HTTP check (D-50): a GET without following redirects, a fresh connection each time so the TLS
/// certificate is read on every check (D-52), the body read only for a keyword and capped.
/// </summary>
public sealed class HttpCheckRunner : IHttpCheckRunner
{
    public const string HttpClientName = "checks";

    /// <summary>Largest part of a body searched for a keyword.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>Where the certificate validation callback leaves what it saw, per request.</summary>
    public static readonly HttpRequestOptionsKey<CertificateCapture> CertificateKey = new("Portarr.Checks.Certificate");

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;

    public HttpCheckRunner(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// The primary handler of the checks client: redirects not followed, no cookies, and the server
    /// certificate recorded during the normal validation, which stays on (an invalid certificate fails).
    /// </summary>
    public static HttpMessageHandler CreateHandler()
    {
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ServerCertificateCustomValidationCallback = (request, certificate, _, errors) =>
            {
                if (certificate is not null && request.Options.TryGetValue(CertificateKey, out var capture))
                {
                    capture.ExpiresAt = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                }

                return errors == SslPolicyErrors.None;
            },
        };
    }

    public async Task<CheckResult> RunAsync(Uri target, CheckDefinition definition, CancellationToken cancellationToken)
    {
        var at = _timeProvider.GetUtcNow();
        var started = _timeProvider.GetTimestamp();
        var capture = new CertificateCapture();

        using var timeout = new CancellationTokenSource(definition.Timeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Options.Set(CertificateKey, capture);
        request.Headers.ConnectionClose = true;
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Portarr", BuildInfo.Version));

        try
        {
            using var response = await _httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var latency = _timeProvider.GetElapsedTime(started);
            var statusCode = (int)response.StatusCode;

            string? error = null;
            if (!definition.Accepts(statusCode))
            {
                error = CheckErrors.UnexpectedStatus(statusCode);
            }
            else if (definition.Keyword is not null && !await ContainsAsync(response.Content, definition.Keyword, linked.Token))
            {
                error = CheckErrors.KeywordMissing;
            }

            return new CheckResult(at, statusCode, latency, error, capture.ExpiresAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CheckResult(at, null, null, CheckErrors.Timeout, capture.ExpiresAt);
        }
        catch (HttpRequestException exception)
        {
            return new CheckResult(at, null, null, ReasonCode(exception, capture, at), capture.ExpiresAt);
        }
    }

    private static string ReasonCode(HttpRequestException exception, CertificateCapture capture, DateTimeOffset now)
    {
        return exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => CheckErrors.DnsFailure,
            HttpRequestError.SecureConnectionError when capture.ExpiresAt < now => CheckErrors.TlsExpired,
            HttpRequestError.SecureConnectionError => CheckErrors.TlsInvalid,
            HttpRequestError.ConnectionError or HttpRequestError.ProxyTunnelError => CheckErrors.ConnectionFailed,
            _ => CheckErrors.InvalidResponse,
        };
    }

    private static async Task<bool> ContainsAsync(HttpContent content, string keyword, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxBodyBytes];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
        {
            length += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, length).Contains(keyword, StringComparison.Ordinal);
    }
}

/// <summary>Filled by the certificate validation callback during the TLS handshake.</summary>
public sealed class CertificateCapture
{
    public DateTimeOffset? ExpiresAt { get; set; }
}
