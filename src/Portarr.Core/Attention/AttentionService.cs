using Portarr.Core.Catalog;
using Portarr.Core.Data;
using Portarr.Core.Infrastructure;
using Portarr.Core.Maintenance;
using Portarr.Core.Status;
using Portarr.Core.Workloads;

namespace Portarr.Core.Attention;

/// <summary>
/// One entry of "what needs attention" across the whole fleet: the area it comes from (apps, infrastructure, data,
/// maintenance, catalog), how bad it is, which kind of finding it is, how many things it concerns and their names (all
/// of them: whoever shows the list decides how many to name).
/// </summary>
public sealed record AttentionFinding(string Area, AttentionLevel Level, string Kind, int Count, IReadOnlyList<string> Subjects);

/// <summary>
/// The cross-domain "needs attention" classification, in one place for every consumer (the MCP overview today, the
/// Overview page and notifications later) instead of one copy per surface. It only reads what the pages already show,
/// from memory; the findings of one area keep the kinds and levels that area defines (maintenance has its own, D-67).
/// </summary>
public interface IAttentionService
{
    /// <summary>The findings, worst first (danger, warning, info), in a stable order inside a level.</summary>
    IReadOnlyList<AttentionFinding> GetFindings();
}

public sealed class AttentionService : IAttentionService
{
    private readonly ICatalogService _catalogService;
    private readonly IFleetService _fleetService;
    private readonly IWorkloadInventory _inventory;
    private readonly IEnumerable<IWorkloadSource> _sources;
    private readonly IInfrastructureMonitor _monitor;
    private readonly IDataService _dataService;
    private readonly IMaintenanceService _maintenanceService;

    public AttentionService(
        ICatalogService catalogService,
        IFleetService fleetService,
        IWorkloadInventory inventory,
        IEnumerable<IWorkloadSource> sources,
        IInfrastructureMonitor monitor,
        IDataService dataService,
        IMaintenanceService maintenanceService)
    {
        _catalogService = catalogService;
        _fleetService = fleetService;
        _inventory = inventory;
        _sources = sources;
        _monitor = monitor;
        _dataService = dataService;
        _maintenanceService = maintenanceService;
    }

    public IReadOnlyList<AttentionFinding> GetFindings()
    {
        var state = _catalogService.Current;
        var findings = new List<AttentionFinding>();
        findings.AddRange(AppFindings(_fleetService.GetApps()));
        findings.AddRange(InfrastructureFindings(state.Catalog));
        findings.AddRange(DataFindings(_dataService.GetReport()));
        findings.AddRange(_maintenanceService.GetReport().Attention.Select(a => new AttentionFinding("maintenance", a.Level, a.Kind, a.Count, a.Subjects.Select(s => s.Label).ToList())));
        if (state.Errors.Count > 0)
        {
            findings.Add(new AttentionFinding("catalog", AttentionLevel.Warning, "catalog-errors", state.Errors.Count, []));
        }

        return findings.OrderBy(f => f.Level).ToList();
    }

    private static IEnumerable<AttentionFinding> AppFindings(IReadOnlyList<AppView> apps)
    {
        foreach (var (status, level, kind) in new[]
        {
            (HealthStatus.Down, AttentionLevel.Danger, "app-down"),
            (HealthStatus.Degraded, AttentionLevel.Warning, "app-degraded"),
            (HealthStatus.Unknown, AttentionLevel.Info, "app-unknown"),
        })
        {
            var matching = apps.Where(a => a.Status.Status == status).OrderBy(a => a.App.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (matching.Count > 0)
            {
                yield return new AttentionFinding("apps", level, kind, matching.Count, matching.Select(a => $"{a.App.Name} ({StatusText.Of(a.Status)})").ToList());
            }
        }
    }

    private IEnumerable<AttentionFinding> InfrastructureFindings(Catalog.Catalog catalog)
    {
        var inventory = _inventory.Current;
        var metrics = _monitor.Current;

        var unreachable = catalog.Environments
            .Select(e => (Environment: e, Current: inventory.GetValueOrDefault(e.Id)))
            .Where(x => EnvironmentConnection.Of(_sources.Any(s => s.Supports(x.Environment)), x.Current) == "unreachable")
            .ToList();
        if (unreachable.Count > 0)
        {
            yield return new AttentionFinding("infrastructure", AttentionLevel.Danger, "environment-unreachable", unreachable.Count, unreachable.Select(x => $"{x.Environment.Name} ({x.Current?.Error ?? "unreachable"})").ToList());
        }

        var notReady = catalog.Environments
            .SelectMany(e => (metrics.GetValueOrDefault(e.Id)?.Machines ?? []).Where(m => m.Machine.Ready == false).Select(m => $"{m.Machine.Name} ({e.Name})"))
            .ToList();
        if (notReady.Count > 0)
        {
            yield return new AttentionFinding("infrastructure", AttentionLevel.Danger, "machine-not-ready", notReady.Count, notReady);
        }

        var unsampled = catalog.Environments.Where(e => metrics.GetValueOrDefault(e.Id)?.Error is not null).ToList();
        if (unsampled.Count > 0)
        {
            yield return new AttentionFinding("infrastructure", AttentionLevel.Warning, "metrics-unavailable", unsampled.Count, unsampled.Select(e => $"{e.Name} ({metrics[e.Id].Error})").ToList());
        }
    }

    private static IEnumerable<AttentionFinding> DataFindings(DataReport report)
    {
        foreach (var (state, level, kind) in new[]
        {
            (CopyState.Failed, AttentionLevel.Danger, "backup-failed"),
            (CopyState.Missing, AttentionLevel.Danger, "data-missing"),
            (CopyState.Late, AttentionLevel.Warning, "backup-late"),
            (CopyState.NotSetUp, AttentionLevel.Warning, "backup-tool-not-set-up"),
        })
        {
            var assets = report.Assets.Where(a => a.State == state).ToList();
            if (assets.Count > 0)
            {
                yield return new AttentionFinding("data", level, kind, assets.Count, assets.Select(a => $"{a.Asset.Name} ({ImportanceText(a.Asset.Importance)})").ToList());
            }
        }

        var withoutOffsite = report.Assets.Where(a => a.Asset.Importance == DataImportance.Critical && a.Offsite?.LastSuccessAt is null).ToList();

        // An off-site job that has not run yet is pending, not a gap: the operator waits, or runs it once.
        var pending = withoutOffsite.Where(a => a.Offsite?.State == CopyState.Waiting).ToList();
        var critical = withoutOffsite.Except(pending).ToList();
        if (critical.Count > 0)
        {
            yield return new AttentionFinding("data", AttentionLevel.Warning, "critical-without-offsite", critical.Count, critical.Select(a => a.Asset.Name).ToList());
        }

        if (pending.Count > 0)
        {
            yield return new AttentionFinding("data", AttentionLevel.Info, "critical-offsite-pending", pending.Count, pending.Select(a => a.Asset.Name).ToList());
        }
    }

    private static string ImportanceText(DataImportance importance) => importance.ToString().ToLowerInvariant();
}
