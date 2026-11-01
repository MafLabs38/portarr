using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Catalog.Editing;

namespace Portarr.Api.V1.Catalog;

/// <summary>
/// Guards the catalog write endpoints with the catalog's own switch (D-85): off unless
/// <c>PORTARR__CATALOG__WRITABLE=true</c> (403). Who may write is the <c>catalog.write</c> permission (V7, D-92), checked by
/// the authorization policy placed next to this attribute: a signed-in user whose role holds it, a key of such a user, or
/// one of the deprecated environment keys. The rate limit runs before both, so wrong keys are counted too.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CatalogWriteAccessAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<ICatalogEditor>().Writable)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Catalog read-only",
                Detail = "Catalog writes are off: set PORTARR__CATALOG__WRITABLE=true to edit the catalog through the API.",
            })
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentTypes = { "application/problem+json" },
            };
        }
    }
}
