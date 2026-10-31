using Portarr.Core.Checks;

namespace Portarr.Core.Test.Checks;

/// <summary>Answers each request with a function; can play the TLS handshake by filling the certificate capture.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public DateTimeOffset? CertificateExpiresAt { get; init; }

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (CertificateExpiresAt is not null && request.Options.TryGetValue(HttpCheckRunner.CertificateKey, out var capture))
        {
            capture.ExpiresAt = CertificateExpiresAt;
        }

        return _respond(request, cancellationToken);
    }
}
