using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Icons;

namespace Portarr.Api.V1.Catalog;

[ApiController]
[Route("api/v1/icons")]
public sealed class IconsController : ControllerBase
{
    private readonly IIconService _iconService;

    public IconsController(IIconService iconService)
    {
        _iconService = iconService;
    }

    /// <summary>
    /// An app icon by name (D-47). Third-party SVGs are served with a policy that forbids scripts,
    /// so opening one directly cannot run code in Portarr's origin.
    /// </summary>
    [HttpGet("{name}")]
    public async Task<IActionResult> GetAsync(string name, CancellationToken cancellationToken)
    {
        var icon = await _iconService.GetAsync(name, cancellationToken);
        if (icon is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=86400";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        Response.Headers.XContentTypeOptions = "nosniff";

        return PhysicalFile(icon.Path, icon.ContentType);
    }
}
