using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Portarr.Common;
using Portarr.Core.Configuration;
using Portarr.Core.Datastore;
using Prometheus;

namespace Portarr.Http;

public static class HttpServiceCollectionExtensions
{
    /// <summary>Rate limiting policies used by the API controllers.</summary>
    public const string LogStreamsPolicyName = "log-streams";
    public const string UsagePolicyName = "usage";

    /// <summary>Open log streams per client, and for all clients together.</summary>
    public const int MaxLogStreamsPerClient = 4;
    public const int MaxLogStreams = 16;

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

            // Each open log stream holds a connection to a Docker host: a few per client, and in
            // total fewer than the Docker client's connection cap, so streams never starve the
            // inventory poll. The global limiter runs before the per-client policy.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => IsLogStream(context)
                ? RateLimitPartition.GetConcurrencyLimiter(LogStreamsPolicyName, _ => new ConcurrencyLimiterOptions { PermitLimit = MaxLogStreams, QueueLimit = 0 })
                : RateLimitPartition.GetNoLimiter(string.Empty));
            options.AddPolicy(LogStreamsPolicyName, context => RateLimitPartition.GetConcurrencyLimiter(
                ClientKey(context),
                _ => new ConcurrencyLimiterOptions { PermitLimit = MaxLogStreamsPerClient, QueueLimit = 0 }));

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

    private static bool IsLogStream(HttpContext context)
    {
        return context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == LogStreamsPolicyName;
    }

    /// <summary>
    /// Behind a reverse proxy, takes the client address and scheme from X-Forwarded-For and
    /// X-Forwarded-Proto, only when the request comes from a trusted proxy
    /// (<see cref="PortarrOptions.TrustedProxies"/>). Without trusted proxies the headers are
    /// ignored. Must run first, so logging and rate limiting see the real client.
    /// </summary>
    public static WebApplication UsePortarrForwardedHeaders(this WebApplication app)
    {
        var proxies = app.Services.GetRequiredService<IOptions<PortarrOptions>>().Value.TrustedProxyEntries;
        if (proxies.Count == 0)
        {
            return app;
        }

        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };

        // The defaults trust loopback: only the configured proxies are trusted.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var entry in proxies)
        {
            // Entries are checked at startup by PortarrOptionsValidator.
            if (PortarrOptions.TryParseProxy(entry, out var network))
            {
                options.KnownIPNetworks.Add(network);
            }
        }

        app.UseForwardedHeaders(options);
        return app;
    }

    /// <summary>
    /// Defense-in-depth headers on every response: the UI only loads its own scripts, styles,
    /// fonts and images, cannot be framed, and leaks no referrer to the apps it links to.
    /// No 'unsafe-inline': the Vite build emits stylesheet files, and React's style props go
    /// through the CSSOM, which CSP does not restrict. Icons set their own stricter policy.
    /// </summary>
    public static WebApplication UsePortarrSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy =
                "default-src 'self'; img-src 'self' data:; style-src 'self'; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
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
