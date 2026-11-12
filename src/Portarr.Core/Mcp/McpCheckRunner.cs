using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Portarr.Common;
using Portarr.Core.Catalog;
using Portarr.Core.Checks;

namespace Portarr.Core.Mcp;

/// <summary>
/// The probe of a remote MCP server (D-114): one <c>initialize</c> request, which any MCP server answers whatever its tools,
/// without credentials, without following a redirect, with the checks' client (fresh connection, valid certificate required).
/// A JSON-RPC answer, even an error, means a server is there; 401 and 403 mean it is there and enforcing; anything else is a
/// failure with the reason code the checks already use. The body is read up to <see cref="HttpCheckRunner.MaxBodyBytes"/>, and
/// a session the server opened is closed (best effort).
/// </summary>
public sealed class McpCheckRunner : ICheckRunner
{
    public const string SessionHeader = "Mcp-Session-Id";

    private const string InitializeBody = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"Portarr","version":"%VERSION%"}}}""";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;

    public McpCheckRunner(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
    }

    public string Type => CheckTypes.Mcp;

    public async Task<CheckResult> RunAsync(Uri target, CheckDefinition definition, CancellationToken cancellationToken)
    {
        var at = _timeProvider.GetUtcNow();
        var started = _timeProvider.GetTimestamp();
        var capture = new CertificateCapture();

        using var timeout = new CancellationTokenSource(definition.Timeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = new StringContent(InitializeBody.Replace("%VERSION%", BuildInfo.Version, StringComparison.Ordinal), Encoding.UTF8, "application/json"),
        };
        request.Options.Set(HttpCheckRunner.CertificateKey, capture);
        request.Headers.ConnectionClose = true;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Portarr", BuildInfo.Version));

        var client = _httpClientFactory.CreateClient(HttpCheckRunner.HttpClientName);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var latency = _timeProvider.GetElapsedTime(started);
            var statusCode = (int)response.StatusCode;

            string? error = null;
            if (statusCode is 401 or 403)
            {
                // The server is there and asks for credentials: healthy, said so by the state.
            }
            else if (!IsJsonRpc(await ReadBodyAsync(response.Content, linked.Token)))
            {
                error = statusCode >= 400 ? CheckErrors.UnexpectedStatus(statusCode) : CheckErrors.InvalidResponse;
            }

            await EndSessionAsync(client, target, response, linked.Token);
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

    /// <summary>A JSON-RPC 2.0 message, bare or as the first event of a server-sent stream.</summary>
    internal static bool IsJsonRpc(string body)
    {
        var text = body.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (!text.StartsWith('{'))
        {
            var data = text.Split('\n').Select(line => line.TrimEnd('\r')).FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal));
            if (data is null)
            {
                return false;
            }

            text = data["data:".Length..].Trim();
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("jsonrpc", out var version)
                && version.ValueKind == JsonValueKind.String
                && version.GetString() == "2.0";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<string> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[HttpCheckRunner.MaxBodyBytes];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
        {
            length += read;

            // A server-sent stream may stay open after its first event: that event is all the probe needs.
            if (content.Headers.ContentType?.MediaType == "text/event-stream" && Encoding.UTF8.GetString(buffer, 0, length).Contains("\n\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    private static async Task EndSessionAsync(HttpClient client, Uri target, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.Headers.TryGetValues(SessionHeader, out var values) || values.FirstOrDefault() is not { Length: > 0 } session)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, target);
            request.Headers.TryAddWithoutValidation(SessionHeader, session);
            request.Headers.ConnectionClose = true;
            using var _ = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // Best effort: the server drops an idle session by itself.
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
}
