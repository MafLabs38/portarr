using System.Text.Json.Nodes;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Portarr.Api.V1.Catalog;
using Portarr.Core.Accounts;
using Portarr.Core.Catalog.Editing;
using Portarr.Core.Portal;
using Portarr.Core.SystemInfo;
using Portarr.Http;

namespace Portarr.Api.V1.Portal;

/// <summary>
/// The identity of the portal (D-91): the name of the infrastructure, a logo and the version of the running Portarr,
/// shown in the sidebar. Reading is open like the rest of the UI's data; changing the name or the logo is a catalog
/// write (D-85): off unless <c>PORTARR__CATALOG__WRITABLE=true</c>, keyed by <c>PORTARR__APIKEY</c>, versioned,
/// validated on the whole file, audited.
/// </summary>
[ApiController]
[Route("api/v1/portal")]
[Produces("application/json")]
public sealed class PortalController : ControllerBase
{
    private const string Channel = "api";

    /// <summary>The request may carry the logo plus the multipart framing.</summary>
    private const long MaxRequestBytes = PortalLogo.MaxBytes + 64 * 1024;

    private readonly IPortalService _portal;
    private readonly ICatalogEditor _editor;
    private readonly ISystemStatusService _systemStatusService;
    private readonly IValidator<PortalRequest> _validator;

    public PortalController(IPortalService portal, ICatalogEditor editor, ISystemStatusService systemStatusService, IValidator<PortalRequest> validator)
    {
        _portal = portal;
        _editor = editor;
        _systemStatusService = systemStatusService;
        _validator = validator;
    }

    /// <summary>The name, logo and badge colors of the portal and the version of this Portarr.</summary>
    [HttpGet]
    [ProducesResponseType<PortalResource>(StatusCodes.Status200OK)]
    public async Task<PortalResource> GetAsync(CancellationToken cancellationToken)
    {
        var status = await _systemStatusService.GetStatusAsync(cancellationToken);
        return _portal.Get().ToResource(status.Version, _editor.Writable);
    }

    /// <summary>The portal's logo, as the catalog declares it.</summary>
    /// <remarks>
    /// Served like an icon (D-47): an SVG gets a policy that forbids scripts and styles, so opening it directly cannot
    /// run code in Portarr's origin. The <c>v</c> parameter of <c>logoUrl</c> is the file's hash: with it the answer is
    /// cached for a year.
    /// </remarks>
    /// <param name="v">The hash from <c>logoUrl</c>.</param>
    /// <response code="200">The image (PNG, WebP or SVG).</response>
    /// <response code="404">No logo is declared, or it cannot be used: Portarr's own applies.</response>
    [HttpGet("logo")]
    [AllowAnonymous]
    [Produces("image/png", "image/webp", "image/svg+xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetLogo([FromQuery] string? v)
    {
        if (_portal.Get().LogoFile is not { } logo)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = v == logo.Hash ? "public, max-age=31536000, immutable" : "public, max-age=300";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        Response.Headers.XContentTypeOptions = "nosniff";

        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{logo.Hash}\"");
        return File(logo.Content, logo.ContentType, logo.Modified, etag);
    }

    /// <summary>Sets the name of the infrastructure (empty goes back to Portarr's own) and/or the colors of the badge texts (empty goes back to the accent).</summary>
    /// <param name="request">The name and/or the colors, and the catalog version you read (optional).</param>
    /// <param name="cancellationToken">Cancels the edit.</param>
    /// <response code="400">The name or a color is not valid: errors by field path.</response>
    /// <response code="401">The API key is missing or wrong.</response>
    /// <response code="403">Catalog writes are off.</response>
    /// <response code="409">The catalog changed since the version sent.</response>
    [HttpPut]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [CatalogWriteAccess]
    [EnableRateLimiting(HttpServiceCollectionExtensions.CatalogWritesPolicyName)]
    [ProducesResponseType<CatalogEditResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetIdentityAsync(PortalRequest request, CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var operation = (request.Name, request.Colors) switch
        {
            (not null, not null) => "set-identity",
            (not null, null) => "set-name",
            _ => "set-colors",
        };
        return await EditAsync(operation, request.Version, CatalogEdits.SetPortal(request.Name, null, request.Colors?.Name, request.Colors?.Version), cancellationToken);
    }

    /// <summary>
    /// Uploads the logo (multipart form, field <c>file</c>): a PNG, WebP or SVG of at most 1 MiB, checked by its content.
    /// It is stored in the <c>branding</c> folder next to the catalog, a previous file of that name kept, and
    /// <c>portal.logo</c> is set to it.
    /// </summary>
    /// <param name="file">The image.</param>
    /// <param name="cancellationToken">Cancels the upload.</param>
    /// <response code="400">Not an image of an accepted format, or too large.</response>
    /// <response code="401">The API key is missing or wrong.</response>
    /// <response code="403">Catalog writes are off.</response>
    [HttpPut("logo")]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [CatalogWriteAccess]
    [EnableRateLimiting(HttpServiceCollectionExtensions.CatalogWritesPolicyName)]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<CatalogEditResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UploadLogoAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length is 0 or > PortalLogo.MaxBytes)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = [$"file is required: an image of at most {PortalLogo.MaxBytes / 1024} KiB."] }));
        }

        using var content = new MemoryStream((int)file.Length);
        await file.CopyToAsync(content, cancellationToken);

        var actor = new CatalogActor(Channel, HttpContext.Connection.RemoteIpAddress?.ToString(), null, User.Identity?.Name);
        var upload = await _portal.SaveLogoAsync(file.FileName, content.ToArray(), actor, cancellationToken);
        return upload.Edit.Status == CatalogEditStatus.Invalid && upload.Path is null
            ? ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = [.. upload.Edit.Errors] }))
            : this.ToActionResult(upload.Edit, CatalogSections.Portal, CatalogSections.Portal);
    }

    /// <summary>Stops using the custom logo: Portarr's own applies again. The file stays in the <c>branding</c> folder.</summary>
    /// <param name="version">The catalog version you read (the current one when omitted).</param>
    /// <param name="cancellationToken">Cancels the edit.</param>
    /// <response code="401">The API key is missing or wrong.</response>
    /// <response code="403">Catalog writes are off.</response>
    /// <response code="409">The catalog changed since the version sent.</response>
    [HttpDelete("logo")]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [CatalogWriteAccess]
    [EnableRateLimiting(HttpServiceCollectionExtensions.CatalogWritesPolicyName)]
    [ProducesResponseType<CatalogEditResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> RemoveLogoAsync([FromQuery] string? version, CancellationToken cancellationToken)
    {
        return EditAsync("remove-logo", version, CatalogEdits.SetPortal(null, string.Empty), cancellationToken);
    }

    private async Task<IActionResult> EditAsync(string operation, string? version, Func<JsonObject?, JsonObject?> change, CancellationToken cancellationToken)
    {
        var actor = new CatalogActor(Channel, HttpContext.Connection.RemoteIpAddress?.ToString(), null, User.Identity?.Name);
        var edit = new CatalogEdit(CatalogSections.Portal, CatalogSections.Portal, operation, version ?? await _editor.GetVersionAsync(cancellationToken), actor, change);
        return this.ToActionResult(await _editor.EditAsync(edit, cancellationToken), CatalogSections.Portal, CatalogSections.Portal);
    }
}
