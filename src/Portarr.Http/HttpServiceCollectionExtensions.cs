using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Common;
using Portarr.Core.Datastore;
using Prometheus;

namespace Portarr.Http;

public static class HttpServiceCollectionExtensions
{
    /// <summary>Rate limiting policies used by the API controllers.</summary>
    public const string LogStreamsPolicyName = "log-streams";
    public const string UsagePolicyName = "usage";

    private const string ReadyTag = "ready";

    /// <summary>
    /// Registers the HTTP plumbing shared by every API version: controllers and RFC 9457 problem details.
    /// </summary>
    public static IMvcBuilder AddPortarrHttp(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Each open log stream holds a connection to a Docker host: a few per client.
            options.AddPolicy(LogStreamsPolicyName, context => RateLimitPartition.GetConcurrencyLimiter(
                ClientKey(context),
                _ => new ConcurrencyLimiterOptions { PermitLimit = 4, QueueLimit = 0 }));

            // Usage makes Docker sample a container twice; the UI polls every 5 s per open page.
            options.AddPolicy(UsagePolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromSeconds(10), QueueLimit = 0 }));
        });
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag]);

        return services.AddControllers();
    }

    private static string ClientKey(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Defense-in-depth headers on every response: the UI only loads its own scripts, styles,
    /// fonts and images, cannot be framed, and leaks no referrer to the apps it links to.
    /// Icons set their own stricter policy.
    /// </summary>
    public static WebApplication UsePortarrSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy =
                "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            await next(context);
        });

        return app;
    }

    /// <summary>
    /// Serves the built frontend files. Must run before routing: the static file middleware
    /// skips requests for which an endpoint (such as the SPA fallback) was already selected.
    /// </summary>
    public static WebApplication UsePortarrFrontendFiles(this WebApplication app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();

        return app;
    }

    /// <summary>
    /// Liveness (<c>/health/live</c>: the process answers) and readiness (<c>/health/ready</c>:
    /// its dependencies, such as the database, answer), for Docker and Kubernetes probes.
    /// </summary>
    public static IEndpointRouteBuilder MapPortarrHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });

        return endpoints;
    }

    /// <summary>
    /// Prometheus metrics of Portarr itself on <c>/metrics</c> (F-SYS-06): HTTP requests, .NET runtime,
    /// process, and <c>portarr_build_info</c>. Call after routing so requests are labelled by route.
    /// </summary>
    public static WebApplication UsePortarrMetrics(this WebApplication app)
    {
        Metrics.CreateGauge("portarr_build_info", "Portarr build running, always 1.", "version")
            .WithLabels(BuildInfo.Version)
            .Set(1);

        app.UseHttpMetrics();
        app.MapMetrics();

        return app;
    }

    /// <summary>
    /// Returns the frontend's index page for client-side routes. Unknown /api, /health and /metrics routes stay plain 404s.
    /// </summary>
    public static WebApplication MapPortarrFrontendFallback(this WebApplication app)
    {
        app.MapFallbackToFile("{**path:regex(^(?!(api|health|metrics)(/|$)).*$)}", "index.html");

        return app;
    }
}
