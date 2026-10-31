using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;

namespace Portarr.Api.V1.Catalog;

/// <summary>
/// Guards the catalog write endpoints until Portarr has accounts (C2, D-85): off unless
/// <c>PORTARR__CATALOG__WRITABLE=true</c> (403), and only with the key of <c>PORTARR__APIKEY</c> in <c>X-Api-Key</c>
/// or <c>Authorization: Bearer</c> (401), compared in constant time. The rate limit runs before it, so wrong keys
/// are counted too. The two catalog reads (one item as the file holds it, the audit log) carry it with
/// <see cref="RequireWritable"/> off: they show the connectors' addresses and who changed what, so they need the same
/// key, and they answer whether or not writes are on.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CatalogWriteAccessAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>Refuse with 403 while catalog writes are off (the default). A read sets it to false: only the key is checked.</summary>
    public bool RequireWritable { get; init; } = true;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<PortarrOptions>>().Value;
        if (RequireWritable && !options.Catalog.Writable)
        {
            context.Result = Problem(StatusCodes.Status403Forbidden, "Catalog read-only", "Catalog writes are off: set PORTARR__CATALOG__WRITABLE=true (and PORTARR__APIKEY) to edit the catalog through the API.");
            return;
        }

        var request = context.HttpContext.Request;
        var presented = ApiKeys.Read(request.Headers[ApiKeys.HeaderName].ToString(), request.Headers.Authorization.ToString());
        if (!ApiKeys.Matches(presented, options.ApiKey))
        {
            context.HttpContext.Response.Headers.WWWAuthenticate = "Bearer realm=\"portarr-api\"";
            context.Result = Problem(StatusCodes.Status401Unauthorized, "API key required", $"Send the key of PORTARR__APIKEY in {ApiKeys.HeaderName} or Authorization: Bearer.");
        }
    }

    private static ObjectResult Problem(int status, string title, string detail)
    {
        return new ObjectResult(new ProblemDetails { Status = status, Title = title, Detail = detail }) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
