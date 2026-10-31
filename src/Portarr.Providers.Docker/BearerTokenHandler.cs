using System.Net.Http.Headers;

namespace Portarr.Providers.Docker;

/// <summary>Adds <c>Authorization: Bearer</c> from a token read at each request (the Portarr reader asks for one, D-100); nothing when it is empty.</summary>
internal sealed class BearerTokenHandler(Func<string?> token) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (token() is { Length: > 0 } value)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
