using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Portarr.Api.V1.Portal;
using Portarr.Core.Accounts;
using Portarr.Core.Catalog.Editing;
using Portarr.Core.Portal;
using Portarr.Http;
using Portarr.Http.Authentication;

namespace Portarr.Api.V1.Accounts;

/// <summary>
/// Sign-in and the signed-in user's own account (V7, D-92): the first run, the cookie session, the profile (display name,
/// password, avatar) and the user's API keys. Everything but <c>status</c>, <c>setup</c>, <c>login</c> and <c>logout</c>
/// needs a session; changing the account or the keys needs a browser session, not an API key.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private const long MaxAvatarRequestBytes = AvatarService.MaxBytes + 64 * 1024;

    private readonly IAccountService _accounts;
    private readonly ISetupCode _setupCode;
    private readonly IApiKeyService _apiKeys;
    private readonly IAvatarService _avatars;
    private readonly IAuthAuditLog _audit;
    private readonly IPortalService _portal;
    private readonly TimeProvider _timeProvider;

    public AuthController(IAccountService accounts, ISetupCode setupCode, IApiKeyService apiKeys, IAvatarService avatars, IAuthAuditLog audit, IPortalService portal, TimeProvider timeProvider)
    {
        _accounts = accounts;
        _setupCode = setupCode;
        _apiKeys = apiKeys;
        _avatars = avatars;
        _audit = audit;
        _portal = portal;
        _timeProvider = timeProvider;
    }

    /// <summary>Whether the first administrator still has to be created, and the portal's name and logo for the login page.</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    [ProducesResponseType<AuthStatusResource>(StatusCodes.Status200OK)]
    public async Task<AuthStatusResource> GetStatusAsync(CancellationToken cancellationToken)
    {
        var identity = _portal.Get();
        var setupRequired = await _accounts.SetupRequiredAsync(cancellationToken);
        return new AuthStatusResource(
            setupRequired,
            identity.Name,
            identity.LogoFile is null ? null : $"{PortalResourceMapper.LogoPath}?v={identity.LogoFile.Hash}",
            setupRequired && _setupCode.Required);
    }

    /// <summary>Creates the first administrator and signs in. Accepted only while no user exists; once one does, always 409.</summary>
    /// <response code="201">Created and signed in.</response>
    /// <response code="400">The user name or the password is not acceptable.</response>
    /// <response code="403">A setup code is required and the one sent is not right (it is printed in the Portarr log).</response>
    /// <response code="409">Portarr is already set up.</response>
    [HttpPost("setup")]
    [AllowAnonymous]
    [EnableRateLimiting(HttpServiceCollectionExtensions.LoginPolicyName)]
    [ProducesResponseType<MeResource>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetupAsync(SetupRequest request, CancellationToken cancellationToken)
    {
        if (_setupCode.Required && !_setupCode.Matches(request.SetupCode))
        {
            await _audit.AppendAsync("setup.failure", request.Username, null, "setup code refused", this.Client(), cancellationToken);
            return Problem(title: "Setup code required", detail: "Enter the setup code printed in the Portarr log (also in the setup-code file of the data folder).", statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await _accounts.SetupAsync(request.Username, request.DisplayName, request.Password, this.Client(), cancellationToken);
        if (!result.Succeeded)
        {
            return this.ToProblem(result);
        }

        _setupCode.Clear();
        await SignInAsync(result.Value!, persistent: true);
        return Created("/api/v1/auth/me", result.Value!.ToMe(AuthMethods.Cookie));
    }

    /// <summary>Signs in with a user name and a password; the answer is one generic message whatever the reason it fails.</summary>
    /// <response code="200">Signed in: the session cookie is set (a browser-session cookie, or a persistent one with <c>remember</c>).</response>
    /// <response code="401">Invalid user name or password, or the account is locked (five failures lock it for five minutes).</response>
    /// <response code="429">Too many attempts from this address.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(HttpServiceCollectionExtensions.LoginPolicyName)]
    [ProducesResponseType<MeResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _accounts.LoginAsync(request.Username, request.Password, this.Client(), cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(title: "Sign-in failed", detail: result.Message, statusCode: StatusCodes.Status401Unauthorized);
        }

        await SignInAsync(result.Value!, request.Remember);
        return Ok(result.Value!.ToMe(AuthMethods.Cookie));
    }

    /// <summary>Ends the browser session.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true && User.AuthMethod() == AuthMethods.Cookie)
        {
            await _audit.AppendAsync("logout", User.Identity.Name, User.Identity.Name, null, this.Client(), cancellationToken);
        }

        await HttpContext.SignOutAsync(AuthenticationServiceCollectionExtensions.CookieScheme);
        return NoContent();
    }

    /// <summary>The signed-in user: account, role and permissions.</summary>
    [HttpGet("me")]
    [ProducesResponseType<MeResource>(StatusCodes.Status200OK)]
    public MeResource GetMe() => User.ToMe();

    /// <summary>Changes the display name.</summary>
    [HttpPut("profile")]
    [Authorize(Policy = AuthenticationServiceCollectionExtensions.SessionPolicyName)]
    [ProducesResponseType<MeResource>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfileAsync(ProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await _accounts.UpdateProfileAsync(Actor(), request.DisplayName, cancellationToken);
        return result.Succeeded ? Ok((await Identity(cancellationToken))!.ToMe(AuthMethods.Cookie)) : this.ToProblem(result);
    }

    /// <summary>Changes the password. The current one is required; other sessions are signed out, this one continues.</summary>
    /// <response code="204">Changed.</response>
    /// <response code="400">The new password is not acceptable.</response>
    /// <response code="403">The current password is not right.</response>
    [HttpPut("password")]
    [Authorize(Policy = AuthenticationServiceCollectionExtensions.SessionPolicyName)]
    [EnableRateLimiting(HttpServiceCollectionExtensions.LoginPolicyName)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePasswordAsync(PasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _accounts.ChangePasswordAsync(Actor(), request.CurrentPassword, request.NewPassword, cancellationToken);
        if (!result.Succeeded)
        {
            return this.ToProblem(result);
        }

        var current = await HttpContext.AuthenticateAsync(AuthenticationServiceCollectionExtensions.CookieScheme);
        await SignInAsync(result.Value!, current.Properties?.IsPersistent == true);
        return NoContent();
    }

    /// <summary>Uploads the avatar (multipart form, field <c>file</c>): a png, webp or jpeg of at most 1 MiB, checked by its content. An svg is refused.</summary>
    /// <response code="400">Not an image of an accepted format, or too large.</response>
    [HttpPut("avatar")]
    [Authorize(Policy = AuthenticationServiceCollectionExtensions.SessionPolicyName)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxAvatarRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAvatarRequestBytes)]
    [ProducesResponseType<MeResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAvatarAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length is 0 or > AvatarService.MaxBytes)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = [$"file is required: an image of at most {AvatarService.MaxBytes / 1024} KiB."] }));
        }

        using var content = new MemoryStream((int)file.Length);
        await file.CopyToAsync(content, cancellationToken);
        var result = await _avatars.SaveAsync(Actor(), User.UserId() ?? -1, content.ToArray(), cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error == AccountError.Invalid
                ? ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = [result.Message!] }))
                : this.ToProblem(result);
        }

        return Ok((await Identity(cancellationToken))!.ToMe(AuthMethods.Cookie));
    }

    /// <summary>Removes the avatar: the initials show again.</summary>
    [HttpDelete("avatar")]
    [Authorize(Policy = AuthenticationServiceCollectionExtensions.SessionPolicyName)]
    [ProducesResponseType<MeResource>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveAvatarAsync(CancellationToken cancellationToken)
    {
        var result = await _avatars.RemoveAsync(Actor(), User.UserId() ?? -1, cancellationToken);
        return result.Succeeded ? Ok((await Identity(cancellationToken))!.ToMe(AuthMethods.Cookie)) : this.ToProblem(result);
    }

    /// <summary>The signed-in user's API keys, newest first (prefix, scope, dates; never the key).</summary>
    [HttpGet("keys")]
    [Authorize(Policy = AuthenticationServiceCollectionExtensions.SessionPolicyName)]
    [ProducesResponseType<IReadOnlyList<ApiKeyResource>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ApiKeyResource>> ListKeysAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        return (await _apiKeys.ListAsync(User.UserId() ?? -1, cancellationToken)).Select(k => k.ToResource(now)).ToList();
    }

    /// <summary>
    /// Creates a named key for the signed-in user: <c>read</c> (the view permissions of the role) or <c>full</c> (all of its
    /// permissions), optionally expiring. The key is in the answer this once; only its hash is stored. A key never exceeds
    /// its owner's role and stops working when the owner is disabled.
    /// </summary>
    /// <response code="201">Created: <c>value</c> is the key.</response>
    /// <response code="409">Too many active keys.</response>
    [HttpPost("keys")]
    [Authorize(Policy = AuthenticationServiceCollectionExtensions.SessionPolicyName)]
    [ProducesResponseType<CreatedApiKeyResource>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateKeyAsync(ApiKeyRequest request, CancellationToken cancellationToken)
    {
        var result = await _apiKeys.CreateAsync(Actor(), request.Name, request.Scope, request.Expires, cancellationToken);
        return result.Succeeded
            ? Created($"/api/v1/auth/keys/{result.Value!.Info.Id}", new CreatedApiKeyResource(result.Value.Info.ToResource(_timeProvider.GetUtcNow()), result.Value.Key))
            : this.ToProblem(result);
    }

    /// <summary>Revokes one of the signed-in user's keys; it stops working at once.</summary>
    [HttpDelete("keys/{id:int}")]
    [Authorize(Policy = AuthenticationServiceCollectionExtensions.SessionPolicyName)]
    [ProducesResponseType<ApiKeyResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeKeyAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _apiKeys.RevokeAsync(Actor(), id, cancellationToken);
        return result.Succeeded ? Ok(result.Value!.ToResource(_timeProvider.GetUtcNow())) : this.ToProblem(result);
    }

    private AccountActor Actor() => User.ToActor(this.Client());

    private Task<AccountIdentity?> Identity(CancellationToken cancellationToken) => _accounts.ResolveAsync(User.UserId() ?? -1, null, cancellationToken);

    private Task SignInAsync(AccountIdentity identity, bool persistent) =>
        HttpContext.SignInAsync(
            AuthenticationServiceCollectionExtensions.CookieScheme,
            PortarrClaims.CreateSessionPrincipal(identity, AuthenticationServiceCollectionExtensions.CookieScheme),
            new AuthenticationProperties { IsPersistent = persistent });
}
