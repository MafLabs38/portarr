using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;

namespace Portarr.Core.Mcp;

/// <summary>
/// Composes the rendering of a server for a client (D-113). The client's adapter says what is specific to it; this service decides everything
/// that is generic, so that no adapter has to: which client, which scope and operating system (with their defaults), the variables the workstation
/// must provide, the provisioning of the secrets from the catalog's store, and the notes that hold for every client.
/// </summary>
public interface IMcpRenderingService
{
    /// <summary>
    /// How to install the server in a client: the scope defaults to the server's, the operating system to Linux. Throws
    /// <see cref="ArgumentException"/> naming the valid values when the client, the scope or the system is unknown.
    /// </summary>
    McpRendering Install(McpServerDefinition server, string client, string? scope = null, string? os = null);

    /// <summary>How to remove the server from a client, if present; same defaults and errors as <see cref="Install"/>.</summary>
    McpRendering Remove(McpServerDefinition server, string client, string? scope = null, string? os = null);

    /// <summary>The installation in every client, each at the scope it holds the server at (the server's own, else the client's first).</summary>
    IReadOnlyList<McpRendering> InstallEverywhere(McpServerDefinition server, string? os = null);

    /// <summary>The operating system as the renderers expect it: trimmed, lower case, and the default when none is given.</summary>
    string NormalizeOs(string? os);
}

public sealed class McpRenderingService : IMcpRenderingService
{
    public const string DefaultOs = McpOperatingSystems.Linux;

    private readonly IMcpClientRegistry _clients;
    private readonly IMcpSecretStoreRegistry _stores;
    private readonly ICatalogService _catalogService;

    public McpRenderingService(IMcpClientRegistry clients, IMcpSecretStoreRegistry stores, ICatalogService catalogService)
    {
        _clients = clients;
        _stores = stores;
        _catalogService = catalogService;
    }

    public McpRendering Install(McpServerDefinition server, string client, string? scope = null, string? os = null)
    {
        return Compose(server, client, scope, os, removal: false);
    }

    public McpRendering Remove(McpServerDefinition server, string client, string? scope = null, string? os = null)
    {
        return Compose(server, client, scope, os, removal: true);
    }

    public IReadOnlyList<McpRendering> InstallEverywhere(McpServerDefinition server, string? os = null)
    {
        return _clients.All.Select(c => Install(server, c.Id, c.Scopes.Contains(server.Scope) ? server.Scope : c.Scopes[0], os)).ToList();
    }

    public string NormalizeOs(string? os) => string.IsNullOrWhiteSpace(os) ? DefaultOs : os.Trim().ToLowerInvariant();

    private McpRendering Compose(McpServerDefinition server, string client, string? scope, string? os, bool removal)
    {
        var renderer = _clients.Find(client?.Trim().ToLowerInvariant())
            ?? throw new ArgumentException($"Unknown client '{client}'. Clients: {string.Join(", ", _clients.Ids)}.", nameof(client));
        var resolvedScope = string.IsNullOrWhiteSpace(scope) ? server.Scope : scope.Trim().ToLowerInvariant();
        if (!McpScopes.All.Contains(resolvedScope))
        {
            throw new ArgumentException($"Unknown scope '{scope}'. Scopes: {string.Join(", ", McpScopes.All)}.", nameof(scope));
        }

        var resolvedOs = NormalizeOs(os);
        if (!McpOperatingSystems.All.Contains(resolvedOs))
        {
            throw new ArgumentException($"Unknown operating system '{os}'. Systems: {string.Join(", ", McpOperatingSystems.All)}.", nameof(os));
        }

        if (!renderer.Scopes.Contains(resolvedScope))
        {
            return new McpRendering(server.Id, renderer.Id, renderer.Name, resolvedScope, false, [], [], [$"{renderer.Name} has no '{resolvedScope}' scope; it holds servers at: {string.Join(", ", renderer.Scopes)}."], []);
        }

        var context = new McpRenderContext(server, resolvedScope, resolvedOs);
        var own = removal ? renderer.Remove(context) : renderer.Install(context);
        var operations = own.Operations.ToList();
        var notes = own.Notes.ToList();
        AddGenericNotes(context, operations, notes, removal);

        var secrets = removal ? [] : SecretsOf(server);
        var requiresSecrets = new List<string>();
        if (secrets.Count > 0 && _catalogService.Current.Catalog.SecretStore is { } store && _stores.Find(store.Kind) is { } provisioner)
        {
            operations.Add(provisioner.Provision(store, secrets, resolvedOs));
            notes.AddRange(provisioner.Notes(store, resolvedOs));
            requiresSecrets.AddRange(secrets.Select(s => provisioner.Identifier(store, s.Key)).Distinct(StringComparer.Ordinal));
        }

        return new McpRendering(server.Id, renderer.Id, renderer.Name, resolvedScope, true, removal ? [] : server.RequiredVariables(), operations, notes, requiresSecrets);
    }

    /// <summary>What holds for every client, whatever its adapter says.</summary>
    private static void AddGenericNotes(McpRenderContext context, List<McpOperation> operations, List<string> notes, bool removal)
    {
        if (context.Windows && operations.Exists(o => o.Kind == McpOperationKinds.Command))
        {
            notes.Add("Run the commands in PowerShell: they are quoted with single quotes, which cmd.exe does not read as quoting.");
        }

        if (!removal && context.Windows && context.Server.Stdio?.Command is "npx" or "npx.cmd")
        {
            notes.Add("On native Windows, if the client cannot start npx, declare the command as cmd with the arguments /c npx … (not verified for every client).");
        }
    }

    /// <summary>The secrets this server needs from the operator's store: the variable that receives each, and its key (D-116). One per variable.</summary>
    private static List<McpSecretRef> SecretsOf(McpServerDefinition server)
    {
        return server.Values
            .Where(v => v is { IsEnv: true, HasSecret: true })
            .Select(v => new McpSecretRef(v.EnvName!, v.SecretKey!))
            .DistinctBy(s => s.Variable)
            .OrderBy(s => s.Variable, StringComparer.Ordinal)
            .ToList();
    }
}
