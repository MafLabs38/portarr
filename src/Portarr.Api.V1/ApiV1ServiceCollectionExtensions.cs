using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Portarr.Api.V1;

public static class ApiV1ServiceCollectionExtensions
{
    /// <summary>The OpenAPI document of <c>/api/v1</c> (F-API-01).</summary>
    public const string OpenApiDocumentPath = "/openapi/v1.json";

    /// <summary>The interactive API reference (Scalar).</summary>
    public const string ApiReferencePath = "/docs";

    /// <summary>Controllers, input validators and the OpenAPI document of <c>/api/v1</c>.</summary>
    public static IMvcBuilder AddPortarrApiV1(this IMvcBuilder mvc)
    {
        mvc.Services.AddValidatorsFromAssemblyContaining(typeof(ApiV1ServiceCollectionExtensions), ServiceLifetime.Singleton);

        // Summaries come from the XML doc comments of the controllers. Only /api/v1 is documented: the health
        // probes, /metrics and the MCP endpoint have their own contracts.
        mvc.Services.AddOpenApi("v1", options =>
        {
            options.ShouldInclude = description => description.RelativePath?.StartsWith("api/v1/", StringComparison.Ordinal) == true;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Portarr API",
                    Version = "v1",
                    Description = "API of the Portarr portal: apps, workloads, infrastructure, data and backups, maintenance. Portarr informs, the operator acts: the only writes are Portarr's own settings and, when PORTARR__CATALOG__WRITABLE is on, edits of its catalog file (the description of the fleet, with the key of PORTARR__APIKEY), never the infrastructure.",
                };
                return Task.CompletedTask;
            });
        });

        return mvc.AddApplicationPart(typeof(ApiV1ServiceCollectionExtensions).Assembly);
    }

    /// <summary>
    /// Serves the OpenAPI document at <c>/openapi/v1.json</c> and the Scalar reference at <c>/docs</c>. Scalar's
    /// script is served by Portarr itself (no CDN); its inline bootstrap is allowed by a CSP specific to this page
    /// (see <c>UsePortarrSecurityHeaders</c>).
    /// </summary>
    public static IEndpointRouteBuilder MapPortarrApiDocs(this IEndpointRouteBuilder endpoints)
    {
        // The reference documents the API, not the data: public, like the login page.
        endpoints.MapOpenApi(OpenApiDocumentPath).AllowAnonymous();
        endpoints.MapScalarApiReference(ApiReferencePath, options => options
            .WithTitle("Portarr API")
            .WithOpenApiRoutePattern(OpenApiDocumentPath)
            .DisableDefaultFonts()
            .DisableTelemetry()
            .HideClientButton()
            .HideDeveloperTools()
            .DisableAgent()
            .DisableMcp()
            .WithNonce())
            .AllowAnonymous();

        return endpoints;
    }
}
