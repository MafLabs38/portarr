using Portarr.Core.Infrastructure;
using Portarr.Core.Status;

namespace Portarr.Api.V1.Infrastructure;

/// <summary>Joins the catalog, the live machine metrics and the fleet's workloads into the Infrastructure view.</summary>
public static class InfrastructureResourceBuilder
{
    /// <summary>
    /// Every environment, in catalog order, with its machines, their live window of points (D-54) and their
    /// workloads sorted by consumption.
    /// A machine with a host agent carries its reads (D-72), and a VM with one reports its whole load to its hypervisor.
    /// </summary>
    public static IReadOnlyList<InfrastructureEnvironmentResource> Build(
        Core.Catalog.Catalog catalog,
        IReadOnlyDictionary<string, EnvironmentMetrics> metrics,
        IReadOnlyList<WorkloadView> fleetWorkloads,
        IReadOnlyDictionary<HostMachineKey, HostMetricsView>? hosts = null)
    {
        hosts ??= new Dictionary<HostMachineKey, HostMetricsView>();
        var names = catalog.Environments.ToDictionary(e => e.Id, e => e.Name, StringComparer.Ordinal);
        var workloads = fleetWorkloads.ToLookup(w => w.Workload.EnvironmentId, StringComparer.Ordinal);

        return catalog.Environments.Select(environment =>
        {
            var current = metrics.GetValueOrDefault(environment.Id);
            var machines = current?.Machines ?? [];
            var usage = current?.Workloads ?? new Dictionary<string, WorkloadUsageSample>();

            // A Docker environment is one machine: its containers have no node.
            var placed = workloads[environment.Id]
                .Select(w => (View: w, Machine: w.Workload.Node ?? (machines.Count == 1 ? machines[0].Machine.Id : null)))
                .ToList();

            var machineResources = machines.Select(machine => ToResource(
                machine,
                placed.Where(p => p.Machine == machine.Machine.Id)
                    .Select(p => (p.View.Workload.Id, Usage: usage.GetValueOrDefault(p.View.Workload.Id)))
                    .OrderByDescending(p => p.Usage is not null)
                    .ThenByDescending(p => p.Usage?.CpuCores ?? 0)
                    .ThenByDescending(p => p.Usage?.MemoryBytes ?? 0)
                    .Select(p => new MachineWorkloadResource(p.Id, p.Usage?.CpuCores, p.Usage?.MemoryBytes))
                    .ToList(),
                names,
                hosts.GetValueOrDefault(new HostMachineKey(environment.Id, machine.Machine.Id)),
                hosts)).ToList();

            var known = machines.Select(m => m.Machine.Id).ToHashSet(StringComparer.Ordinal);
            var unplaced = machines.Count == 0 ? [] : placed.Where(p => p.Machine is null || !known.Contains(p.Machine)).Select(p => p.View.Workload.Id).ToList();

            return new InfrastructureEnvironmentResource(
                environment.Id,
                environment.Name,
                environment.Runtime,
                current?.SampledAt,
                current?.Error,
                (int)Math.Round((current?.Interval ?? TimeSpan.Zero).TotalSeconds),
                current?.Resolution is { } resolution ? (int)Math.Round(resolution.TotalSeconds) : null,
                (int)InfrastructureMonitor.Window.TotalSeconds,
                machineResources,
                unplaced);
        }).ToList();
    }

    private static MachineResource ToResource(
        MachineView view,
        IReadOnlyList<MachineWorkloadResource> workloads,
        Dictionary<string, string> names,
        HostMetricsView? agent,
        IReadOnlyDictionary<HostMachineKey, HostMetricsView> hosts)
    {
        var machine = view.Machine;
        return new MachineResource(
            machine.Id,
            machine.Name,
            machine.Kind.ToResource(),
            machine.Roles ?? [],
            machine.Ready,
            machine.CpuCapacityCores,
            machine.MemoryCapacityBytes,
            machine.CpuCores,
            machine.MemoryBytes,
            machine.Scope.ToResource(),
            view.History.Select(p => new MachinePointResource(p.At, p.CpuCores, p.MemoryBytes, p.GuestCpuCores)).ToList(),
            workloads,
            view.HostEnvironmentId is { } hypervisor ? new MachineHostResource(hypervisor, names.GetValueOrDefault(hypervisor, hypervisor)) : null,
            (view.Guests ?? []).Select(g => ToGuest(g, hosts.GetValueOrDefault(new HostMachineKey(g.EnvironmentId, g.MachineId)))).ToList(),
            agent?.ToResource());
    }

    /// <summary>A VM with its own host agent reports its whole load and memory (D-72), not only its containers.</summary>
    private static GuestResource ToGuest(GuestMachine guest, HostMetricsView? host)
    {
        if (host?.Latest is not { } reading || host.Error is not null)
        {
            return new GuestResource(guest.EnvironmentId, guest.MachineId, guest.Name, guest.CpuCapacityCores, guest.MemoryCapacityBytes, guest.CpuCores, guest.Scope?.ToResource());
        }

        var cores = reading.Cores > 0 ? reading.Cores : guest.CpuCapacityCores;
        var load = reading.CpuPercent is { } percent && cores is { } count ? Math.Round(percent / 100 * count, 3) : (double?)null;
        return new GuestResource(guest.EnvironmentId, guest.MachineId, guest.Name, cores, reading.Memory.TotalBytes, load ?? guest.CpuCores, load is null ? guest.Scope?.ToResource() : UsageScope.Host.ToResource());
    }
}
