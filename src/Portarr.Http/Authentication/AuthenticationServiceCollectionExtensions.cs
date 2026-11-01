using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Accounts;
using Portarr.Core.Configuration;

namespace Portarr.Http.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    /// <summary>The cookie scheme (the browser's session).</summary>
    public const string CookieScheme = "Cookies";

    /// <summary>Picks the cookie or the key scheme per request, so one default serves the UI and the API clients.</summary>
    public const string DefaultScheme = "Portarr";

    /// <summary>The policy of operations only a browser session may do (keys, password, profile): an API key cannot.</summary>
    public const string SessionPolicyName = "session";

    public const string SessionCookieName = "portarr_session";

    /// <summary>The name when the cookie is always <c>Secure</c>: the prefix makes the browser refuse one set without it, or for another path or domain.</summary>
    public const string SecureSessionCookieName = "__Host-portarr_session";

    /// <summary>Header every cookie-authenticated write must carry (CSRF, on top of SameSite=Strict). API-key requests are exempt.</summary>
    public const string CsrfHeaderName = "X-Requested-With";

    public const string CsrfHeaderValue = "portarr";

    /// <summary>
    /// Authentication and authorization (V7): a cookie session and API keys behind one default scheme, one policy per
    /// permission (named after it), and a fallback policy that requires a signed-in principal everywhere else, so an
    /// endpoint is protected unless it says otherwise.
    /// </summary>
    public static IServiceCollection AddPortarrAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = DefaultScheme;
                options.DefaultAuthenticateScheme = DefaultScheme;
                options.DefaultChallengeScheme = DefaultScheme;
                options.DefaultForbidScheme = DefaultScheme;
                options.DefaultSignInScheme = CookieScheme;
                options.DefaultSignOutScheme = CookieScheme;
            })
            .AddPolicyScheme(DefaultScheme, DefaultScheme, options =>
                options.ForwardDefaultSelector = context => ApiKeyAuthenticationHandler.HasKey(context.Request) ? ApiKeyAuthenticationHandler.SchemeName : CookieScheme)
            .AddCookie(CookieScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

        services.AddOptions<CookieAuthenticationOptions>(CookieScheme).Configure<IOptions<AuthOptions>>((options, auth) =>
        {
            var always = auth.Value.CookieSecurity == CookieSecurity.Always;
            options.Cookie.Name = always ? SecureSessionCookieName : SessionCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;

            // Auto: Secure whenever the request is HTTPS (behind a trusted proxy, as forwarded headers say). Always: for a proxy Portarr does not trust.
            options.Cookie.SecurePolicy = always ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.Events.OnSigningIn = context =>
            {
                WarnIfBehindAnUntrustedProxy(context.HttpContext);
                return Task.CompletedTask;
            };
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromDays(auth.Value.SessionDays);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context =>
            {
                // The UI handles a 401 itself (it shows the login page); API clients are told how to authenticate.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer realm=\"portarr\"";
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
            options.Events.OnValidatePrincipal = ValidateSessionAsync;
        });

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(SessionPolicyName, policy => policy.RequireAuthenticatedUser().RequireClaim(PortarrClaims.Method, AuthMethods.Cookie));
        services.AddOptions<AuthorizationOptions>().Configure(options =>
        {
            foreach (var key in Permissions.All.Select(permission => permission.Key))
            {
                options.AddPolicy(key, policy => policy.RequireAuthenticatedUser().RequireClaim(PortarrClaims.Permission, key));
            }
        });

        return services;
    }

    /// <summary>
    /// Keeps the keys that sign the session cookie in the data folder (<c>keys</c>, readable by the service account only),
    /// so a container recreated on the same volume does not sign everyone out.
    /// </summary>
    public static IServiceCollection AddPortarrDataProtection(this IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("Portarr");
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<PortarrOptions>, ILoggerFactory>((options, portarr, loggers) =>
        {
            var folder = Path.Combine(portarr.Value.DataFolder, "keys");
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(folder), loggers);
        });
        return services;
    }

    private static int _untrustedProxyWarned;

    // A request that says it came over HTTPS (X-Forwarded-Proto) but is still http here crossed a proxy Portarr ignores:
    // the cookie goes out without Secure. Said once, with the way out.
    private static void WarnIfBehindAnUntrustedProxy(HttpContext context)
    {
        if (context.Request.IsHttps || context.Request.Headers["X-Forwarded-Proto"] != "https" || Interlocked.Exchange(ref _untrustedProxyWarned, 1) == 1)
        {
            return;
        }

        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Portarr.Auth").LogWarning(
            "A sign-in came through a proxy that Portarr does not trust (X-Forwarded-Proto is https, the request is http): the session cookie is issued without Secure. Add the proxy to PORTARR__TRUSTEDPROXIES, or set PORTARR__AUTH__COOKIESECURITY=Always");
    }

    /// <summary>
    /// Rejects requests that change something with a cookie session and no <c>X-Requested-With: portarr</c> header (a
    /// browser cannot add one to a cross-site form or fetch without a CORS preflight Portarr never grants), and so do the
    /// sign-in endpoints. Requests with a key are exempt: a key is not sent by the browser on its own. Anything else
    /// unauthenticated is simply a 401. Place after authentication.
    /// </summary>
    public static WebApplication UsePortarrCsrfProtection(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if ((request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) || request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
                && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method) && !HttpMethods.IsTrace(request.Method)
                && !ApiKeyAuthenticationHandler.HasKey(request)
                && (context.User.Identity?.IsAuthenticated == true || request.Path.StartsWithSegments("/api/v1/auth", StringComparison.OrdinalIgnoreCase))
                && request.Headers[CsrfHeaderName] != CsrfHeaderValue)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Missing header", Detail = $"Send {CsrfHeaderName}: {CsrfHeaderValue} with requests that change something." },
                    options: null,
                    contentType: "application/problem+json");
                return;
            }

            await next(context);
        });

        return app;
    }

    /// <summary>Logs a warning for each deprecated environment key that is set (they work, as the <c>system</c> principal).</summary>
    public static WebApplication WarnAboutSystemKeys(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Portarr.Auth");
        foreach (var source in app.Services.GetServices<ISystemApiKeySource>().Where(s => !string.IsNullOrWhiteSpace(s.Key)))
        {
            logger.LogWarning("{Variable} is set: it works as a bootstrap key with the view permissions and catalog.write (the 'system' principal) and is deprecated; create per-user API keys instead", source.Name);
        }

        return app;
    }

    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var accounts = context.HttpContext.RequestServices.GetRequiredService<IAccountService>();
        var identity = principal?.UserId() is { } id
            ? await accounts.ResolveAsync(id, principal.FindFirstValue(PortarrClaims.Stamp), context.HttpContext.RequestAborted)
            : null;
        if (identity is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieScheme);
            return;
        }

        // The session only names the account: its role and permissions are read now, so an edit takes effect at once.
        context.ReplacePrincipal(PortarrClaims.CreatePrincipal(identity, AuthMethods.Cookie, CookieScheme));
    }
}
