using System.Security.Cryptography;
using System.Text;

namespace Portarr.Reader;

/// <summary>
/// Every route but <c>/healthz</c> needs the reader's token, sent as <c>Authorization: Bearer</c> or <c>X-Api-Key</c>
/// (D-100). Compared by SHA-256 hash in constant time; the token is never logged and a wrong one learns nothing.
/// </summary>
public sealed class TokenMiddleware
{
    public const string HealthPath = "/healthz";

    private readonly RequestDelegate _next;
    private readonly byte[] _expected;

    public TokenMiddleware(RequestDelegate next, ReaderOptions options)
    {
        _next = next;
        _expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Token));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Equals(HealthPath, StringComparison.Ordinal) || Matches(Presented(context.Request)))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer realm=\"portarr-reader\"";
    }

    private bool Matches(string? presented)
    {
        return presented is { Length: > 0 } && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(presented)), _expected);
    }

    private static string? Presented(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return authorization[bearer.Length..].Trim();
        }

        var key = request.Headers["X-Api-Key"].ToString();
        return key.Length > 0 ? key : null;
    }
}
