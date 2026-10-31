using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;

namespace Portarr.Api.V1.Accounts;

/// <summary>User avatars (V7, D-95), for any signed-in user.</summary>
[ApiController]
[Route("api/v1/avatars")]
public sealed class AvatarsController : ControllerBase
{
    private readonly IAccountService _accounts;
    private readonly IAvatarService _avatars;

    public AvatarsController(IAccountService accounts, IAvatarService avatars)
    {
        _accounts = accounts;
        _avatars = avatars;
    }

    /// <summary>A user's avatar.</summary>
    /// <remarks>
    /// Served like the portal logo and the icons (D-47, D-91): <c>nosniff</c> and a policy that forbids everything, so
    /// opening the file directly cannot run anything. The <c>v</c> parameter of the avatar URL is the image's hash: with
    /// it the answer is cached for a year.
    /// </remarks>
    /// <param name="id">The user.</param>
    /// <param name="v">The hash from the avatar URL.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <response code="200">The image (PNG, WebP or JPEG).</response>
    /// <response code="404">No such user, or no avatar.</response>
    [HttpGet("{id:int}")]
    [Produces("image/png", "image/webp", "image/jpeg")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(int id, [FromQuery] string? v, CancellationToken cancellationToken)
    {
        var user = await _accounts.GetUserAsync(id, cancellationToken);
        if (_avatars.Find(user?.AvatarFile) is not { } avatar)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = v == avatar.Hash ? "private, max-age=31536000, immutable" : "private, max-age=300";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        Response.Headers.XContentTypeOptions = "nosniff";

        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{avatar.Hash}\"");
        return PhysicalFile(avatar.Path, avatar.ContentType, new DateTimeOffset(global::System.IO.File.GetLastWriteTimeUtc(avatar.Path)), etag);
    }
}
