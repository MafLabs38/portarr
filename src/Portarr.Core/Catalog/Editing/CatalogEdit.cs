using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Portarr.Core.Catalog.Editing;

/// <summary>The top-level lists of the catalog the editor changes; every item has an <c>id</c>.</summary>
public static class CatalogSections
{
    public const string Environments = "environments";
    public const string Stacks = "stacks";
    public const string Apps = "apps";
    public const string Data = "data";
    public const string Links = "links";
    public const string Providers = "providers";

    /// <summary>The identity of the portal (D-91): one mapping, not a list, edited through the same editor (id <c>portal</c>).</summary>
    public const string Portal = "portal";

    public static readonly IReadOnlyList<string> All = [Environments, Stacks, Apps, Data, Links, Providers];

    /// <summary>The sections that carry <c>notes</c>.</summary>
    public static readonly IReadOnlyList<string> WithNotes = [Environments, Stacks, Apps, Links, Providers];
}

public enum CatalogEditStatus
{
    Applied,

    /// <summary>The item already reads like that: nothing written.</summary>
    Unchanged,

    /// <summary>Catalog writes are off (<c>PORTARR__CATALOG__WRITABLE</c>).</summary>
    ReadOnly,

    /// <summary>The file changed since the caller read it: read it again.</summary>
    Conflict,

    NotFound,
    AlreadyExists,

    /// <summary>The edit, or the catalog it would produce, is not valid.</summary>
    Invalid,
}

/// <summary>
/// Who asked for an edit: the channel (<c>api</c>, <c>mcp</c>, <c>console</c>), the client's address and, for an MCP
/// client that says so, where the request came from (<c>chat:&lt;conversation&gt;</c> for the built-in chat, D-89).
/// </summary>
public sealed record CatalogActor(string Channel, string? Client, string? Via = null);

/// <summary>
/// One edit of one catalog item. <c>Change</c> gets the current item (null when absent) and returns the new one
/// (null to remove it); it throws <see cref="CatalogEditException"/> to refuse. <c>ExpectedVersion</c> is the
/// version the caller read (optimistic concurrency).
/// </summary>
public sealed record CatalogEdit(
    string Section,
    string Id,
    string Operation,
    string? ExpectedVersion,
    CatalogActor Actor,
    Func<JsonObject?, JsonObject?> Change);

/// <summary>
/// The outcome of an edit. <c>Version</c> is the catalog's version after it (or the current one when refused);
/// <c>Changes</c> summarises what changed (<c>notes: changed</c>); <c>Errors</c> say why it was refused.
/// </summary>
public sealed record CatalogEditResult(CatalogEditStatus Status, string Version, IReadOnlyList<string> Changes, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Status is CatalogEditStatus.Applied or CatalogEditStatus.Unchanged;
}

/// <summary>One item as the file holds it, with the version to send back when changing it.</summary>
public sealed record CatalogItem(string Version, string Section, string Id, JsonObject Item);

public sealed class CatalogEditException : Exception
{
    public CatalogEditException(CatalogEditStatus status, string message)
        : base(message)
    {
        Status = status;
    }

    public CatalogEditException()
    {
    }

    public CatalogEditException(string message)
        : base(message)
    {
    }

    public CatalogEditException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CatalogEditStatus Status { get; } = CatalogEditStatus.Invalid;
}

/// <summary>The version of a catalog text: the first 16 hex digits of its SHA-256.</summary>
public static class CatalogVersion
{
    public static string Of(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}

/// <summary>
/// The edits the API and the MCP tools share (D-85, D-86): one place for their rules, so both channels behave
/// the same. Each returns the <see cref="CatalogEdit.Change"/> of an edit.
/// </summary>
public static class CatalogEdits
{
    public const string MachinesKey = "machines";
    public const string NotesKey = "notes";
    public const string PortalNameKey = "name";
    public const string PortalLogoKey = "logo";
    public const string PortalColorsKey = "colors";
    public const string PortalColorNameKey = "name";
    public const string PortalColorVersionKey = "version";

    /// <summary>Creates the item; refused when it exists.</summary>
    public static Func<JsonObject?, JsonObject?> Add(string section, string id, JsonObject item)
    {
        return current => current is not null
            ? throw new CatalogEditException(CatalogEditStatus.AlreadyExists, $"{section} '{id}' already exists: replace it instead.")
            : WithId(item, id);
    }

    /// <summary>Creates or replaces the item.</summary>
    public static Func<JsonObject?, JsonObject?> Upsert(string id, JsonObject item) => _ => WithId(item, id);

    public static Func<JsonObject?, JsonObject?> Remove(string section, string id)
    {
        return current => current is null ? throw NotFound(section, id) : null;
    }

    /// <summary>
    /// Sets the name, the logo and the colors of the badge texts of the portal (D-91): null leaves a value as it is, an
    /// empty text clears it; the <c>colors</c> and <c>portal</c> mappings are removed when nothing is left. The
    /// colors are checked by the catalog's validation, like the rest.
    /// </summary>
    public static Func<JsonObject?, JsonObject?> SetPortal(string? name, string? logo, string? nameColor = null, string? versionColor = null)
    {
        return current =>
        {
            var next = new JsonObject();
            Keep(next, PortalNameKey, name, current?[PortalNameKey]);
            Keep(next, PortalLogoKey, logo, current?[PortalLogoKey]);

            var colors = new JsonObject();
            var currentColors = current?[PortalColorsKey] as JsonObject;
            Keep(colors, PortalColorNameKey, nameColor, currentColors?[PortalColorNameKey]);
            Keep(colors, PortalColorVersionKey, versionColor, currentColors?[PortalColorVersionKey]);
            if (colors.Count > 0)
            {
                next[PortalColorsKey] = colors;
            }

            return next.Count == 0 ? null : next;
        };
    }

    private static void Keep(JsonObject target, string key, string? value, JsonNode? current)
    {
        var kept = value is null ? current?.ToString() : value.Trim();
        if (!string.IsNullOrEmpty(kept))
        {
            target[key] = kept;
        }
    }

    /// <summary>Sets (or clears, when empty) the notes of an item, or of one machine of an environment.</summary>
    public static Func<JsonObject?, JsonObject?> SetNotes(string section, string id, string? notes, string? machine = null)
    {
        return current =>
        {
            if (current is null)
            {
                throw NotFound(section, id);
            }

            if (machine is null)
            {
                return With(current, NotesKey, string.IsNullOrWhiteSpace(notes) ? null : JsonValue.Create(notes));
            }

            if (section != CatalogSections.Environments)
            {
                throw new CatalogEditException(CatalogEditStatus.Invalid, "Only environments have machines.");
            }

            var machines = Machines(current);
            var entry = machines.OfType<JsonObject>().FirstOrDefault(m => m["name"]?.ToString() == machine);
            if (entry is null)
            {
                machines.Add(new JsonObject { ["name"] = machine, [NotesKey] = notes });
            }
            else
            {
                machines[machines.IndexOf(entry)] = With(entry, NotesKey, string.IsNullOrWhiteSpace(notes) ? null : JsonValue.Create(notes));
            }

            return With(current, MachinesKey, machines);
        };
    }

    /// <summary>Creates or replaces one machine (by <c>name</c>) of an environment.</summary>
    public static Func<JsonObject?, JsonObject?> UpsertMachine(string environmentId, string name, JsonObject machine)
    {
        return current =>
        {
            if (current is null)
            {
                throw NotFound(CatalogSections.Environments, environmentId);
            }

            var next = machine.DeepClone().AsObject();
            next.Remove("name");
            next = new JsonObject(new[] { KeyValuePair.Create<string, JsonNode?>("name", name) }.Concat(next.Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()))));

            var machines = Machines(current);
            var index = machines.Select((m, i) => (m, i)).FirstOrDefault(x => x.m?["name"]?.ToString() == name, (null, -1)).Item2;
            if (index >= 0)
            {
                machines[index] = next;
            }
            else
            {
                machines.Add(next);
            }

            return With(current, MachinesKey, machines);
        };
    }

    public static Func<JsonObject?, JsonObject?> RemoveMachine(string environmentId, string name)
    {
        return current =>
        {
            if (current is null)
            {
                throw NotFound(CatalogSections.Environments, environmentId);
            }

            var machines = Machines(current);
            var entry = machines.FirstOrDefault(m => m?["name"]?.ToString() == name)
                ?? throw new CatalogEditException(CatalogEditStatus.NotFound, $"Environment '{environmentId}' has no machine '{name}'.");
            machines.Remove(entry);
            return With(current, MachinesKey, machines.Count == 0 ? null : machines);
        };
    }

    private static CatalogEditException NotFound(string section, string id) => new(CatalogEditStatus.NotFound, $"No {section} item '{id}' in the catalog.");

    private static JsonArray Machines(JsonObject item) => item[MachinesKey] is JsonArray machines ? machines.DeepClone().AsArray() : [];

    /// <summary>The item with its id first: an item without id gets the one of the route; a different id is refused.</summary>
    private static JsonObject WithId(JsonObject item, string id)
    {
        if (item["id"] is { } given && given.ToString() != id)
        {
            throw new CatalogEditException(CatalogEditStatus.Invalid, $"id: the item says '{given}' but the request targets '{id}'.");
        }

        var rest = item.Where(p => p.Key != "id").Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()));
        return new JsonObject(new[] { KeyValuePair.Create<string, JsonNode?>("id", id) }.Concat(rest));
    }

    /// <summary>A copy of <paramref name="item"/> with <paramref name="key"/> set (appended when new) or removed when null.</summary>
    private static JsonObject With(JsonObject item, string key, JsonNode? value)
    {
        var copy = item.DeepClone().AsObject();
        if (value is null)
        {
            copy.Remove(key);
        }
        else
        {
            copy[key] = value;
        }

        return copy;
    }
}
