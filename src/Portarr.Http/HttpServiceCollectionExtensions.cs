using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Portarr.Http;

public static class HttpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the HTTP plumbing shared by every API version: controllers and RFC 9457 problem details.
    /// </summary>
    public static IMvcBuilder AddPortarrHttp(this IServiceCollection services)
    {
        services.AddProblemDetails();

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
    /// Returns the frontend's index page for client-side routes. Unknown /api routes stay plain 404s.
    /// </summary>
    public static WebApplication MapPortarrFrontendFallback(this WebApplication app)
    {
        app.MapFallbackToFile("{**path:regex(^(?!api(/|$)).*$)}", "index.html");

        return app;
    }
}
