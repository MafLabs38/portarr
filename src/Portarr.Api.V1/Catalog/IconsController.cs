using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;
using Portarr.Core.Icons;

namespace Portarr.Api.V1.Catalog;

[ApiController]
[Authorize(Policy = Permissions.AppsView)]
[Route("api/v1/icons")]
public sealed class IconsController : ControllerBase
{
    private readonly IIconService _iconService;

    public IconsController(IIconService iconService)
    {
        _iconService = iconService;
    }

    /// <summary>An app icon by name (D-47).</summary>
    /// <remarks>
    /// Third-party SVGs are served with a policy that forbids scripts
    /// and styles, so opening one directly cannot run code in Portarr's origin. The policy only
    /// applies to an icon opened as a document: inside an img tag, an SVG is styled as usual.
    /// </remarks>
    /// <response code="200">The image (SVG, PNG, WebP…).</response>
    /// <response code="404">No such icon.</response>
    [HttpGet("{name}")]
    [Produces("image/svg+xml", "image/png", "image/webp", "image/jpeg")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(string name, CancellationToken cancellationToken)
    {
        var icon = await _iconService.GetAsync(name, cancellationToken);
        if (icon is null)
        {
            return NotFound();
        }

        // Short cache plus validators: an icon dropped in icons/ shows up within minutes, without restart.
        Response.Headers.CacheControl = "public, max-age=300";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        Response.Headers.XContentTypeOptions = "nosniff";

        var lastModified = global::System.IO.File.GetLastWriteTimeUtc(icon.Path);
        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{lastModified.Ticks:x}\"");

        return PhysicalFile(icon.Path, icon.ContentType, new DateTimeOffset(lastModified), etag);
    }
}
