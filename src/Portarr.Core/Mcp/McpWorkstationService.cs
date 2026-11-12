using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Core.Mcp;

/// <summary>A workstation with the servers wanted on it, resolved against the catalog.</summary>
public sealed record McpWorkstationView(WorkstationDefinition Workstation, IReadOnlyList<McpServerView> Wanted);

/// <summary>
/// What an assistant on a workstation does to bring it in line with the catalog (D-110): <c>Install</c> the wanted servers in each
/// client, <c>Remove</c> (if present) the servers of the catalog that are not wanted there, and provide <c>RequiresEnv</c>, for which it must be
/// able to read the keys of <c>RequiresSecrets</c> in the operator's secret store (D-117). Nothing is
/// tracked about whether it was applied: the assistant that applies it is the witness (D-114).
/// </summary>
public sealed record McpWorkstationPlan(
    WorkstationDefinition Workstation,
    string Os,
    IReadOnlyList<McpRendering> Install,
    IReadOnlyList<McpRendering> Remove,
    IReadOnlyList<string> RequiresEnv,
    IReadOnlyList<string> RequiresSecrets);

public interface IMcpWorkstationService
{
    IReadOnlyList<McpWorkstationView> List();

    /// <summary>A workstation by id or by name (case-insensitive), or null.</summary>
    McpWorkstationView? Find(string idOrName);

    /// <summary>
    /// The plan of a workstation. The operating system defaults to the workstation's own; an unknown one throws
    /// <see cref="ArgumentException"/> naming the valid values.
    /// </summary>
    McpWorkstationPlan GetPlan(WorkstationDefinition workstation, string? os = null);
}

public sealed class McpWorkstationService : IMcpWorkstationService
{
    private readonly ICatalogService _catalogService;
    private readonly IMcpServerService _servers;

    public McpWorkstationService(ICatalogService catalogService, IMcpServerService servers)
    {
        _catalogService = catalogService;
        _servers = servers;
    }

    public IReadOnlyList<McpWorkstationView> List()
    {
        var servers = _servers.List().ToDictionary(s => s.Server.Id, StringComparer.Ordinal);
        return _catalogService.Current.Catalog.WorkstationList
            .Select(w => new McpWorkstationView(w, w.Wanted.Select(x => servers.GetValueOrDefault(x.ServerId)).OfType<McpServerView>().ToList()))
            .ToList();
    }

    public McpWorkstationView? Find(string idOrName)
    {
        var workstations = List();
        return workstations.FirstOrDefault(w => string.Equals(w.Workstation.Id, idOrName, StringComparison.OrdinalIgnoreCase))
            ?? workstations.FirstOrDefault(w => string.Equals(w.Workstation.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public McpWorkstationPlan GetPlan(WorkstationDefinition workstation, string? os = null)
    {
        var system = string.IsNullOrWhiteSpace(os) ? workstation.Os : os.Trim().ToLowerInvariant();
        var servers = _servers.List().Select(s => s.Server).ToList();

        var install = new List<McpRendering>();
        foreach (var wanted in workstation.Wanted)
        {
            if (servers.Find(s => s.Id == wanted.ServerId) is not { } server)
            {
                continue;
            }

            install.AddRange(wanted.Clients.Select(client => _servers.Render(server, client, wanted.Scope, system)));
        }

        // Every other (server, client) pair of this machine: remove it if it is there. A client that cannot hold the server at its scope has nothing to remove.
        var remove = new List<McpRendering>();
        foreach (var server in servers)
        {
            var wantedIn = workstation.ClientsWanting(server.Id);
            foreach (var client in workstation.Clients.Where(c => !wantedIn.Contains(c)))
            {
                var rendering = _servers.RenderRemoval(server, client, null, system);
                if (rendering.Supported)
                {
                    remove.Add(rendering);
                }
            }
        }

        var requires = install.SelectMany(r => r.RequiresEnv).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var secrets = install.SelectMany(r => r.RequiresSecrets).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return new McpWorkstationPlan(workstation, system, install, remove, requires, secrets);
    }
}
