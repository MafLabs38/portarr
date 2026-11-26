using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Portarr.Core.Accounts;
using Portarr.Core.Catalog.Editing;
using Portarr.Http;

namespace Portarr.Api.V1.Catalog;

/// <summary>
/// Edits the catalog file (D-83 to D-85): the file stays the source of truth (D-44), these endpoints change it for
/// the operator or an assistant. Every write sends the catalog version it read (409 when the file changed since),
/// is validated on the whole resulting file (400 with field paths, file untouched), keeps the previous version and
/// is audited. Writes are off unless <c>PORTARR__CATALOG__WRITABLE=true</c> and need the key of
/// <c>PORTARR__APIKEY</c>; they change Portarr's description of the fleet, never the infrastructure (D-31).
/// </summary>
[SuppressMessage("Major Code Smell", "S6960:This controller has multiple responsibilities and could be split into 2 smaller controllers.", Justification = "One REST resource, the catalog: its items, their notes, the machines of an environment and the audit of these edits are the same route family and the same permission; two controllers on api/v1/catalog would only share a route prefix.")]
[ApiController]
[Route("api/v1/catalog")]
[RequestSizeLimit(MaxBodyBytes)]
[Produces("application/json")]
public sealed class CatalogEditController : ControllerBase
{
    private const string Section = "{section:" + CatalogSectionConstraint.Name + "}";

    /// <summary>An item is a few KiB of YAML as JSON; every write re-parses the whole catalog under a lock, so a body beyond this is refused before it is read.</summary>
    private const int MaxBodyBytes = 256 * 1024;
    private const string Channel = "api";
    private const int MaxAuditEntries = 200;

    private readonly ICatalogEditor _editor;
    private readonly ICatalogAuditLog _auditLog;
    private readonly IValidator<CatalogItemRequest> _itemValidator;
    private readonly IValidator<CatalogNotesRequest> _notesValidator;
    private readonly IAuthorizationService _authorization;

    public CatalogEditController(
        ICatalogEditor editor,
        ICatalogAuditLog auditLog,
        IValidator<CatalogItemRequest> itemValidator,
        IValidator<CatalogNotesRequest> notesValidator,
        IAuthorizationService authorization)
    {
        _authorization = authorization;
        _editor = editor;
        _auditLog = auditLog;
        _itemValidator = itemValidator;
        _notesValidator = notesValidator;
    }

    /// <summary>One item of the catalog as the file holds it, with the version to send back when changing it.</summary>
    /// <param name="section">environments, stacks, apps, data, links, providers, mcpServers or workstations.</param>
    /// <param name="id">The item's id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <response code="404">No such item.</response>
    [HttpGet(Section + "/{id}")]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [ProducesResponseType<CatalogItemResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogItemResource>> GetItemAsync(string section, string id, CancellationToken cancellationToken)
    {
        var item = await _editor.GetItemAsync(section, id, await MayReconfigureAsync(), cancellationToken);
        return item is null
            ? Problem(title: "Not found", detail: $"No {section} item '{id}' in the catalog.", statusCode: StatusCodes.Status404NotFound)
            : new CatalogItemResource(item.Version, item.Section, item.Id, item.Item);
    }

    /// <summary>The items of a section as the file holds them, with the version to send back when changing one. How hosts are reached (<c>docker</c>, <c>kubernetes</c>…) is left out unless the caller may reconfigure (<c>settings.manage</c>).</summary>
    /// <param name="section">environments, stacks, apps, data, links, providers, mcpServers or workstations.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    [HttpGet(Section)]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [ProducesResponseType<CatalogSectionResource>(StatusCodes.Status200OK)]
    public async Task<CatalogSectionResource> GetSectionAsync(string section, CancellationToken cancellationToken)
    {
        var (version, items) = await _editor.GetItemsAsync(section, await MayReconfigureAsync(), cancellationToken);
        return new CatalogSectionResource(version, section, items);
    }

    /// <summary>Checks an item as a write would, without writing anything: <c>valid</c> or the errors by field path.</summary>
    /// <param name="section">environments, stacks, apps, data, links, providers, mcpServers or workstations.</param>
    /// <param name="id">The item's id; the item's own id may be omitted.</param>
    /// <param name="request">The version read and the whole item.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <response code="400">The item, or the catalog it would produce, is not valid: errors by field path.</response>
    /// <response code="409">The catalog changed since the version sent.</response>
    [HttpPost(Section + "/{id}/validate")]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [EnableRateLimiting(HttpServiceCollectionExtensions.CatalogWritesPolicyName)]
    [ProducesResponseType<CatalogEditResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ValidateItemAsync(string section, string id, CatalogItemRequest request, CancellationToken cancellationToken)
    {
        return await ValidateAsync(_itemValidator, request, cancellationToken)
            ?? await EditAsync(section, id, "validate", request.Version, CatalogEdits.Upsert(id, request.Item!), cancellationToken, dryRun: true);
    }

    /// <summary>The catalog file as it is, to keep a copy or commit it back to a repository. Needs <c>settings.manage</c>.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    [HttpGet("export")]
    [Authorize(Policy = Permissions.SettingsManage)]
    [Produces("application/yaml")]
    [ProducesResponseType<string>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(CancellationToken cancellationToken)
    {
        var (version, text) = await _editor.ExportAsync(cancellationToken);
        Response.Headers.ETag = $"\"{version}\"";
        return Content(text, "application/yaml; charset=utf-8");
    }

    /// <summary>The latest catalog edits, newest first: when, through which channel, what, and the versions before and after.</summary>
    /// <param name="limit">How many entries, 1 to 200 (default 20).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    [HttpGet("audit")]
    [Authorize(Policy = Permissions.AssistantView)]
    [ProducesResponseType<IReadOnlyList<CatalogAuditEntryResource>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CatalogAuditEntryResource>> GetAuditAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        var entries = await _auditLog.ReadAsync(Math.Clamp(limit, 1, MaxAuditEntries), cancellationToken);
        return entries.Select(e => e.ToResource()).ToList();
    }

    /// <summary>Adds an item to a section of the catalog.</summary>
    /// <param name="section">environments, stacks, apps, data, links, providers, mcpServers or workstations.</param>
    /// <param name="request">The version read and the item, with its id.</param>
    /// <param name="cancellationToken">Cancels the edit.</param>
    /// <response code="201">Added; the body gives the catalog's new version.</response>
    /// <response code="400">The item, or the catalog it would produce, is not valid: errors by field path.</response>
    /// <response code="401">The API key is missing or wrong.</response>
    /// <response code="403">Catalog writes are off.</response>
    /// <response code="409">The catalog changed since the version sent, or the id exists.</response>
    [HttpPost(Section)]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [CatalogWriteAccess]
    [EnableRateLimiting(HttpServiceCollectionExtensions.CatalogWritesPolicyName)]
    [ProducesResponseType<CatalogEditResource>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddAsync(string section, CatalogItemRequest request, CancellationToken cancellationToken)
    {
        if (await ValidateAsync(_itemValidator, request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (request.Item!["id"]?.ToString() is not { Length: > 0 } id)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["item.id"] = ["id is required: lowercase letters, digits and dashes."] }));
        }

        var result = await EditAsync(section, id, "add", request.Version, CatalogEdits.Add(section, id, request.Item), cancellationToken);
        return result is OkObjectResult ok ? Created($"/api/v1/catalog/{section}/{id}", ok.Value) : result;
    }

    /// <summary>Creates or replaces an item; only what changed is rewritten in the file, comments elsewhere are kept.</summary>
    /// <param name="section">environments, stacks, apps, data, links, providers, mcpServers or workstations.</param>
    /// <param name="id">The item's id; the item's own id may be omitted.</param>
    /// <param name="request">The version read and the whole item.</param>
    /// <param name="cancellationToken">Cancels the edit.</param>
    /// <response code="400">The item, or the catalog it would produce, is not valid: errors by field path.</response>
    /// <response code="401">The API key is missing or wrong.</response>
    /// <response code="403">Catalog writes are off.</response>
    /// <response code="409">The catalog changed since the version sent.</response>
    [HttpPut(Section + "/{id}")]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [CatalogWriteAccess]
    [EnableRateLimiting(HttpServiceCollectionExtensions.CatalogWritesPolicyName)]
    [ProducesResponseType<CatalogEditResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpsertAsync(string section, string id, CatalogItemRequest request, CancellationToken cancellationToken)
    {
        return await ValidateAsync(_itemValidator, request, cancellationToken)
            ?? await EditAsync(section, id, "upsert", request.Version, CatalogEdits.Upsert(id, request.Item!), cancellationToken);
    }

    /// <summary>Removes an item.</summary>
    /// <param name="section">environments, stacks, apps, data, links, providers, mcpServers or workstations.</param>
    /// <param name="id">The item's id.</param>
    /// <param name="version">The catalog version you read.</param>
    /// <param name="cancellationToken">Cancels the edit.</param>
    /// <response code="400">The catalog would not be valid without it (e.g. an app still uses this environment).</response>
    /// <response code="401">The API key is missing or wrong.</response>
    /// <response code="403">Catalog writes are off.</response>
    /// <response code="404">No such item.</response>
    /// <response code="409">The catalog changed since the version sent.</response>
    [HttpDelete(Section + "/{id}")]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [CatalogWriteAccess]
    [EnableRateLimiting(HttpServiceCollectionExtensions.CatalogWritesPolicyName)]
    [ProducesResponseType<CatalogEditResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> RemoveAsync(string section, string id, [FromQuery] string? version, CancellationToken cancellationToken)
    {
        return EditAsync(section, id, "remove", version, CatalogEdits.Remove(section, id), cancellationToken);
    }

    /// <summary>Sets the notes (Markdown) of an environment, stack, app, link or provider; empty clears them.</summary>
    /// <param name="section">environments, stacks, apps, links or providers.</param>
    /// <param name="id">The item's id.</param>
    /// <param name="request">The version read and the notes.</param>
    /// <param name="cancellationToken">Cancels the edit.</param>
    /// <response code="400">The notes are too long or look like they hold a secret.</response>
    /// <response code="404">No such item.</response>
    /// <response code="409">The catalog changed since the version sent.</response>
    [HttpPut(Section + "/{id}/notes")]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [CatalogWriteAccess]
    [EnableRateLimiting(HttpServiceCollectionExtensions.CatalogWritesPolicyName)]
    [ProducesResponseType<CatalogEditResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetNotesAsync(string section, string id, CatalogNotesRequest request, CancellationToken cancellationToken)
    {
        if (!CatalogSections.WithNotes.Contains(section))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["section"] = [$"Only {string.Join(", ", CatalogSections.WithNotes)} have notes."] }));
        }

        return await ValidateAsync(_notesValidator, request, cancellationToken)
            ?? await EditAsync(section, id, "set-notes", request.Version, CatalogEdits.SetNotes(section, id, request.Notes), cancellationToken);
    }

    /// <summary>Creates or replaces what is known about one machine of an environment (a cluster node, a VPS): notes and access.</summary>
    /// <param name="id">The environment's id.</param>
    /// <param name="name">The machine's name (its host or node name).</param>
    /// <param name="request">The version read and the machine: <c>notes</c>, <c>access</c>.</param>
    /// <param name="cancellationToken">Cancels the edit.</param>
    /// <response code="400">Not valid: errors by field path.</response>
    /// <response code="404">No such environment.</response>
    /// <response code="409">The catalog changed since the version sent.</response>
    [HttpPut("environments/{id}/machines/{name}")]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [CatalogWriteAccess]
    [EnableRateLimiting(HttpServiceCollectionExtensions.CatalogWritesPolicyName)]
    [ProducesResponseType<CatalogEditResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpsertMachineAsync(string id, string name, CatalogItemRequest request, CancellationToken cancellationToken)
    {
        return await ValidateAsync(_itemValidator, request, cancellationToken)
            ?? await EditAsync(CatalogSections.Environments, id, "upsert-machine", request.Version, CatalogEdits.UpsertMachine(id, name, request.Item!), cancellationToken);
    }

    /// <summary>Forgets one machine of an environment.</summary>
    /// <param name="id">The environment's id.</param>
    /// <param name="name">The machine's name.</param>
    /// <param name="version">The catalog version you read.</param>
    /// <param name="cancellationToken">Cancels the edit.</param>
    /// <response code="404">No such environment or machine.</response>
    /// <response code="409">The catalog changed since the version sent.</response>
    [HttpDelete("environments/{id}/machines/{name}")]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [CatalogWriteAccess]
    [EnableRateLimiting(HttpServiceCollectionExtensions.CatalogWritesPolicyName)]
    [ProducesResponseType<CatalogEditResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> RemoveMachineAsync(string id, string name, [FromQuery] string? version, CancellationToken cancellationToken)
    {
        return EditAsync(CatalogSections.Environments, id, "remove-machine", version, CatalogEdits.RemoveMachine(id, name), cancellationToken);
    }

    private async Task<IActionResult?> ValidateAsync<T>(IValidator<T> validator, T request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        return validation.IsValid ? null : ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
    }

    private async Task<IActionResult> EditAsync(string section, string id, string operation, string? version, Func<JsonObject?, JsonObject?> change, CancellationToken cancellationToken, bool dryRun = false)
    {
        var actor = new CatalogActor(Channel, HttpContext.Connection.RemoteIpAddress?.ToString(), null, User.Identity?.Name, await MayReconfigureAsync());
        var result = await _editor.EditAsync(new CatalogEdit(section, id, operation, version, actor, change, dryRun), cancellationToken);
        return this.ToActionResult(result, section, id);
    }

    /// <summary>A role holding <c>settings.manage</c> (a person or the key of one) reads and changes how a host is reached; any other is refused and reads the items without it (D-134).</summary>
    private async Task<bool> MayReconfigureAsync() => (await _authorization.AuthorizeAsync(User, Permissions.SettingsManage)).Succeeded;
}
