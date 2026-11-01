namespace Portarr.Core.Workloads;

/// <summary>How Portarr reaches an environment: connected, unreachable, pending (not read yet) or not-connected.</summary>
public static class EnvironmentConnection
{
    /// <param name="supported">A workload connector supports the environment.</param>
    /// <param name="current">Its last inventory, if any.</param>
    public static string Of(bool supported, EnvironmentInventory? current) => (supported, current) switch
    {
        (false, _) or (_, { NotConfigured: true }) => "not-connected",
        (_, null) => "pending",
        (_, { Reachable: true }) => "connected",
        _ => "unreachable",
    };
}
