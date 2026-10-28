using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Core.Status;

public sealed record AppWorkloadView(string EnvironmentId, string Name, Workload? Workload, ResolvedStatus Status);

public sealed record AppView(CatalogApp App, ResolvedStatus Status, CatalogStack? Stack, MemberRole? Role, IReadOnlyList<AppWorkloadView> Workloads);

public sealed record StackMemberView(StackMember Member, string AppName, ResolvedStatus Status);

public sealed record StackView(CatalogStack Stack, ResolvedStatus Status, IReadOnlyList<StackMemberView> Members);

public sealed record WorkloadView(Workload Workload, ResolvedStatus Status, IReadOnlyList<string> AppIds, bool Stale);

public interface IFleetService
{
    IReadOnlyList<AppView> GetApps();

    IReadOnlyList<StackView> GetStacks();

    IReadOnlyList<WorkloadView> GetWorkloads();
}

/// <summary>The declared catalog joined with the discovered inventory, with statuses resolved.</summary>
public sealed class FleetService : IFleetService
{
    private readonly ICatalogService _catalogService;
    private readonly IWorkloadInventory _inventory;

    public FleetService(ICatalogService catalogService, IWorkloadInventory inventory)
    {
        _catalogService = catalogService;
        _inventory = inventory;
    }

    public IReadOnlyList<AppView> GetApps()
    {
        var catalog = _catalogService.Current.Catalog;
        var inventory = _inventory.Current;

        return catalog.Apps.Select(app => BuildAppView(app, catalog, inventory)).ToList();
    }

    public IReadOnlyList<StackView> GetStacks()
    {
        var apps = GetApps().ToDictionary(a => a.App.Id);

        return _catalogService.Current.Catalog.Stacks.Select(stack =>
        {
            var members = stack.Members
                .Select(member => new StackMemberView(member, apps[member.AppId].App.Name, apps[member.AppId].Status))
                .ToList();
            var status = StatusResolver.ForStack(members.Select(m => (m.Member, m.AppName, m.Status.Status)).ToList());

            return new StackView(stack, status, members);
        }).ToList();
    }

    public IReadOnlyList<WorkloadView> GetWorkloads()
    {
        var apps = _catalogService.Current.Catalog.Apps;

        return _inventory.Current.Values
            .SelectMany(inventory => inventory.Workloads.Select(workload => new WorkloadView(
                workload,
                inventory.Reachable ? StatusResolver.ForWorkload(workload) : new ResolvedStatus(HealthStatus.Unknown, $"{inventory.EnvironmentId} unreachable"),
                apps.Where(app => app.Workloads.Any(s => s.EnvironmentId == workload.EnvironmentId && s.Name == workload.Name)).Select(app => app.Id).ToList(),
                !inventory.Reachable)))
            .OrderBy(view => view.Workload.EnvironmentId, StringComparer.Ordinal)
            .ThenBy(view => view.Workload.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static AppView BuildAppView(CatalogApp app, Catalog.Catalog catalog, IReadOnlyDictionary<string, EnvironmentInventory> inventory)
    {
        var matches = app.Workloads.Select(selector =>
        {
            var environmentInventory = inventory.GetValueOrDefault(selector.EnvironmentId);
            var workload = environmentInventory?.Workloads.FirstOrDefault(w => w.Name == selector.Name);
            return (selector, environmentInventory, workload);
        }).ToList();

        var workloadViews = matches.Select(m => new AppWorkloadView(
            m.selector.EnvironmentId,
            m.selector.Name,
            m.workload,
            StatusResolver.ForApp(app with { Workloads = [m.selector] }, [m]))).ToList();

        var stack = catalog.Stacks.FirstOrDefault(s => s.Members.Any(member => member.AppId == app.Id));
        var role = stack?.Members.First(member => member.AppId == app.Id).Role;

        return new AppView(app, StatusResolver.ForApp(app, matches), stack, role, workloadViews);
    }
}
