using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;
using Portarr.Http.Authentication;

namespace Portarr.Api.V1.Accounts;

/// <summary>
/// Roles (V7, D-93): named sets of permissions. Admin, Operator, Observer and Guest are built in and fixed; custom roles can
/// be created, renamed, given other permissions and deleted when no user has them.
/// </summary>
[ApiController]
[Authorize(Policy = Permissions.UsersManage)]
[Authorize(Policy = AuthenticationServiceCollectionExtensions.SessionPolicyName)]
[Route("api/v1/roles")]
[Produces("application/json")]
public sealed class RolesController : ControllerBase
{
    private readonly IAccountService _accounts;

    public RolesController(IAccountService accounts)
    {
        _accounts = accounts;
    }

    /// <summary>The roles with their permissions and user counts, and the list of permissions a role is made of.</summary>
    [HttpGet]
    [ProducesResponseType<RoleListResource>(StatusCodes.Status200OK)]
    public async Task<RoleListResource> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await _accounts.ListRolesAsync(cancellationToken);
        return new RoleListResource(roles.Select(r => r.ToResource()).ToList(), Permissions.All.Select(p => new PermissionResource(p.Key, p.Area, p.Reserved)).ToList());
    }

    /// <summary>Creates a custom role.</summary>
    /// <response code="400">The name or the permissions are not acceptable.</response>
    /// <response code="409">A role with that name exists.</response>
    [HttpPost]
    [ProducesResponseType<RoleResource>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync(RoleRequest request, CancellationToken cancellationToken)
    {
        var result = await _accounts.CreateRoleAsync(Actor(), request.Name, request.Description, request.Permissions, cancellationToken);
        return result.Succeeded ? Created($"/api/v1/roles/{result.Value!.Id}", result.Value.ToResource()) : this.ToProblem(result);
    }

    /// <summary>Renames a custom role and replaces its permissions; users with it get the change at once.</summary>
    /// <response code="403">A built-in role: create a custom role to vary it.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType<RoleResource>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAsync(int id, RoleRequest request, CancellationToken cancellationToken)
    {
        var result = await _accounts.UpdateRoleAsync(Actor(), id, request.Name, request.Description, request.Permissions, cancellationToken);
        return result.Succeeded ? Ok(result.Value!.ToResource()) : this.ToProblem(result);
    }

    /// <summary>Deletes a custom role that no user has.</summary>
    /// <response code="403">A built-in role.</response>
    /// <response code="409">Users still have it.</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType<RoleResource>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _accounts.DeleteRoleAsync(Actor(), id, cancellationToken);
        return result.Succeeded ? Ok(result.Value!.ToResource()) : this.ToProblem(result);
    }

    private AccountActor Actor() => User.ToActor(this.Client());
}
