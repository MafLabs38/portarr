using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Portarr.Core.Catalog.Editing;

namespace Portarr.Api.V1.Catalog;

/// <summary>A route segment that is one of the catalog's editable sections (<see cref="CatalogSections.All"/>), so the route and the editor name them once.</summary>
public sealed class CatalogSectionConstraint : IRouteConstraint
{
    public const string Name = "catalogSection";

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection) =>
        values.TryGetValue(routeKey, out var value) && value is string section && CatalogSections.All.Contains(section, StringComparer.Ordinal);
}
