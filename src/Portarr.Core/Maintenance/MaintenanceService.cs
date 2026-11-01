using System.Text.RegularExpressions;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Core.Maintenance;

/// <summary>
/// A repository with the catalog apps and the running images built from it: <c>Deployed</c> when either links
/// to it. A repository without any is code the operator makes but does not run here (a desktop app, a library).
/// </summary>
public sealed record RepositoryView(CodeRepository Repository, IReadOnlyList<string> AppIds, IReadOnlyList<string> Images, bool Deployed);

/// <summary>A running workload of an image: where it runs and the apps it belongs to.</summary>
public sealed record ImageUser(string EnvironmentId, string Name, string? Namespace, IReadOnlyList<string> AppIds);

/// <summary>
/// One image as it runs in the fleet: every running workload created from the same reference with the same
/// verdict, so twenty socket proxies make one row. <c>RepositoryFullName</c> is the scanned repository the
/// image is built from, when it is one of the operator's.
/// </summary>
public sealed record DeployedImageView(ImageReference Image, ImageCheck Check, IReadOnlyList<ImageUser> Users, string? RepositoryFullName);

public enum AttentionLevel
{
    Danger,
    Warning,
    Info,
}

/// <summary>One thing an attention entry names: a repository or an image, with its count when it has several.</summary>
public sealed record AttentionSubject(string Label, Uri? Url, int? Count = null);

/// <summary>
/// "What needs attention" (D-67): one entry per kind of finding, worst first, naming what it concerns.
/// <c>Kind</c>: severe-alerts, secret-alerts, ci-failing, image-major, images-behind, image-rebuilt,
/// dependabot-off, code-scanning-off, other-alerts, dependabot-prs, images-unknown, code-source-error.
/// </summary>
public sealed record AttentionItem(string Kind, AttentionLevel Level, int Count, IReadOnlyList<AttentionSubject> Subjects);

public sealed record MaintenanceSummary(
    int Repositories,
    int DeployedRepositories,
    int Critical,
    int High,
    int Medium,
    int Low,
    int QualityAlerts,
    int SecretAlerts,
    int DependabotOff,
    int CodeScanningOff,
    int CiFailing,
    int DependabotPullRequests,
    int Images,
    int ImagesUpToDate,
    int ImagesBehind,
    int ImagesMajor,
    int ImagesFloating,
    int ImagesUnknown);

public sealed record MaintenanceReport(
    MaintenanceSummary Summary,
    IReadOnlyList<AttentionItem> Attention,
    bool CodeSourceConfigured,
    RepositoryScan Scan,
    IReadOnlyList<RepositoryView> Repositories,
    IReadOnlyList<DeployedImageView> Images,
    MaintenanceRefresh Refresh);

public interface IMaintenanceService
{
    MaintenanceReport GetReport();
}

/// <summary>
/// Joins the repository scan, the running images and their registry lookups with the catalog (D-64 to D-67).
/// Built from memory only: a request reaches no connector.
/// </summary>
public sealed class MaintenanceService : IMaintenanceService
{
    private readonly ICatalogService _catalogService;
    private readonly IWorkloadInventory _inventory;
    private readonly IMaintenanceMonitor _monitor;
    private readonly IReadOnlyList<ICodeRepositorySource> _codeSources;

    public MaintenanceService(ICatalogService catalogService, IWorkloadInventory inventory, IMaintenanceMonitor monitor, IEnumerable<ICodeRepositorySource> codeSources)
    {
        _catalogService = catalogService;
        _inventory = inventory;
        _monitor = monitor;
        _codeSources = codeSources.ToList();
    }

    public MaintenanceReport GetReport()
    {
        return Build(_catalogService.Current.Catalog, _inventory.Current, _monitor.Current, _monitor.Refresh, _codeSources);
    }

    public static MaintenanceReport Build(Catalog.Catalog catalog, IReadOnlyDictionary<string, EnvironmentInventory> inventory, MaintenanceState state, MaintenanceRefresh? refresh = null, IReadOnlyList<ICodeRepositorySource>? codeSources = null)
    {
        var scanned = state.Repositories.Repositories.ToDictionary(r => r.FullName, StringComparer.OrdinalIgnoreCase);
        var appsOf = AppsOf(catalog);

        var images = RunningImages(inventory, state, scanned, appsOf, codeSources ?? []);

        // A repository is linked to an app by the catalog (repository:), or by an image built from it.
        var links = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in catalog.Apps.Where(a => a.Repository is not null))
        {
            Link(links, app.Repository!).Add(app.Id);
        }

        foreach (var image in images.Where(i => i.RepositoryFullName is not null))
        {
            Link(links, image.RepositoryFullName!).UnionWith(image.Users.SelectMany(u => u.AppIds));
        }

        var built = images.Where(i => i.RepositoryFullName is not null)
            .ToLookup(i => i.RepositoryFullName!, i => i.Image.Display, StringComparer.OrdinalIgnoreCase);
        var repositories = state.Repositories.Repositories
            .Select(repository => links.TryGetValue(repository.FullName, out var apps)
                ? new RepositoryView(repository, apps.Order(StringComparer.Ordinal).ToList(), built[repository.FullName].Distinct(StringComparer.Ordinal).ToList(), true)
                : new RepositoryView(repository, [], [], false))
            .OrderByDescending(view => Weight(view.Repository))
            .ThenByDescending(view => view.Repository.PushedAt)
            .ToList();

        return new MaintenanceReport(
            Summarize(repositories, images),
            Attention(repositories, images, state.Repositories),
            catalog.CodeSources.Count > 0,
            state.Repositories,
            repositories,
            images,
            refresh ?? MaintenanceRefresh.None);
    }

    private static HashSet<string> Link(Dictionary<string, HashSet<string>> links, string repository)
    {
        if (!links.TryGetValue(repository, out var apps))
        {
            apps = new HashSet<string>(StringComparer.Ordinal);
            links[repository] = apps;
        }

        return apps;
    }

    private static Func<Workload, IReadOnlyList<string>> AppsOf(Catalog.Catalog catalog)
    {
        return workload => catalog.Apps
            .Where(app => app.Workloads.Any(selector => selector.Matches(workload)))
            .Select(app => app.Id)
            .ToList();
    }

    private static List<DeployedImageView> RunningImages(
        IReadOnlyDictionary<string, EnvironmentInventory> inventory,
        MaintenanceState state,
        Dictionary<string, CodeRepository> scanned,
        Func<Workload, IReadOnlyList<string>> appsOf,
        IReadOnlyList<ICodeRepositorySource> codeSources)
    {
        return MaintenanceMonitor.RunningImages(inventory)
            .Select(pair =>
            {
                var (workload, image) = pair;
                var label = workload.Labels.GetValueOrDefault(ImageCheck.VersionLabel);
                var check = ImageCheck.Evaluate(image, label, workload.ImageDigest, state.Registries.GetValueOrDefault(image.RepositoryKey));
                return (Workload: workload, Image: image, Check: check, Repository: SourceRepository(workload, image, scanned, codeSources));
            })
            .GroupBy(entry => (entry.Image.Display, entry.Check))
            .Select(group => new DeployedImageView(
                group.First().Image,
                group.Key.Check,
                group.Select(e => new ImageUser(e.Workload.EnvironmentId, e.Workload.Name, e.Workload.Namespace, appsOf(e.Workload)))
                    .OrderBy(u => u.EnvironmentId, StringComparer.Ordinal)
                    .ThenBy(u => u.Name, StringComparer.Ordinal)
                    .ToList(),
                group.Select(e => e.Repository).FirstOrDefault(r => r is not null)))
            .OrderBy(view => Rank(view.Check))
            .ThenBy(view => view.Image.Display, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The scanned repository an image is built from: what its OCI source label names, else what its registry name says,
    /// each in the code host's own terms (the connector knows what its repository addresses and image names look like).
    /// </summary>
    private static string? SourceRepository(Workload workload, ImageReference image, Dictionary<string, CodeRepository> scanned, IReadOnlyList<ICodeRepositorySource> codeSources)
    {
        if (workload.Labels.GetValueOrDefault(ImageCheck.SourceLabel) is { } label
            && codeSources.Select(source => source.RepositoryOfSourceLabel(label)).FirstOrDefault(name => name is not null) is { } labelled
            && scanned.TryGetValue(labelled, out var fromLabel))
        {
            return fromLabel.FullName;
        }

        return codeSources.Select(source => source.RepositoryOfImage(image)).FirstOrDefault(name => name is not null) is { } named
            && scanned.TryGetValue(named, out var fromName) ? fromName.FullName : null;
    }

    private static int Rank(ImageCheck check) => check.Status switch
    {
        ImageStatus.Behind => check.Change switch
        {
            VersionChange.Major => 0,
            VersionChange.Minor => 1,
            _ => 2,
        },
        ImageStatus.NewerBuild => 3,
        ImageStatus.Floating => 4,
        ImageStatus.Unknown => 5,
        ImageStatus.Pending => 6,
        _ => 7,
    };

    /// <summary>Repositories with the most pressing findings first: secrets, severe alerts, failing CI, any alert.</summary>
    private static int Weight(CodeRepository repository)
    {
        return (repository.SecretScanning.Counts.Total * 10_000)
            + ((repository.Dependabot.Counts.Severe + repository.CodeScanning.Counts.Severe) * 1_000)
            + (repository.Ci?.Failed == true ? 500 : 0)
            + repository.Dependabot.Counts.Total + repository.CodeScanning.Counts.Total - repository.CodeScanning.Counts.Other;
    }

    private static MaintenanceSummary Summarize(List<RepositoryView> repositories, List<DeployedImageView> images)
    {
        var code = repositories.Select(r => r.Repository).ToList();
        int Sum(Func<AlertCounts, int> pick) => code.Sum(r => pick(r.Dependabot.Counts) + pick(r.CodeScanning.Counts));

        return new MaintenanceSummary(
            code.Count,
            repositories.Count(r => r.Deployed),
            Sum(c => c.Critical),
            Sum(c => c.High),
            Sum(c => c.Medium),
            Sum(c => c.Low),
            code.Sum(r => r.CodeScanning.Counts.Other),
            code.Sum(r => r.SecretScanning.Counts.Total),
            code.Count(r => r.Dependabot.State == ScanningState.Off),
            code.Count(r => r.CodeScanning.State == ScanningState.Off),
            code.Count(r => r.Ci?.Failed == true),
            code.Sum(r => r.DependabotPullRequests ?? 0),
            images.Count,
            images.Count(i => i.Check.Status == ImageStatus.UpToDate),
            images.Count(i => i.Check.Status is ImageStatus.Behind or ImageStatus.NewerBuild),
            images.Count(i => i.Check is { Status: ImageStatus.Behind, Change: VersionChange.Major }),
            images.Count(i => i.Check.Status == ImageStatus.Floating),
            images.Count(i => i.Check.Status is ImageStatus.Unknown or ImageStatus.Pending));
    }

    private static List<AttentionItem> Attention(List<RepositoryView> repositories, List<DeployedImageView> images, RepositoryScan scan)
    {
        var code = repositories.Select(r => r.Repository).ToList();
        var items = new List<AttentionItem>();

        void Add(string kind, AttentionLevel level, IReadOnlyList<AttentionSubject> subjects, int? count = null)
        {
            if (subjects.Count > 0)
            {
                items.Add(new AttentionItem(kind, level, count ?? subjects.Count, subjects));
            }
        }

        static AttentionSubject Repo(CodeRepository repository, int? count = null) => new(repository.FullName, repository.Url, count);

        // An image created from an id has no name worth showing: its workload's is.
        static AttentionSubject Image(DeployedImageView image) => new(image.Image.IsImageId ? image.Users[0].Name : image.Image.Display, null);

        if (scan.Error is not null)
        {
            items.Add(new AttentionItem("code-source-error", AttentionLevel.Warning, 1, [new AttentionSubject(scan.Error, null)]));
        }

        var severe = code.Where(r => r.Dependabot.Counts.Severe + r.CodeScanning.Counts.Severe > 0).ToList();
        Add("severe-alerts", AttentionLevel.Danger, severe.Select(r => Repo(r, r.Dependabot.Counts.Severe + r.CodeScanning.Counts.Severe)).ToList(), severe.Sum(r => r.Dependabot.Counts.Severe + r.CodeScanning.Counts.Severe));
        var secrets = code.Where(r => r.SecretScanning.Counts.Total > 0).ToList();
        Add("secret-alerts", AttentionLevel.Danger, secrets.Select(r => Repo(r, r.SecretScanning.Counts.Total)).ToList(), secrets.Sum(r => r.SecretScanning.Counts.Total));
        Add("ci-failing", AttentionLevel.Warning, code.Where(r => r.Ci?.Failed == true).Select(r => new AttentionSubject(r.FullName, r.Ci!.Url ?? r.Url)).ToList());
        Add("image-major", AttentionLevel.Warning, images.Where(i => i.Check is { Status: ImageStatus.Behind, Change: VersionChange.Major }).Select(Image).ToList());
        Add("images-behind", AttentionLevel.Info, images.Where(i => i.Check is { Status: ImageStatus.Behind, Change: not VersionChange.Major }).Select(Image).ToList());
        Add("image-rebuilt", AttentionLevel.Info, images.Where(i => i.Check.Status == ImageStatus.NewerBuild).Select(Image).ToList());

        var other = code.Where(r => r.Dependabot.Counts.Total - r.Dependabot.Counts.Severe + r.CodeScanning.Counts.Total - r.CodeScanning.Counts.Severe > 0).ToList();
        Add("other-alerts", AttentionLevel.Info, other.Select(r => Repo(r, r.Dependabot.Counts.Total - r.Dependabot.Counts.Severe + r.CodeScanning.Counts.Total - r.CodeScanning.Counts.Severe)).ToList(), other.Sum(r => r.Dependabot.Counts.Total - r.Dependabot.Counts.Severe + r.CodeScanning.Counts.Total - r.CodeScanning.Counts.Severe));
        Add("dependabot-off", AttentionLevel.Warning, code.Where(r => r.Dependabot.State == ScanningState.Off).Select(r => Repo(r)).ToList());
        Add("code-scanning-off", AttentionLevel.Info, code.Where(r => r.CodeScanning.State == ScanningState.Off).Select(r => Repo(r)).ToList());
        var pulls = code.Where(r => r.DependabotPullRequests > 0).ToList();
        Add("dependabot-prs", AttentionLevel.Info, pulls.Select(r => new AttentionSubject(r.FullName, r.DependabotPullsUrl ?? r.Url, r.DependabotPullRequests)).ToList(), pulls.Sum(r => r.DependabotPullRequests ?? 0));
        Add("images-unknown", AttentionLevel.Info, images.Where(i => i.Check.Status == ImageStatus.Unknown).Select(Image).ToList());

        return items.OrderBy(item => item.Level).ToList();
    }

}
