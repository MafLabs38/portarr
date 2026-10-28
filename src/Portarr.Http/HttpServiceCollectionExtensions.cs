using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Common;
using Portarr.Core.Datastore;
using Prometheus;

namespace Portarr.Http;

public static class HttpServiceCollectionExtensions
{
    private const string ReadyTag = "ready";

    /// <summary>
    /// Registers the HTTP plumbing shared by every API version: controllers and RFC 9457 problem details.
    /// </summary>
    public static IMvcBuilder AddPortarrHttp(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag]);

        return services.AddControllers();
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
