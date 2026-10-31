namespace Portarr.Core.Accounts;

/// <summary>One permission: its key, the area the roles editor groups it under, and whether it is reserved.</summary>
/// <param name="Key">The stable key (<c>apps.view</c>), also the name of its authorization policy.</param>
/// <param name="Area">Where the roles editor lists it (Apps, Infrastructure, Data, Maintenance, Assistant, Catalog, System, Administration).</param>
/// <param name="Reserved">Defined for a feature that does not exist yet: only Admin holds it, a custom role cannot be given it.</param>
public sealed record PermissionInfo(string Key, string Area, bool Reserved = false)
{
    /// <summary>A view permission: what a read-only API key keeps of its owner's role.</summary>
    public bool IsView => !Reserved && Key.EndsWith(".view", StringComparison.Ordinal);
}

/// <summary>
/// The closed, code-defined list of permissions (V7, D-92). Each page and endpoint declares the one it needs through
/// an ASP.NET Core authorization policy named after it; a role is a named set of them. A permission is added with the
/// feature it protects, never before (<see cref="SecretsView"/> is the one reserved exception).
/// </summary>
public static class Permissions
{
    public const string AppsView = "apps.view";
    public const string InfrastructureView = "infrastructure.view";
    public const string DataView = "data.view";
    public const string MaintenanceView = "maintenance.view";

    /// <summary>The Assistant page: the MCP server's state and the knowledge base.</summary>
    public const string AssistantView = "assistant.view";

    /// <summary>The built-in chat (D-89): own conversations, own tokens.</summary>
    public const string ChatUse = "chat.use";

    /// <summary>Changes to the catalog (D-85): the API, the MCP write tools and approving a chat write.</summary>
    public const string CatalogWrite = "catalog.write";

    public const string SystemView = "system.view";
    public const string SettingsManage = "settings.manage";
    public const string UsersManage = "users.manage";

    /// <summary>Reserved for the day secrets can be revealed (audited); nothing uses it yet.</summary>
    public const string SecretsView = "secrets.view";

    public static IReadOnlyList<PermissionInfo> All { get; } =
    [
        new(AppsView, "Apps"),
        new(InfrastructureView, "Infrastructure"),
        new(DataView, "Data"),
        new(MaintenanceView, "Maintenance"),
        new(AssistantView, "Assistant"),
        new(ChatUse, "Assistant"),
        new(CatalogWrite, "Catalog"),
        new(SystemView, "System"),
        new(SettingsManage, "Administration"),
        new(UsersManage, "Administration"),
        new(SecretsView, "Administration", Reserved: true),
    ];

    public static IReadOnlySet<string> Keys { get; } = All.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>The permissions a role can be given: all but the reserved ones.</summary>
    public static IReadOnlySet<string> Assignable { get; } = All.Where(p => !p.Reserved).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> View { get; } = All.Where(p => p.IsView).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>The permissions that are in the list, in the list's order.</summary>
    public static IReadOnlyList<string> Ordered(IEnumerable<string> keys)
    {
        var wanted = keys.ToHashSet(StringComparer.Ordinal);
        return All.Where(p => wanted.Contains(p.Key)).Select(p => p.Key).ToList();
    }
}

/// <summary>
/// The four roles every Portarr has (V7, D-93): seeded, never deleted, their permission sets defined here so a new
/// permission reaches the right ones with the code that adds it. To vary one, a custom role is created from it.
/// </summary>
public static class BuiltInRoles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Observer = "Observer";
    public const string Guest = "Guest";

    private static readonly string[] OperatorExcluded = [Permissions.UsersManage, Permissions.SettingsManage, Permissions.SecretsView];

    public static IReadOnlyList<BuiltInRole> All { get; } =
    [
        new(Admin, "Everything, settings and users included.", Permissions.Keys),
        new(Operator, "Everything except settings, users and secrets.", Permissions.Keys.Except(OperatorExcluded).ToHashSet(StringComparer.Ordinal)),
        new(Observer, "Every page, read only; no chat, no change.", Permissions.View),
        new(Guest, "The apps launcher only.", new HashSet<string>(StringComparer.Ordinal) { Permissions.AppsView }),
    ];

    public static BuiltInRole? Find(string name) => All.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed record BuiltInRole(string Name, string Description, IReadOnlySet<string> Permissions);
