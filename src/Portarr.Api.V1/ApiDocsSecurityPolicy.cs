using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Scalar.AspNetCore;

namespace Portarr.Api.V1;

/// <summary>
/// The Content-Security-Policy of the API reference page (<c>/docs</c>, Scalar), the one exception to the global
/// policy of <c>UsePortarrSecurityHeaders</c> (no <c>'unsafe-inline'</c>). Scalar needs two things the UI does not:
/// a small inline module script that starts it, and inline styles injected at run time. Scripts stay strict: the
/// inline script carries a per-request nonce (Scalar's <c>WithNonce</c>) and every other script comes from
/// <c>'self'</c>: Scalar's own bundle is served by Portarr, no CDN, no third-party host. Only styles get
/// <c>'unsafe-inline'</c>; the page shows Portarr's own OpenAPI document, nothing a visitor can write.
/// </summary>
public static partial class ApiDocsSecurityPolicy
{
    private const string PolicyBefore = "default-src 'self'; script-src 'self' 'nonce-";

    private const string PolicyAfter =
        "'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; font-src 'self' data:; connect-src 'self'; worker-src 'self' blob:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    /// <summary>
    /// Replaces the CSP of the reference page's HTML by the nonce policy above. Must run after
    /// <c>UsePortarrSecurityHeaders</c>. The other <c>/docs</c> files (the script) keep the global policy.
    /// </summary>
    public static WebApplication UsePortarrApiDocsSecurityPolicy(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method) && ReferencePagePattern().IsMatch(context.Request.Path.Value ?? string.Empty))
            {
                // The endpoint stores its nonce once it renders, which is after this middleware: set the header as the response starts.
                context.Response.OnStarting(() =>
                {
                    if (context.Items[ScalarOptions.NonceHttpContextItemKey] is string nonce)
                    {
                        context.Response.Headers.ContentSecurityPolicy = $"{PolicyBefore}{nonce}{PolicyAfter}";
                    }

                    return Task.CompletedTask;
                });
            }

            await next(context);
        });

        return app;
    }

    /// <summary><c>/docs</c>, <c>/docs/</c> and <c>/docs/{document}</c>: the HTML, not the files next to it.</summary>
    [GeneratedRegex("^/docs(/[^/.]*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePagePattern();
}
