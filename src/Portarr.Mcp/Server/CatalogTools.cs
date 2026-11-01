using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Portarr.Core.Accounts;
using Portarr.Core.Catalog.Editing;
using Portarr.Core.Portal;
using Portarr.Http.Authentication;

namespace Portarr.Mcp.Server;

/// <summary>An edit's outcome: <c>applied</c> or <c>unchanged</c>, the catalog's new version (send it with the next edit) and what changed.</summary>
public sealed record CatalogWriteAnswer(string Status, string Version, IReadOnlyList<string> Changes);

/// <summary>A catalog item as the file holds it, or, without an id, the ids of the section; with the version to send back.</summary>
public sealed record CatalogItemAnswer(string Version, string Section, string? Id, JsonObject? Item, IReadOnlyList<string>? Ids);

/// <summary>The identity of the portal (D-91) with the catalog version to send back, the logos the branding folder already holds and the colors of the badge texts.</summary>
public sealed record PortalAnswer(string Version, string Name, string? Logo, IReadOnlyList<string> LogoFiles, string? LogoProblem, string NameColor, string VersionColor);

/// <summary>
/// The catalog write tools of the MCP server (D-86), registered only with <c>PORTARR__MCP__WRITETOOLS=true</c> and
/// effective only with <c>PORTARR__CATALOG__WRITABLE=true</c>. They change Portarr's description of the fleet (the
/// catalog file), never the infrastructure (D-31), through the same editor as the API: same validation, versions
/// and audit (channel <c>mcp</c>). Every write takes the version read from <c>get_catalog_item</c> or the last write.
/// </summary>
[McpServerToolType]
public sealed partial class CatalogTools
{
    private const string Channel = "mcp";

    /// <summary>
    /// Sent by the agent runner (D-89): <c>chat:&lt;conversation id&gt;</c>, kept in the audit as <c>via</c>. Informative
    /// only (any client holding the MCP key could send it); anything not shaped like an origin is ignored.
    /// </summary>
    public const string OriginHeader = "X-Portarr-Origin";

    private const string ItemShape = "The item as JSON with the keys of the catalog file (docs/catalog.md), its id included.";
    private const string VersionHelp = "The catalog version from get_catalog_item or the previous write; a stale version is refused.";

    private readonly ICatalogEditor _editor;
    private readonly IPortalService _portal;
    private readonly IHttpContextAccessor _httpContext;

    public CatalogTools(ICatalogEditor editor, IPortalService portal, IHttpContextAccessor httpContext)
    {
        _editor = editor;
        _portal = portal;
        _httpContext = httpContext;
    }

    [RequiresPermissions(Permissions.CatalogWrite)]
    [McpServerTool(Name = "get_catalog_item", Title = "Read a catalog item", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reads the catalog file before changing it: one item exactly as written (keys of the catalog file), or without an id the ids of the section; always the version to send with a write.")]
    public async Task<CatalogItemAnswer> GetCatalogItemAsync(
        [Description("environments, stacks, apps, data, links or providers.")] string section,
        [Description("The item's id; omit to list the ids of the section.")] string? id = null,
        CancellationToken cancellationToken = default)
    {
        CheckSection(section);
        if (id is null)
        {
            var version = await _editor.GetVersionAsync(cancellationToken);
            return new CatalogItemAnswer(version, section, null, null, await IdsAsync(section, cancellationToken));
        }

        var item = await _editor.GetItemAsync(section, id, cancellationToken)
            ?? throw new McpException($"No {section} item '{id}'. Ids: {string.Join(", ", await IdsAsync(section, cancellationToken))}.");
        return new CatalogItemAnswer(item.Version, section, id, item.Item, null);
    }

    [RequiresPermissions]
    [McpServerTool(Name = "get_portal", Title = "Read the portal's identity", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reads the identity shown in the sidebar: the name of the infrastructure, the logo the catalog declares (a path in the branding folder), the logo files already in that folder, the colors of the name and of the version (a hex color or a theme token name) and the version to send with set_portal.")]
    public async Task<PortalAnswer> GetPortalAsync(CancellationToken cancellationToken = default)
    {
        var identity = _portal.Get();
        return new PortalAnswer(await _editor.GetVersionAsync(cancellationToken), identity.Name, identity.Logo, _portal.ListBranding(), identity.LogoProblem, identity.Colors.Name, identity.Colors.Version);
    }

    [RequiresPermissions(Permissions.CatalogWrite)]
    [McpServerTool(Name = "set_portal", Title = "Set the portal's identity", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Sets the name of the infrastructure, the logo and/or the colors of the name and of the version shown in the sidebar. The logo must be one of the files already in the branding folder (get_portal lists them); uploading an image is done by the operator in the UI. Omit a value to keep it, send an empty one to go back to Portarr's own.")]
    public Task<CatalogWriteAnswer> SetPortalAsync(
        [Description(VersionHelp)] string version,
        [Description("The name of the infrastructure, at most 40 characters; empty for Portarr's own.")] string? name = null,
        [Description("A path from get_portal's logoFiles (branding/logo.png); empty for Portarr's own logo.")] string? logo = null,
        [Description("The color of the name: a hex color (#rgb or #rrggbb) or one of accent, accentDeep, secondary, ink, inkSoft, inkMuted; empty for the accent (Portarr's yellow).")] string? nameColor = null,
        [Description("The color of the version, same values as nameColor.")] string? versionColor = null,
        CancellationToken cancellationToken = default)
    {
        if (name is null && logo is null && nameColor is null && versionColor is null)
        {
            throw new McpException("Give a name, a logo, a color, or several.");
        }

        foreach (var (field, color) in new[] { ("nameColor", nameColor), ("versionColor", versionColor) })
        {
            if (!string.IsNullOrWhiteSpace(color) && !PortalColor.IsValid(color.Trim()))
            {
                throw new McpException(PortalColor.Validate(field, color.Trim())!);
            }
        }

        if (!string.IsNullOrWhiteSpace(logo) && !_portal.ListBranding().Contains(logo.Trim(), StringComparer.Ordinal))
        {
            throw new McpException($"logo must be one of the files in the branding folder: {string.Join(", ", _portal.ListBranding())}. The operator uploads new images in System, Status.");
        }

        return EditAsync(CatalogSections.Portal, CatalogSections.Portal, "set-portal", version, CatalogEdits.SetPortal(name, logo, nameColor, versionColor), cancellationToken);
    }

    [RequiresPermissions(Permissions.CatalogWrite)]
    [McpServerTool(Name = "upsert_app", Title = "Create or replace an app", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Creates or replaces an app of the catalog: id, name, type (web, desktop, service), icon, description, urls, workloads, check, repository, notes.")]
    public Task<CatalogWriteAnswer> UpsertAppAsync([Description(ItemShape)] JsonElement item, [Description(VersionHelp)] string version, CancellationToken cancellationToken = default) =>
        UpsertAsync(CatalogSections.Apps, item, version, cancellationToken);

    [RequiresPermissions(Permissions.CatalogWrite)]
    [McpServerTool(Name = "remove_app", Title = "Remove an app", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Removes an app from the catalog (refused while a stack, a data asset or a provider still names it).")]
    public Task<CatalogWriteAnswer> RemoveAppAsync([Description("The app id.")] string id, [Description(VersionHelp)] string version, CancellationToken cancellationToken = default) =>
        EditAsync(CatalogSections.Apps, id, "remove", version, CatalogEdits.Remove(CatalogSections.Apps, id), cancellationToken);

    [RequiresPermissions(Permissions.CatalogWrite)]
    [McpServerTool(Name = "upsert_stack", Title = "Create or replace a stack", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Creates or replaces a stack: id, name, description, members ([{ app, role: critical|optional }]), notes. Members kept in order keep their comments in the file.")]
    public Task<CatalogWriteAnswer> UpsertStackAsync([Description(ItemShape)] JsonElement item, [Description(VersionHelp)] string version, CancellationToken cancellationToken = default) =>
        UpsertAsync(CatalogSections.Stacks, item, version, cancellationToken);

    [RequiresPermissions(Permissions.CatalogWrite)]
    [McpServerTool(Name = "upsert_environment", Title = "Create or replace an environment", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Creates or replaces an environment (a host, a cluster, a hosting account): id, name, address, host (its hypervisor), connector (docker, cloudflare, kubernetes), notes, access ({ ssh: { alias, host, port, user, key (file name only), jump }, consoles: [{ label, href }], notes }) and machines ([{ name, notes, access }]). Read it first: the connector settings must be kept.")]
    public Task<CatalogWriteAnswer> UpsertEnvironmentAsync([Description(ItemShape)] JsonElement item, [Description(VersionHelp)] string version, CancellationToken cancellationToken = default) =>
        UpsertAsync(CatalogSections.Environments, item, version, cancellationToken);

    [RequiresPermissions(Permissions.CatalogWrite)]
    [McpServerTool(Name = "upsert_machine", Title = "Describe a machine", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Creates or replaces what is known about one machine of an environment (a cluster node, a VPS): notes and access. Never a password or a key: name the key file.")]
    public Task<CatalogWriteAnswer> UpsertMachineAsync(
        [Description("The environment id.")] string environment,
        [Description("The machine's host or node name.")] string name,
        [Description("{ notes, access: { ssh: { alias, host, port, user, key, jump }, consoles: [{ label, href }], notes } }")] JsonElement machine,
        [Description(VersionHelp)] string version,
        CancellationToken cancellationToken = default) =>
        EditAsync(CatalogSections.Environments, environment, "upsert-machine", version, CatalogEdits.UpsertMachine(environment, name, Object(machine, "machine")), cancellationToken);

    [RequiresPermissions(Permissions.CatalogWrite)]
    [McpServerTool(Name = "upsert_data_asset", Title = "Create or replace a data asset", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Creates or replaces a followed data asset: id, name, app, kind, where ({ environment, namespace, workload, claim, path }), importance (critical, important, comfort), maxAge, targets ({ local: {…}, offsite: {…} } with backrest, k8up, cronjob or manual sources).")]
    public Task<CatalogWriteAnswer> UpsertDataAssetAsync([Description(ItemShape)] JsonElement item, [Description(VersionHelp)] string version, CancellationToken cancellationToken = default) =>
        UpsertAsync(CatalogSections.Data, item, version, cancellationToken);

    [RequiresPermissions(Permissions.CatalogWrite)]
    [McpServerTool(Name = "add_link", Title = "Add a network link", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Adds a network link between two environments or machines: id, from, to (environment ids, machine names or environment/machine), kind (wireguard-tunnel, vpn, lan, peering, ssh-tunnel, other), name, network (CIDR), notes (e.g. which side initiates, the port).")]
    public Task<CatalogWriteAnswer> AddLinkAsync([Description(ItemShape)] JsonElement item, [Description(VersionHelp)] string version, CancellationToken cancellationToken = default)
    {
        var value = Object(item, "item");
        var id = IdOf(value);
        return EditAsync(CatalogSections.Links, id, "add", version, CatalogEdits.Add(CatalogSections.Links, id, value), cancellationToken);
    }

    [RequiresPermissions(Permissions.CatalogWrite)]
    [McpServerTool(Name = "upsert_provider", Title = "Create or replace a provider", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Creates or replaces an external provider or account: id, name, kind (cdn, paas, dns, registrar, cloud, vps, storage, backup, email, git, registry, monitoring, other), console (URL), account (a name or e-mail hint, never a credential), usedBy (app and environment ids), notes.")]
    public Task<CatalogWriteAnswer> UpsertProviderAsync([Description(ItemShape)] JsonElement item, [Description(VersionHelp)] string version, CancellationToken cancellationToken = default) =>
        UpsertAsync(CatalogSections.Providers, item, version, cancellationToken);

    [RequiresPermissions(Permissions.CatalogWrite)]
    [McpServerTool(Name = "remove_catalog_item", Title = "Remove a catalog item", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Removes a stack, environment, data asset, link or provider (refused while something else names it).")]
    public Task<CatalogWriteAnswer> RemoveItemAsync(
        [Description("environments, stacks, apps, data, links or providers.")] string section,
        [Description("The item's id.")] string id,
        [Description(VersionHelp)] string version,
        CancellationToken cancellationToken = default)
    {
        CheckSection(section);
        if (section == CatalogSections.Apps)
        {
            throw new McpException("Use remove_app to remove an app.");
        }

        return EditAsync(section, id, "remove", version, CatalogEdits.Remove(section, id), cancellationToken);
    }

    [RequiresPermissions(Permissions.CatalogWrite)]
    [McpServerTool(Name = "set_notes", Title = "Set notes", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Sets the free Markdown notes of an environment, a machine of an environment, a stack, an app, a link or a provider; empty clears them. Notes are for people and assistants: what it is for, quirks, how it is wired. Never secrets.")]
    public Task<CatalogWriteAnswer> SetNotesAsync(
        [Description("environments, stacks, apps, links or providers.")] string section,
        [Description("The item's id.")] string id,
        [Description("Markdown, at most 8000 characters; empty to clear.")] string notes,
        [Description(VersionHelp)] string version,
        [Description("For an environment: the machine the notes are about (created when absent).")] string? machine = null,
        CancellationToken cancellationToken = default)
    {
        if (!CatalogSections.WithNotes.Contains(section))
        {
            throw new McpException($"section must be one of {string.Join(", ", CatalogSections.WithNotes)}.");
        }

        return EditAsync(section, id, "set-notes", version, CatalogEdits.SetNotes(section, id, notes, machine), cancellationToken);
    }

    /// <summary>The origin an MCP client declares, when it looks like one (<c>chat</c> or <c>chat:&lt;id&gt;</c>); otherwise null.</summary>
    public static string? OriginOf(string? header)
    {
        return header is { Length: > 0 and <= 80 } && OriginPattern().IsMatch(header) ? header : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z][a-z-]{0,15}(:[A-Za-z0-9-]{1,64})?$")]
    private static partial System.Text.RegularExpressions.Regex OriginPattern();

    private static void CheckSection(string section)
    {
        if (!CatalogSections.All.Contains(section))
        {
            throw new McpException($"section must be one of {string.Join(", ", CatalogSections.All)}.");
        }
    }

    private static JsonObject Object(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            ? JsonObject.Create(element)!
            : throw new McpException($"{name} must be a JSON object.");
    }

    private static string IdOf(JsonObject item)
    {
        return item["id"]?.ToString() is { Length: > 0 } id ? id : throw new McpException("item.id is required: lowercase letters, digits and dashes.");
    }

    private Task<CatalogWriteAnswer> UpsertAsync(string section, JsonElement item, string version, CancellationToken cancellationToken)
    {
        var value = Object(item, "item");
        var id = IdOf(value);
        return EditAsync(section, id, "upsert", version, CatalogEdits.Upsert(id, value), cancellationToken);
    }

    private Task<IReadOnlyList<string>> IdsAsync(string section, CancellationToken cancellationToken) => _editor.GetIdsAsync(section, cancellationToken);

    private async Task<CatalogWriteAnswer> EditAsync(string section, string id, string operation, string version, Func<JsonObject?, JsonObject?> change, CancellationToken cancellationToken)
    {
        var context = _httpContext.HttpContext;

        // A chat token names its conversation: that is the truth. The header is only a hint from other clients.
        var via = context?.User.FindFirst(PortarrClaims.Conversation)?.Value is { } conversation ? $"chat:{conversation}" : OriginOf(context?.Request.Headers[OriginHeader].ToString());
        var actor = new CatalogActor(Channel, context?.Connection.RemoteIpAddress?.ToString(), via, context?.User.Identity?.Name);
        var result = await _editor.EditAsync(new CatalogEdit(section, id, operation, version, actor, change), cancellationToken);
        if (!result.Succeeded)
        {
            throw new McpException($"{result.Status}: {string.Join(" ", result.Errors)} (catalog version {result.Version})");
        }

        return new CatalogWriteAnswer(result.Status.ToString().ToLowerInvariant(), result.Version, result.Changes);
    }
}
