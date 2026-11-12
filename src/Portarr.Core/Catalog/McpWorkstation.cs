namespace Portarr.Core.Catalog;

/// <summary>
/// A machine the operator works from with an AI assistant (V9, D-111): a laptop, a training PC, the dev VM. Only a name matters; <c>Os</c>
/// decides the paths and commands of the plan, <c>Clients</c> are the assistants installed there, <c>Wanted</c> the MCP servers wanted on
/// it. <c>EnvironmentId</c> says the machine is also an environment of the catalog (a dev VM). Declarative: Portarr never reaches it (D-110).
/// </summary>
public sealed record WorkstationDefinition(
    string Id,
    string Name,
    string Os,
    string? EnvironmentId,
    IReadOnlyList<string> Clients,
    IReadOnlyList<WantedServer> Wanted,
    string? Notes = null)
{
    /// <summary>The clients a server is wanted in on this machine; empty when it is not wanted.</summary>
    public IReadOnlyList<string> ClientsWanting(string serverId) => Wanted.FirstOrDefault(w => w.ServerId == serverId)?.Clients ?? [];
}

/// <summary>
/// An MCP server wanted on a workstation, in these <c>Clients</c> (always resolved: the clients of the workstation unless the entry
/// narrows them) at this <c>Scope</c> (null: the server's own).
/// </summary>
public sealed record WantedServer(string ServerId, IReadOnlyList<string> Clients, string? Scope);
