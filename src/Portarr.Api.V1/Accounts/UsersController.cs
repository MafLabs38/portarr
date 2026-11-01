using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;
using Portarr.Http.Authentication;

namespace Portarr.Api.V1.Accounts;

/// <summary>
/// Users, for those whose role holds <c>users.manage</c> (V7, D-93). Nobody can change their own role or disable or delete
/// their own account, and the last enabled Admin cannot be demoted, disabled or deleted.
/// </summary>
[ApiController]
[Authorize(Policy = Permissions.UsersManage)]
[Authorize(Policy = AuthenticationServiceCollectionExtensions.SessionPolicyName)]
[Route("api/v1/users")]
[Produces("application/json")]
public sealed class UsersController : ControllerBase
{
    private const int MaxActivity = 200;

    private readonly IAccountService _accounts;
    private readonly IAuthAuditLog _audit;
    private readonly TimeProvider _timeProvider;

    public UsersController(IAccountService accounts, IAuthAuditLog audit, TimeProvider timeProvider)
    {
        _accounts = accounts;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    /// <summary>The users, by name.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserResource>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<UserResource>> ListAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        return (await _accounts.ListUsersAsync(cancellationToken)).Select(u => u.ToResource(now)).ToList();
    }

    /// <summary>Creates a user with a role and an initial password.</summary>
    /// <response code="201">Created.</response>
    /// <response code="400">The user name, the password or the role is not acceptable.</response>
    /// <response code="409">That user name is taken.</response>
    [HttpPost]
    [ProducesResponseType<UserResource>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync(UserCreateRequest request, CancellationToken cancellationToken)
    {
        var result = await _accounts.CreateUserAsync(Actor(), request.Username, request.DisplayName, request.Password, request.RoleId, cancellationToken);
        return result.Succeeded ? Created($"/api/v1/users/{result.Value!.Id}", result.Value.ToResource(_timeProvider.GetUtcNow())) : this.ToProblem(result);
    }

    /// <summary>Changes the display name, the role and/or whether the account is disabled (a disabled user's sessions and keys stop working).</summary>
    /// <response code="403">Your own role or state.</response>
    /// <response code="409">It would leave no enabled Admin.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType<UserResource>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAsync(int id, UserUpdateRequest request, CancellationToken cancellationToken)
    {
        var result = await _accounts.UpdateUserAsync(Actor(), id, request.DisplayName, request.RoleId, request.Disabled, cancellationToken);
        return result.Succeeded ? Ok(result.Value!.ToResource(_timeProvider.GetUtcNow())) : this.ToProblem(result);
    }

    /// <summary>Sets a new password; the user's sessions end.</summary>
    [HttpPost("{id:int}/password")]
    [ProducesResponseType<UserResource>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPasswordAsync(int id, PasswordResetRequest request, CancellationToken cancellationToken)
    {
        var result = await _accounts.ResetPasswordAsync(Actor(), id, request.Password, cancellationToken);
        return result.Succeeded ? Ok(result.Value!.ToResource(_timeProvider.GetUtcNow())) : this.ToProblem(result);
    }

    /// <summary>Deletes a user, their API keys and their sessions.</summary>
    /// <response code="403">Your own account.</response>
    /// <response code="409">It would leave no enabled Admin.</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType<UserResource>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _accounts.DeleteUserAsync(Actor(), id, cancellationToken);
        return result.Succeeded ? Ok(result.Value!.ToResource(_timeProvider.GetUtcNow())) : this.ToProblem(result);
    }

    /// <summary>The latest events of the accounts, newest first: sign-ins and failures, changes to users, roles and keys. Never a password or a key.</summary>
    /// <param name="limit">How many, 1 to 200 (default 200).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    [HttpGet("activity")]
    [ProducesResponseType<IReadOnlyList<AuthActivityResource>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AuthActivityResource>> GetActivityAsync(int limit = MaxActivity, CancellationToken cancellationToken = default)
    {
        var entries = await _audit.ReadAsync(Math.Clamp(limit, 1, MaxActivity), cancellationToken);
        return entries.Select(e => new AuthActivityResource(e.At, e.Event, e.Actor, e.Subject, e.Detail, e.Client)).ToList();
    }

    private AccountActor Actor() => User.ToActor(this.Client());
}
