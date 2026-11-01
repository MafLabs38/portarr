using Portarr.Core.Catalog;
using Portarr.Core.Checks;
using Portarr.Core.Workloads;

namespace Portarr.Core.Status;

/// <summary>
/// A workload of an app. <c>Name</c> is the matched workload's name, else what the selector looks for.
/// <c>Runtime</c> is its environment's connector kind (docker, cloudflare, kubernetes…).
/// </summary>
public sealed record AppWorkloadView(string EnvironmentId, string Name, MemberRole Role, Workload? Workload, ResolvedStatus Status, string Runtime = RuntimeNames.Docker);

/// <summary>A declared URL, or a LAN URL derived from a published port (<c>Derived</c>).</summary>
public sealed record AppUrlView(string Label, Uri Href, UrlScope Scope, bool Derived);

/// <summary>
/// The check of an app: its definition, the URL it probes (the check URL, else the public URL, else the
/// LAN URL) and its results so far. <c>Status</c> is the check's own status, before the critical rule.
/// </summary>
public sealed record AppCheckView(CheckDefinition Definition, Uri Target, CheckState? State, ResolvedStatus Status);

/// <summary>An app joined with its workloads and its check. <c>Icon</c> is the declared one, or one guessed from the first container image (D-47).</summary>
public sealed record AppView(
    CatalogApp App,
    ResolvedStatus Status,
    CatalogStack? Stack,
    MemberRole? Role,
    IReadOnlyList<AppWorkloadView> Workloads,
    IReadOnlyList<AppUrlView> Urls,
    string? Icon,
    AppCheckView? Check = null);

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
    private const string TcpProtocol = "tcp";

    private readonly ICatalogService _catalogService;
    private readonly IWorkloadInventory _inventory;
    private readonly ICheckStore _checks;
    private readonly TimeProvider _timeProvider;

    public FleetService(ICatalogService catalogService, IWorkloadInventory inventory, ICheckStore checks, TimeProvider timeProvider)
    {
        _catalogService = catalogService;
        _inventory = inventory;
        _checks = checks;
        _timeProvider = timeProvider;
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
                apps.Where(app => app.Workloads.Any(s => s.Matches(workload))).Select(app => app.Id).ToList(),
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
            .Select(w => (w.Address, Port: w.Workload!.PublishedPorts.FirstOrDefault(p => p.Protocol == TcpProtocol)))
            .FirstOrDefault(w => w.Port is not null);

        // UriBuilder puts an IPv6 address in brackets.
        if (derived.Port is not null
            && Uri.TryCreate(new UriBuilder(Uri.UriSchemeHttp, derived.Address, derived.Port.PublicPort).ToString(), UriKind.Absolute, out var href))
        {
            urls.Add(new AppUrlView("LAN", href, UrlScope.Lan, true));
        }

        return urls;
    }

    private List<AppView> BuildApps(Catalog.Catalog catalog, IReadOnlyDictionary<string, EnvironmentInventory> inventory)
    {
        var now = _timeProvider.GetUtcNow();
        return catalog.Apps.Select(app => BuildAppView(app, catalog, inventory, now)).ToList();
    }

    /// <summary>The URL a check probes: its own, else the first public URL, else the first LAN URL (D-50).</summary>
    private AppCheckView? BuildCheck(CatalogApp app, List<AppUrlView> urls, DateTimeOffset now)
    {
        if (app.Check is null)
        {
            return null;
        }

        var target = app.Check.Url
            ?? urls.Find(url => url.Scope == UrlScope.Public)?.Href
            ?? urls.Find(url => url.Scope == UrlScope.Lan)?.Href;
        if (target is null)
        {
            return null;
        }

        // Results of a previous target or definition describe another check.
        var state = _checks.Get(new CheckId(app.Id));
        if (state is not null && (state.Target != target || state.Definition != app.Check))
        {
            state = null;
        }

        return new AppCheckView(app.Check, target, state, StatusResolver.ForCheck(state?.Latest, now));
    }

    private AppView BuildAppView(CatalogApp app, Catalog.Catalog catalog, IReadOnlyDictionary<string, EnvironmentInventory> inventory, DateTimeOffset now)
    {
        var matches = app.Workloads.Select(selector =>
        {
            var environmentInventory = inventory.GetValueOrDefault(selector.EnvironmentId);
            var workload = selector.Pick(environmentInventory?.Workloads ?? []);
            return (selector, environmentInventory, workload);
        }).ToList();

        var workloadViews = matches.Select(m => new AppWorkloadView(
            m.selector.EnvironmentId,
            m.workload?.Name ?? m.selector.Description,
            m.selector.Role,
            m.workload,
            StatusResolver.ForSelector(m.selector, m.environmentInventory, m.workload),
            catalog.Environments.FirstOrDefault(e => e.Id == m.selector.EnvironmentId)?.Runtime ?? RuntimeNames.None)).ToList();

        var stack = catalog.Stacks.FirstOrDefault(s => s.Members.Any(member => member.AppId == app.Id));
        var role = stack?.Members.First(member => member.AppId == app.Id).Role;
        var icon = app.Icon ?? workloadViews.Select(w => w.Workload).OfType<Workload>().Select(w => IconNames.FromImage(w.Image)).FirstOrDefault();

        var urls = BuildUrls(app, workloadViews, catalog);
        var check = BuildCheck(app, urls, now);
        var status = StatusResolver.ForApp(matches, check);

        return new AppView(app, status, stack, role, workloadViews, urls, icon, check);
    }
}
