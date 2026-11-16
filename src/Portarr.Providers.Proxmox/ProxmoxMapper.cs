using System.Globalization;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Proxmox;

/// <summary>Proxmox's words for Portarr's: a guest's state, its use of its limits, its uptime in a few words.</summary>
internal static class ProxmoxMapper
{
    public static Workload ToWorkload(string environmentId, string node, ProxmoxGuest guest, DateTimeOffset now)
    {
        var state = ToState(guest.Status);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["proxmox.vmid"] = guest.VmId.ToString(CultureInfo.InvariantCulture) };
        if (!string.IsNullOrWhiteSpace(guest.Tags))
        {
            labels["proxmox.tags"] = guest.Tags;
        }

        return new Workload(
            environmentId,
            guest.VmId.ToString(CultureInfo.InvariantCulture),
            guest.Name,
            ProxmoxSource.ContainerKind,
            state,
            WorkloadHealth.None,
            state == WorkloadState.Running ? $"Up {Duration(guest.UptimeSeconds)}" : guest.Status,
            state == WorkloadState.Running ? now - TimeSpan.FromSeconds(guest.UptimeSeconds) : DateTimeOffset.UnixEpoch,
            null,
            labels,
            Node: node);
    }

    public static WorkloadUsage ToUsage(ProxmoxGuest guest) => new(guest.CpuCores, guest.MemoryBytes, guest.MemoryMaxBytes > 0 ? guest.MemoryMaxBytes : null);

    /// <summary>The node as one machine: its cores and memory, and what is used of them. A host agent on it, when the catalog names one, refines this (D-72).</summary>
    public static MachineSample ToMachine(CatalogEnvironment environment, ProxmoxNode node)
    {
        return new MachineSample(
            environment.Id,
            environment.Name,
            ProxmoxSource.MachineKind,
            node.Cores > 0 ? node.Cores : null,
            node.MemoryMaxBytes > 0 ? node.MemoryMaxBytes : null,
            node.Online ? Math.Round(node.CpuFraction * node.Cores, 3) : null,
            node.Online ? node.MemoryBytes : null,
            UsageScope.Host,
            Ready: node.Online);
    }

    internal static WorkloadState ToState(string status) => status switch
    {
        "running" => WorkloadState.Running,
        "stopped" => WorkloadState.Exited,
        "paused" or "suspended" => WorkloadState.Paused,
        _ => WorkloadState.Unknown,
    };

    internal static string Duration(long seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(seconds, 0));
        if (span.TotalMinutes < 1)
        {
            return "seconds";
        }

        if (span.TotalHours < 1)
        {
            return Plural((int)span.TotalMinutes, "minute");
        }

        return span.TotalDays < 2 ? Plural((int)span.TotalHours, "hour") : Plural((int)span.TotalDays, "day");
    }

    private static string Plural(int count, string unit) => count == 1 ? $"1 {unit}" : $"{count} {unit}s";
}
