using System.Text.Json.Nodes;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Portarr.Core.Catalog.Editing;
using Portarr.Http;

namespace Portarr.Api.V1.Catalog;

/// <summary>
/// Edits the catalog file (D-83 to D-85): the file stays the source of truth (D-44), these endpoints change it for
/// the operator or an assistant. Every write sends the catalog version it read (409 when the file changed since),
/// is validated on the whole resulting file (400 with field paths, file untouched), keeps the previous version and
/// is audited. Writes are off unless <c>PORTARR__CATALOG__WRITABLE=true</c> and need the key of
/// <c>PORTARR__APIKEY</c>; they change Portarr's description of the fleet, never the infrastructure (D-31). Reading
/// one item and the audit log need the same key: they show the connectors' addresses and who changed what.
/// </summary>
[ApiController]
[Route("api/v1/catalog")]
[Produces("application/json")]
public sealed class CatalogEditController : ControllerBase
{
    private const string Section = "{section:regex(^(environments|stacks|apps|data|links|providers)$)}";
    private const string Channel = "api";
    private const int MaxAuditEntries = 200;

    private readonly ICatalogEditor _editor;
    private readonly ICatalogAuditLog _auditLog;
    private readonly IValidator<CatalogItemRequest> _itemValidator;
    private readonly IValidator<CatalogNotesRequest> _notesValidator;

    public CatalogEditController(
        ICatalogEditor editor,
        ICatalogAuditLog auditLog,
        IValidator<CatalogItemRequest> itemValidator,
        IValidator<CatalogNotesRequest> notesValidator)
    {
        _editor = editor;
        _auditLog = auditLog;
        _itemValidator = itemValidator;
        _notesValidator = notesValidator;
    }

    /// <summary>One item of the catalog as the file holds it, with the version to send back when changing it.</summary>
    /// <param name="section">environments, stacks, apps, data, links or providers.</param>
    /// <param name="id">The item's id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <response code="401">The API key is missing or wrong.</response>
    /// <response code="404">No such item.</response>
    [HttpGet(Section + "/{id}")]
    [CatalogWriteAccess(RequireWritable = false)]
    [ProducesResponseType<CatalogItemResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogItemResource>> GetItemAsync(string section, string id, CancellationToken cancellationToken)
    {
        var item = await _editor.GetItemAsync(section, id, cancellationToken);
        return item is null
            ? Problem(title: "Not found", detail: $"No {section} item '{id}' in the catalog.", statusCode: StatusCodes.Status404NotFound)
            : new CatalogItemResource(item.Version, item.Section, item.Id, item.Item);
    }

    /// <summary>The latest catalog edits, newest first: when, through which channel, what, and the versions before and after.</summary>
    /// <param name="limit">How many entries, 1 to 200 (default 20).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <response code="401">The API key is missing or wrong.</response>
    [HttpGet("audit")]
    [CatalogWriteAccess(RequireWritable = false)]
    [ProducesResponseType<IReadOnlyList<CatalogAuditEntryResource>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IReadOnlyList<CatalogAuditEntryResource>> GetAuditAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        var entries = await _auditLog.ReadAsync(Math.Clamp(limit, 1, MaxAuditEntries), cancellationToken);
        return entries.Select(e => e.ToResource()).ToList();
    }

    /// <summary>Adds an item to a section of the catalog.</summary>
    /// <param name="section">environments, stacks, apps, data, links or providers.</param>
    /// <param name="request">The version read and the item, with its id.</param>
    /// <param name="cancellationToken">Cancels the edit.</param>
    /// <response code="201">Added; the body gives the catalog's new version.</response>
    /// <response code="400">The item, or the catalog it would produce, is not valid: errors by field path.</response>
    /// <response code="401">The API key is missing or wrong.</response>
    /// <response code="403">Catalog writes are off.</response>
    /// <response code="409">The catalog changed since the version sent, or the id exists.</response>
    [HttpPost(Section)]
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
    /// <param name="section">environments, stacks, apps, data, links or providers.</param>
    /// <param name="id">The item's id; the item's own id may be omitted.</param>
    /// <param name="request">The version read and the whole item.</param>
    /// <param name="cancellationToken">Cancels the edit.</param>
    /// <response code="400">The item, or the catalog it would produce, is not valid: errors by field path.</response>
    /// <response code="401">The API key is missing or wrong.</response>
    /// <response code="403">Catalog writes are off.</response>
    /// <response code="409">The catalog changed since the version sent.</response>
    [HttpPut(Section + "/{id}")]
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
    /// <param name="section">environments, stacks, apps, data, links or providers.</param>
    /// <param name="id">The item's id.</param>
    /// <param name="version">The catalog version you read.</param>
    /// <param name="cancellationToken">Cancels the edit.</param>
    /// <response code="400">The catalog would not be valid without it (e.g. an app still uses this environment).</response>
    /// <response code="401">The API key is missing or wrong.</response>
    /// <response code="403">Catalog writes are off.</response>
    /// <response code="404">No such item.</response>
    /// <response code="409">The catalog changed since the version sent.</response>
    [HttpDelete(Section + "/{id}")]
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

    private async Task<IActionResult> EditAsync(string section, string id, string operation, string? version, Func<JsonObject?, JsonObject?> change, CancellationToken cancellationToken)
    {
        var actor = new CatalogActor(Channel, HttpContext.Connection.RemoteIpAddress?.ToString());
        var result = await _editor.EditAsync(new CatalogEdit(section, id, operation, version, actor, change), cancellationToken);
        return this.ToActionResult(result, section, id);
    }
}
