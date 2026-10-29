using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Core.Status;

public sealed record AppWorkloadView(string EnvironmentId, string Name, MemberRole Role, Workload? Workload, ResolvedStatus Status);

/// <summary>A declared URL, or a LAN URL derived from a published port (<c>Derived</c>).</summary>
public sealed record AppUrlView(string Label, Uri Href, UrlScope Scope, bool Derived);

/// <summary>An app joined with its workloads. <c>Icon</c> is the declared one, or one guessed from the first container image (D-47).</summary>
public sealed record AppView(
    CatalogApp App,
    ResolvedStatus Status,
    CatalogStack? Stack,
    MemberRole? Role,
    IReadOnlyList<AppWorkloadView> Workloads,
    IReadOnlyList<AppUrlView> Urls,
    string? Icon);

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
        return BuildApps(_catalogService.Current.Catalog, _inventory.Current);
    }

    // Each call reads the catalog and the inventory once: a hot reload in between cannot
    // produce a stack member that is not in the app list.
    public IReadOnlyList<StackView> GetStacks()
    {
        var catalog = _catalogService.Current.Catalog;
        var apps = BuildApps(catalog, _inventory.Current).ToDictionary(a => a.App.Id);

        return catalog.Stacks.Select(stack =>
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
        var inventories = _inventory.Current;

        return inventories.Values
            .SelectMany(inventory => inventory.Workloads.Select(workload => new WorkloadView(
                workload,
                inventory.Reachable ? StatusResolver.ForWorkload(workload) : new ResolvedStatus(HealthStatus.Unknown, $"{inventory.EnvironmentId} unreachable"),
                apps.Where(app => app.Workloads.Any(s => s.EnvironmentId == workload.EnvironmentId && s.Name == workload.Name)).Select(app => app.Id).ToList(),
                !inventory.Reachable)))
            .OrderBy(view => view.Workload.EnvironmentId, StringComparer.Ordinal)
            .ThenBy(view => view.Workload.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Declared URLs, plus a LAN URL built from the environment address and the first published TCP
    /// port when the app declares none (D-46). Web apps only: a service's port is rarely a UI.
    /// </summary>
    private static List<AppUrlView> BuildUrls(CatalogApp app, IReadOnlyList<AppWorkloadView> workloads, Catalog.Catalog catalog)
    {
        var urls = app.Urls.Select(url => new AppUrlView(url.Label, url.Href, url.Scope, false)).ToList();
        if (app.Type != AppType.Web || urls.Exists(url => url.Scope == UrlScope.Lan))
        {
            return urls;
        }

        var derived = workloads
            .OrderBy(w => w.Role)
            .Select(w => (Workload: w.Workload, Address: catalog.Environments.FirstOrDefault(e => e.Id == w.EnvironmentId)?.Address))
            .Where(w => w.Workload is not null && w.Address is not null)
            .Select(w => (w.Address, Port: w.Workload!.PublishedPorts.FirstOrDefault(p => p.Protocol == "tcp")))
            .FirstOrDefault(w => w.Port is not null);

        if (derived.Port is not null && Uri.TryCreate($"http://{derived.Address}:{derived.Port.PublicPort}", UriKind.Absolute, out var href))
        {
            urls.Add(new AppUrlView("LAN", href, UrlScope.Lan, true));
        }

        return urls;
    }

    private static List<AppView> BuildApps(Catalog.Catalog catalog, IReadOnlyDictionary<string, EnvironmentInventory> inventory)
    {
        return catalog.Apps.Select(app => BuildAppView(app, catalog, inventory)).ToList();
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
            m.selector.Role,
            m.workload,
            StatusResolver.ForSelector(m.selector, m.environmentInventory, m.workload))).ToList();

        var stack = catalog.Stacks.FirstOrDefault(s => s.Members.Any(member => member.AppId == app.Id));
        var role = stack?.Members.First(member => member.AppId == app.Id).Role;
        var icon = app.Icon ?? workloadViews.Select(w => w.Workload).OfType<Workload>().Select(w => IconNames.FromImage(w.Image)).FirstOrDefault();

        return new AppView(app, StatusResolver.ForApp(matches), stack, role, workloadViews, BuildUrls(app, workloadViews, catalog), icon);
    }
}
