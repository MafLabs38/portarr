using Portarr.Api.V1.Data;
using Portarr.Api.V1.Infrastructure;
using Portarr.Api.V1.Maintenance;
using Portarr.Api.V1.Workloads;
using Portarr.Core.Catalog;
using Portarr.Core.Data;
using Portarr.Core.Infrastructure;
using Portarr.Core.Maintenance;
using Portarr.Core.Status;
using Portarr.Core.Workloads;

namespace Portarr.Mcp.Digest;

/// <summary>
/// Portarr's live view, told to an AI assistant (V5, D-68). It only re-reads what the pages already show: the
/// fleet (catalog joined with the inventory), the Infrastructure, Data and Maintenance views, through the same
/// Core services and API mappers. Built from memory: a call reaches no connector, and nothing here acts (D-31).
/// </summary>
public sealed class FleetDigest
{
    /// <summary>Most subjects named by one finding; the count tells the rest.</summary>
    public const int MaxSubjects = 10;

    /// <summary>Most entries of a long list (workload search, data assets, repositories, images).</summary>
    public const int MaxItems = 50;

    private const int TopConsumers = 5;
    private const int BusiestMachines = 5;
    private const double BytesPerGiB = 1024d * 1024 * 1024;

    private readonly ICatalogService _catalogService;
    private readonly IFleetService _fleetService;
    private readonly IWorkloadInventory _inventory;
    private readonly IInfrastructureMonitor _monitor;
    private readonly IHostMetricsMonitor _hostMonitor;
    private readonly IDataService _dataService;
    private readonly IMaintenanceService _maintenanceService;
    private readonly IEnumerable<IWorkloadSource> _sources;
    private readonly TimeProvider _timeProvider;

    public FleetDigest(
        ICatalogService catalogService,
        IFleetService fleetService,
        IWorkloadInventory inventory,
        IInfrastructureMonitor monitor,
        IHostMetricsMonitor hostMonitor,
        IDataService dataService,
        IMaintenanceService maintenanceService,
        IEnumerable<IWorkloadSource> sources,
        TimeProvider timeProvider)
    {
        _catalogService = catalogService;
        _fleetService = fleetService;
        _inventory = inventory;
        _monitor = monitor;
        _hostMonitor = hostMonitor;
        _dataService = dataService;
        _maintenanceService = maintenanceService;
        _sources = sources;
        _timeProvider = timeProvider;
    }

    public DateTimeOffset Now => _timeProvider.GetUtcNow();

    public Overview GetOverview()
    {
        var state = _catalogService.Current;
        var apps = _fleetService.GetApps();
        var environments = GetEnvironments();
        var infrastructure = GetInfrastructure(null);
        var data = _dataService.GetReport();
        var maintenance = _maintenanceService.GetReport();

        var findings = new List<Finding>();
        findings.AddRange(AppFindings(apps));
        findings.AddRange(InfrastructureFindings(environments, infrastructure));
        findings.AddRange(DataFindings(data));
        findings.AddRange(maintenance.Attention.Select(a => new Finding("maintenance", Level(a.Level), a.Kind, a.Count, Cap(a.Subjects.Select(s => s.Label)))));
        if (state.Errors.Count > 0)
        {
            findings.Add(new Finding("catalog", "warning", "catalog-errors", state.Errors.Count, []));
        }

        var busiest = infrastructure
            .SelectMany(e => e.Machines.Select(m => new MachineLoad(e.Name, m.Name, m.Cpu.NowPercent, m.Memory.NowPercent, m.Scope)))
            .OrderByDescending(m => Math.Max(m.CpuPercent ?? 0, m.MemoryPercent ?? 0))
            .Take(BusiestMachines)
            .ToList();

        return new Overview(
            Now,
            CountApps(apps),
            findings.OrderBy(f => LevelRank(f.Level)).ToList(),
            environments,
            busiest,
            ToCounts(data.Summary),
            ToCounts(maintenance.Summary),
            state.Errors.Count);
    }

    public IReadOnlyList<EnvironmentLine> GetEnvironments()
    {
        var catalog = _catalogService.Current.Catalog;
        var inventory = _inventory.Current;
        return catalog.Environments.Select(environment =>
        {
            var current = inventory.GetValueOrDefault(environment.Id);
            return new EnvironmentLine(
                environment.Id,
                environment.Name,
                environment.Runtime,
                EnvironmentConnection.Of(_sources.Any(s => s.Supports(environment)), current),
                current?.Error,
                current?.Workloads.Count ?? 0,
                NameOf(catalog, environment.Host));
        }).ToList();
    }

    /// <param name="status">Healthy, Degraded, Down, Unknown, Paused or Maintenance (case-insensitive); null: every app.</param>
    /// <param name="stack">A stack id or name; null: every stack.</param>
    /// <param name="environment">An environment id or name the app runs in; null: everywhere.</param>
    public IReadOnlyList<AppLine> ListApps(string? status, string? stack, string? environment)
    {
        var catalog = _catalogService.Current.Catalog;
        var environmentId = environment is null ? null : FindEnvironment(catalog, environment)?.Id ?? environment;

        return _fleetService.GetApps()
            .Where(a => status is null || string.Equals(a.Status.Status.ToString(), status, StringComparison.OrdinalIgnoreCase))
            .Where(a => stack is null || (a.Stack is { } s && (Same(s.Id, stack) || Same(s.Name, stack))))
            .Where(a => environmentId is null || a.Workloads.Any(w => Same(w.EnvironmentId, environmentId)))
            .OrderByDescending(a => a.Status.Status)
            .ThenBy(a => a.App.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AppLine(
                a.App.Id,
                a.App.Name,
                a.Status.Status.ToString(),
                StatusText.Of(a.Status),
                a.Stack?.Name,
                a.Role?.ToString().ToLowerInvariant(),
                a.Workloads.Select(w => w.EnvironmentId).Distinct(StringComparer.Ordinal).ToList(),
                PreferredUrl(a.Urls)))
            .ToList();
    }

    /// <summary>An app by id or name (case-insensitive); null when none matches.</summary>
    public AppDetail? GetApp(string idOrName)
    {
        var catalog = _catalogService.Current.Catalog;
        var view = _fleetService.GetApps().FirstOrDefault(a => Same(a.App.Id, idOrName)) ?? _fleetService.GetApps().FirstOrDefault(a => Same(a.App.Name, idOrName));
        if (view is null)
        {
            return null;
        }

        var app = view.App;
        var metrics = _monitor.Current;
        var data = _dataService.GetReport().ToResource(catalog.Environments);
        var images = _maintenanceService.GetReport().ToResource(catalog).Images;

        return new AppDetail(
            app.Id,
            app.Name,
            app.Type.ToString().ToLowerInvariant(),
            app.Description,
            app.Tags,
            view.Status.Status.ToString(),
            StatusText.Of(view.Status),
            view.Stack?.Name,
            view.Role?.ToString().ToLowerInvariant(),
            view.Urls.Select(u => new UrlLine(u.Label, SafeUrl.Of(u.Href), u.Scope.ToString().ToLowerInvariant())).ToList(),
            view.Workloads.Select(w => WorkloadLineOf(w, catalog, metrics)).ToList(),
            CheckLineOf(view.Check),
            data.Assets.Where(a => a.AppId == app.Id).Select(a => new AppDataLine(a.Id, a.Name, a.Kind, a.Importance, a.State, a.Local?.State, a.Offsite?.State, Newest(a))).ToList(),
            images.Where(i => i.Users.Any(u => u.AppIds.Contains(app.Id))).Select(ImageLineOf).ToList(),
            app.Repository);
    }

    /// <summary>Names of the apps closest to a query, for a "not found" answer.</summary>
    public IReadOnlyList<string> SuggestApps(string query)
    {
        return _fleetService.GetApps()
            .Select(a => a.App)
            .Where(a => a.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || query.Contains(a.Id, StringComparison.OrdinalIgnoreCase))
            .Select(a => $"{a.Name} ({a.Id})")
            .Take(MaxSubjects)
            .ToList();
    }

    /// <param name="environment">An environment id or name; null: every environment.</param>
    public IReadOnlyList<InfrastructureLine> GetInfrastructure(string? environment)
    {
        var catalog = _catalogService.Current.Catalog;
        var apps = _fleetService.GetWorkloads().ToDictionary(w => (w.Workload.EnvironmentId, w.Workload.Id), w => w);
        var resources = InfrastructureResourceBuilder.Build(catalog, _monitor.Current, _fleetService.GetWorkloads(), _hostMonitor.Current);

        return resources
            .Where(e => environment is null || Same(e.Id, environment) || Same(e.Name, environment))
            .Select(e => new InfrastructureLine(
                e.Id,
                e.Name,
                e.Runtime,
                e.SampledAt,
                e.Error,
                e.Machines.Select(m => MachineLineOf(e.Id, m, apps)).ToList(),
                e.Unplaced.Select(id => apps.GetValueOrDefault((e.Id, id))?.Workload.Name ?? id).ToList()))
            .ToList();
    }

    /// <summary>Workloads whose name, image, namespace or app contains the query (case-insensitive), not running first.</summary>
    public IReadOnlyList<WorkloadMatch> FindWorkloads(string query)
    {
        var catalog = _catalogService.Current.Catalog;
        var names = catalog.Apps.ToDictionary(a => a.Id, a => a.Name, StringComparer.Ordinal);
        return _fleetService.GetWorkloads()
            .Where(w => Contains(w.Workload.Name, query) || Contains(w.Workload.Image, query) || Contains(w.Workload.Namespace, query)
                || w.AppIds.Any(id => Contains(id, query) || Contains(names.GetValueOrDefault(id), query)))
            .OrderByDescending(w => w.Status.Status)
            .ThenBy(w => w.Workload.Name, StringComparer.Ordinal)
            .Take(MaxItems)
            .Select(w => new WorkloadMatch(
                w.Workload.EnvironmentId,
                w.Workload.Name,
                w.Workload.Namespace,
                w.Workload.Node,
                w.Workload.Image,
                w.Workload.State.ToString().ToLowerInvariant(),
                w.Workload.Health.ToString().ToLowerInvariant(),
                w.Status.Status.ToString(),
                StatusText.Of(w.Status),
                w.AppIds.Select(id => names.GetValueOrDefault(id) ?? id).ToList(),
                w.Stale))
            .ToList();
    }

    /// <param name="onlyAtRisk">Only the assets that are not protected (waiting, unknown, not set up, late, failed, missing).</param>
    public DataDigest GetDataCoverage(bool onlyAtRisk)
    {
        var catalog = _catalogService.Current.Catalog;
        var report = _dataService.GetReport();
        var resource = report.ToResource(catalog.Environments);
        var names = catalog.Apps.ToDictionary(a => a.Id, a => a.Name, StringComparer.Ordinal);
        var protectedState = CopyState.Protected.ToResource();

        var assets = resource.Assets
            .Where(a => !onlyAtRisk || a.State != protectedState)
            .Select(a => new DataAssetLine(
                a.Id,
                a.Where.EnvironmentName,
                a.Name,
                names.GetValueOrDefault(a.AppId) ?? a.AppId,
                a.Kind,
                a.Importance,
                Math.Round(a.MaxAgeSeconds / 3600.0, 1),
                a.State,
                CopyLineOf(a.Local),
                CopyLineOf(a.Offsite)))
            .ToList();
        var sources = resource.Sources.Select(s => new DataSourceLine(s.Tool, s.EnvironmentName, s.Error, s.Jobs, s.CheckedAt)).ToList();

        return new DataDigest(ToCounts(report.Summary), assets.Take(MaxItems).ToList(), sources, assets.Count > MaxItems);
    }

    private static CopyLine? CopyLineOf(DataCopyResource? copy) => copy is null
        ? null
        : new CopyLine(copy.State, copy.Reason, copy.LastSuccessAt, copy.Sources.Select(s => s.JobName ?? s.Reference).ToList());

    private static DateTimeOffset? Newest(DataAssetResource asset) => new[] { asset.Local?.LastSuccessAt, asset.Offsite?.LastSuccessAt }.Max();

    /// <param name="section">all, code (repositories) or images.</param>
    public MaintenanceDigest GetMaintenance(string section)
    {
        var catalog = _catalogService.Current.Catalog;
        var report = _maintenanceService.GetReport();
        var resource = report.ToResource(catalog);
        var names = catalog.Apps.ToDictionary(a => a.Id, a => a.Name, StringComparer.Ordinal);
        var code = section is not "images";
        var images = section is not "code";

        var repositories = code
            ? resource.Repositories.Where(NeedsAttention).Select(r => RepositoryLineOf(r, names)).ToList()
            : [];
        var imageLines = images
            ? resource.Images.Where(i => i.Status is not "up-to-date").Select(ImageLineOf).ToList()
            : [];

        return new MaintenanceDigest(
            ToCounts(report.Summary),
            resource.Attention.Select(a => new AttentionLine(a.Kind, a.Level, a.Count, Cap(a.Subjects.Select(s => s.Label)))).ToList(),
            repositories.Take(MaxItems).ToList(),
            imageLines.Take(MaxItems).ToList(),
            resource.CodeSource.Error,
            repositories.Count > MaxItems || imageLines.Count > MaxItems);
    }

    public static IReadOnlyList<string> Cap(IEnumerable<string> subjects)
    {
        var list = subjects.ToList();
        return list.Count <= MaxSubjects ? list : [.. list.Take(MaxSubjects), $"and {list.Count - MaxSubjects} more"];
    }

    public static double? GiB(double? bytes) => bytes is { } b ? Math.Round(b / BytesPerGiB, 2) : null;

    public static double? Percent(double? value, double? capacity) =>
        value is { } v && capacity is > 0 ? Math.Round(v / capacity.Value * 100, 1) : null;

    private static IEnumerable<Finding> AppFindings(IReadOnlyList<AppView> apps)
    {
        foreach (var (status, level, kind) in new[]
        {
            (HealthStatus.Down, "danger", "app-down"),
            (HealthStatus.Degraded, "warning", "app-degraded"),
            (HealthStatus.Unknown, "info", "app-unknown"),
        })
        {
            var matching = apps.Where(a => a.Status.Status == status).OrderBy(a => a.App.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (matching.Count > 0)
            {
                yield return new Finding("apps", level, kind, matching.Count, Cap(matching.Select(a => $"{a.App.Name} ({StatusText.Of(a.Status)})")));
            }
        }
    }

    private static IEnumerable<Finding> InfrastructureFindings(IReadOnlyList<EnvironmentLine> environments, IReadOnlyList<InfrastructureLine> infrastructure)
    {
        var unreachable = environments.Where(e => e.Connection == "unreachable").ToList();
        if (unreachable.Count > 0)
        {
            yield return new Finding("infrastructure", "danger", "environment-unreachable", unreachable.Count, Cap(unreachable.Select(e => $"{e.Name} ({e.Error ?? "unreachable"})")));
        }

        var notReady = infrastructure.SelectMany(e => e.Machines.Where(m => m.Ready == false).Select(m => $"{m.Name} ({e.Name})")).ToList();
        if (notReady.Count > 0)
        {
            yield return new Finding("infrastructure", "danger", "machine-not-ready", notReady.Count, Cap(notReady));
        }

        var unsampled = infrastructure.Where(e => e.Error is not null).ToList();
        if (unsampled.Count > 0)
        {
            yield return new Finding("infrastructure", "warning", "metrics-unavailable", unsampled.Count, Cap(unsampled.Select(e => $"{e.Name} ({e.Error})")));
        }
    }

    private static IEnumerable<Finding> DataFindings(DataReport report)
    {
        foreach (var (state, level, kind) in new[]
        {
            (CopyState.Failed, "danger", "backup-failed"),
            (CopyState.Missing, "danger", "data-missing"),
            (CopyState.Late, "warning", "backup-late"),
            (CopyState.NotSetUp, "warning", "backup-tool-not-set-up"),
        })
        {
            var assets = report.Assets.Where(a => a.State == state).ToList();
            if (assets.Count > 0)
            {
                yield return new Finding("data", level, kind, assets.Count, Cap(assets.Select(a => $"{a.Asset.Name} ({a.Asset.Importance.ToResource()})")));
            }
        }

        var withoutOffsite = report.Assets.Where(a => a.Asset.Importance == DataImportance.Critical && a.Offsite?.LastSuccessAt is null).ToList();

        // An off-site job that has not run yet is pending, not a gap: the operator waits, or runs it once.
        var pending = withoutOffsite.Where(a => a.Offsite?.State == CopyState.Waiting).ToList();
        var critical = withoutOffsite.Except(pending).ToList();
        if (critical.Count > 0)
        {
            yield return new Finding("data", "warning", "critical-without-offsite", critical.Count, Cap(critical.Select(a => a.Asset.Name)));
        }

        if (pending.Count > 0)
        {
            yield return new Finding("data", "info", "critical-offsite-pending", pending.Count, Cap(pending.Select(a => a.Asset.Name)));
        }
    }

    private static AppCounts CountApps(IReadOnlyList<AppView> apps)
    {
        int Count(HealthStatus status) => apps.Count(a => a.Status.Status == status);
        return new AppCounts(apps.Count, Count(HealthStatus.Healthy), Count(HealthStatus.Degraded), Count(HealthStatus.Down), Count(HealthStatus.Unknown), Count(HealthStatus.Paused), Count(HealthStatus.Maintenance));
    }

    private static DataCounts ToCounts(DataSummary s) => new(s.Followed, s.Protected, s.Late, s.Failed, s.Missing, s.Unknown, s.CriticalAssets, s.CriticalOffsite, s.Waiting, s.NotSetUp, s.CriticalOffsitePending);

    private static MaintenanceCounts ToCounts(MaintenanceSummary s) =>
        new(s.Repositories, s.Critical + s.High, s.SecretAlerts, s.CiFailing, s.DependabotOff, s.Images, s.ImagesBehind, s.ImagesMajor, s.ImagesUnknown);

    private static string Level(AttentionLevel level) => level.ToString().ToLowerInvariant();

    private static int LevelRank(string level) => level switch
    {
        "danger" => 0,
        "warning" => 1,
        _ => 2,
    };

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string query) => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static string? NameOf(Core.Catalog.Catalog catalog, string? environmentId) =>
        environmentId is null ? null : catalog.Environments.FirstOrDefault(e => e.Id == environmentId)?.Name ?? environmentId;

    private static CatalogEnvironment? FindEnvironment(Core.Catalog.Catalog catalog, string idOrName) =>
        catalog.Environments.FirstOrDefault(e => Same(e.Id, idOrName) || Same(e.Name, idOrName));

    private static string? PreferredUrl(IReadOnlyList<AppUrlView> urls)
    {
        var url = urls.FirstOrDefault(u => u.Scope == UrlScope.Public) ?? urls.FirstOrDefault();
        return url is null ? null : SafeUrl.Of(url.Href);
    }

    private static AppWorkloadLine WorkloadLineOf(AppWorkloadView view, Core.Catalog.Catalog catalog, IReadOnlyDictionary<string, EnvironmentMetrics> metrics)
    {
        var workload = view.Workload;
        var environment = catalog.Environments.FirstOrDefault(e => e.Id == view.EnvironmentId);
        var environmentMetrics = metrics.GetValueOrDefault(view.EnvironmentId);
        var machines = environmentMetrics?.Machines ?? [];

        // A Docker environment is one machine: its containers have no node.
        var machine = workload?.Node ?? (machines.Count == 1 ? machines[0].Machine.Id : null);
        var usage = workload is null ? null : environmentMetrics?.Workloads.GetValueOrDefault(workload.Id);

        return new AppWorkloadLine(
            view.EnvironmentId,
            view.Runtime,
            view.Name,
            view.Role.ToString().ToLowerInvariant(),
            workload is not null,
            view.Status.Status.ToString(),
            StatusText.Of(view.Status),
            workload?.State.ToString().ToLowerInvariant(),
            workload?.Health.ToString().ToLowerInvariant(),
            workload?.Namespace,
            machine,
            machine is null || environment is null ? null : NameOf(catalog, environment.HostOf(machine)),
            workload?.Image,
            usage?.CpuCores is { } cpu ? Math.Round(cpu, 3) : null,
            usage is null ? null : GiB(usage.MemoryBytes));
    }

    private static CheckLine? CheckLineOf(AppCheckView? check)
    {
        if (check is null)
        {
            return null;
        }

        var latest = check.State?.Latest;
        return new CheckLine(
            SafeUrl.Of(check.Target),
            check.Definition.Critical,
            check.Status.Status.ToString(),
            StatusText.Of(check.Status),
            latest?.At,
            latest?.StatusCode,
            latest?.Latency is { } latency ? (int)Math.Round(latency.TotalMilliseconds) : null,
            latest?.CertificateExpiresAt);
    }

    private static ImageLine ImageLineOf(DeployedImageResource image) => new(
        image.Reference,
        image.Status,
        image.Current,
        image.Latest,
        image.Change,
        image.Reason,
        Cap(image.Users.Select(u => u.AppIds.Count > 0 ? string.Join(", ", u.AppIds) : $"{u.EnvironmentId}/{u.Name}").Distinct(StringComparer.Ordinal)));

    private static MachineLine MachineLineOf(string environmentId, MachineResource machine, Dictionary<(string, string), WorkloadView> workloads)
    {
        var cpuPoints = machine.History.Select(p => p.CpuCores).OfType<double>().ToList();
        var memoryPoints = machine.History.Select(p => p.MemoryBytes).OfType<long>().Select(b => (double)b).ToList();
        var host = machine.HostMetrics is { Error: null, SampledAt: not null } agent ? agent : null;

        return new MachineLine(
            machine.Id,
            machine.Name,
            machine.Kind,
            machine.Ready,
            machine.Roles,
            host is null ? machine.Scope : "host-agent",
            host is null ? ContainerCpu(machine, cpuPoints) : HostCpu(host),
            host is null ? ContainerMemory(machine, memoryPoints) : HostMemory(host),
            machine.Host?.Name,
            machine.Guests.Select(g => g.Name).ToList(),
            machine.Workloads.Count,
            machine.Workloads
                .Where(w => w.CpuCores is not null || w.MemoryBytes is not null)
                .Take(TopConsumers)
                .Select(w =>
                {
                    var view = workloads.GetValueOrDefault((environmentId, w.Id));
                    return new ConsumerLine(view?.Workload.Name ?? w.Id, view?.AppIds ?? [], Round(w.CpuCores, 3), GiB(w.MemoryBytes));
                })
                .ToList(),
            host is null ? null : new HostAgentLine(host.IowaitPercent, host.CorePercents, host.Load1, host.TemperatureCelsius, GiB(host.SwapUsedBytes), GiB(host.SwapTotalBytes)));
    }

    /// <summary>A machine's CPU as its connector reports it (metrics-server, the sum of its containers), with the lowest and highest of the live window.</summary>
    private static UsageLine ContainerCpu(MachineResource machine, List<double> points) => new(
        Round(machine.CpuCores, 3),
        points.Count > 0 ? Math.Round(points.Min(), 3) : null,
        points.Count > 0 ? Math.Round(points.Max(), 3) : null,
        machine.CpuCapacityCores,
        Percent(machine.CpuCores, machine.CpuCapacityCores));

    private static UsageLine ContainerMemory(MachineResource machine, List<double> points) => new(
        GiB(machine.MemoryBytes),
        points.Count > 0 ? GiB(points.Min()) : null,
        points.Count > 0 ? GiB(points.Max()) : null,
        GiB(machine.MemoryCapacityBytes),
        Percent(machine.MemoryBytes, machine.MemoryCapacityBytes));

    /// <summary>The host agent's CPU (D-72) in cores, from its load in % of the machine.</summary>
    private static UsageLine HostCpu(HostMetricsResource host)
    {
        double? Cores(double? percent) => percent is { } p && host.Cores is { } c ? Math.Round(p / 100 * c, 3) : null;
        var points = host.History.Select(p => p.CpuPercent).OfType<double>().ToList();
        return new UsageLine(
            Cores(host.CpuPercent),
            points.Count > 0 ? Cores(points.Min()) : null,
            points.Count > 0 ? Cores(points.Max()) : null,
            host.Cores,
            host.CpuPercent is { } percent ? Math.Round(percent, 1) : null);
    }

    private static UsageLine HostMemory(HostMetricsResource host)
    {
        var points = host.History.Select(p => (double)p.MemoryUsedBytes).ToList();
        return new UsageLine(
            GiB(host.MemoryUsedBytes),
            points.Count > 0 ? GiB(points.Min()) : null,
            points.Count > 0 ? GiB(points.Max()) : null,
            GiB(host.MemoryTotalBytes),
            Percent(host.MemoryUsedBytes, host.MemoryTotalBytes));
    }

    private static double? Round(double? value, int digits) => value is { } v ? Math.Round(v, digits) : null;

    private static bool NeedsAttention(RepositoryResource r) =>
        r.Dependabot.Total + r.CodeScanning.Total + r.SecretScanning.Total > 0
        || r.Dependabot.State != "on"
        || r.Ci?.Failed == true
        || r.DependabotPullRequests > 0;

    private static RepositoryLine RepositoryLineOf(RepositoryResource r, Dictionary<string, string> names) => new(
        r.FullName,
        r.Deployed,
        r.AppIds.Select(id => names.GetValueOrDefault(id) ?? id).ToList(),
        Describe(r.Dependabot),
        Describe(r.CodeScanning),
        Describe(r.SecretScanning),
        CiState(r.Ci),
        r.DependabotPullRequests,
        r.Release?.Tag);

    private static string? CiState(CiRunResource? ci)
    {
        if (ci is null)
        {
            return null;
        }

        return ci.Failed ? "failing" : ci.Conclusion ?? ci.Status;
    }

    private static string Describe(AlertScanResource scan)
    {
        if (scan.State != "on")
        {
            return scan.Reason is null ? scan.State : $"{scan.State}: {scan.Reason}";
        }

        var parts = new (int Count, string Label)[] { (scan.Critical, "critical"), (scan.High, "high"), (scan.Medium, "medium"), (scan.Low, "low"), (scan.Other, "quality") }
            .Where(p => p.Count > 0)
            .Select(p => $"{p.Count} {p.Label}")
            .ToList();
        return parts.Count == 0 ? "on: none" : $"on: {string.Join(", ", parts)}";
    }
}
