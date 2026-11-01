using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Portarr.Core.Accounts;
using Portarr.Core.Catalog;
using Portarr.Mcp.Digest;
using Portarr.Mcp.Knowledge;

namespace Portarr.Mcp.Server;

/// <summary>
/// The read tools of Portarr's MCP server (V5, D-68). Every tool is read-only, idempotent and closed-world: it reads
/// Portarr's memory and reaches no connector. None can act on the infrastructure (D-31); the catalog write tools
/// are separate and off by default (<see cref="CatalogTools"/>, D-86).
/// </summary>
[McpServerToolType]
public sealed class PortarrTools
{
    private readonly FleetDigest _digest;
    private readonly ICatalogService _catalogService;

    public PortarrTools(FleetDigest digest, ICatalogService catalogService)
    {
        _digest = digest;
        _catalogService = catalogService;
    }

    [RequiresPermissions(Permissions.AppsView, Permissions.InfrastructureView, Permissions.DataView, Permissions.MaintenanceView)]
    [McpServerTool(Name = "get_overview", Title = "Overview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Start here. What needs attention across apps, infrastructure, data and maintenance, worst first (danger, warning, info), with app counts, environments and their connection, the busiest machines and data and maintenance counts.")]
    public Overview GetOverview()
    {
        return _digest.GetOverview();
    }

    [RequiresPermissions(Permissions.AppsView)]
    [McpServerTool(Name = "list_apps", Title = "List apps", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("The apps of the catalog with their status and reason, stack, role, environments and main URL, worst status first. Filters are optional and combine.")]
    public IReadOnlyList<AppLine> ListApps(
        [Description("Healthy, Degraded, Down, Unknown, Paused or Maintenance.")] string? status = null,
        [Description("A stack id or name.")] string? stack = null,
        [Description("An environment id or name the app runs in.")] string? environment = null)
    {
        return _digest.ListApps(status, stack, environment);
    }

    [RequiresPermissions(Permissions.AppsView)]
    [McpServerTool(Name = "get_app", Title = "App details", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("One app: status and why, URLs to reach it, its workloads and where they run (environment, machine, hypervisor) with their usage, its HTTP check, its data assets and their backup coverage, its images and their update verdict, its code repository.")]
    public AppDetail GetApp([Description("The app id or name (case-insensitive).")] string app)
    {
        var detail = _digest.GetApp(app);
        if (detail is not null)
        {
            return detail;
        }

        var suggestions = _digest.SuggestApps(app);
        throw new McpException(suggestions.Count > 0
            ? $"No app '{app}'. Did you mean: {string.Join(", ", suggestions)}?"
            : $"No app '{app}'. Call list_apps to see the apps.");
    }

    [RequiresPermissions(Permissions.InfrastructureView)]
    [McpServerTool(Name = "get_infrastructure", Title = "Infrastructure", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Machines per environment: capacity, CPU (cores) and memory (GiB) now with the lowest and highest values of the last 10 minutes, the hypervisor a VM runs on, the VMs a hypervisor hosts, and the top consumers (workloads and their apps).")]
    public IReadOnlyList<InfrastructureLine> GetInfrastructure([Description("An environment id or name; omit for every environment.")] string? environment = null)
    {
        return _digest.GetInfrastructure(environment);
    }

    [RequiresPermissions(Permissions.InfrastructureView)]
    [McpServerTool(Name = "find_workload", Title = "Find workloads", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Containers and pods whose name, image, namespace or app contains the text: environment, machine, image, state, status and reason, apps. Worst status first, at most 50.")]
    public IReadOnlyList<WorkloadMatch> FindWorkload([Description("Text to look for, e.g. postgres, media, ghcr.io/example.")] string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200)
        {
            throw new McpException("The query must be 1 to 200 characters.");
        }

        return _digest.FindWorkloads(query.Trim());
    }

    [RequiresPermissions(Permissions.DataView)]
    [McpServerTool(Name = "get_data_coverage", Title = "Data and backups", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("The data the operator follows (databases, volumes, app files) with their importance, the state of their local and off-site copies (at risk first) and the backup tools read.")]
    public DataDigest GetDataCoverage([Description("Only the assets that are not protected (unknown, late, failed or missing).")] bool onlyAtRisk = false)
    {
        return _digest.GetDataCoverage(onlyAtRisk);
    }

    [RequiresPermissions(Permissions.MaintenanceView)]
    [McpServerTool(Name = "get_maintenance", Title = "Maintenance", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("What is vulnerable, outdated or late: the attention list, repositories with alerts, scanning off, CI failing or Dependabot pull requests waiting, and running images that are not up to date.")]
    public MaintenanceDigest GetMaintenance([Description("all, code (repositories) or images.")] string section = "all")
    {
        if (section is not ("all" or "code" or "images"))
        {
            throw new McpException("section must be all, code or images.");
        }

        return _digest.GetMaintenance(section);
    }

    [RequiresPermissions(Permissions.InfrastructureView)]
    [McpServerTool(Name = "get_environment_knowledge", Title = "Environment knowledge", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("What the operator wrote down about the fleet: notes on environments, machines, apps and stacks; how to reach each machine (SSH alias, host, port, user, key file name, jump host, web consoles; never a secret); network links between environments and machines (WireGuard tunnels, VPNs, LANs) with their address range; the external providers in use (CDN, DNS, registrar, VPS, storage) and what depends on them. Optionally for one environment: its machines, the links touching it, the providers it or its apps use.")]
    public EnvironmentKnowledge GetEnvironmentKnowledge([Description("An environment id or name; omit for the whole fleet.")] string? environment = null)
    {
        var catalog = _catalogService.Current.Catalog;
        if (environment is not null && !catalog.Environments.Any(e => string.Equals(e.Id, environment, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Name, environment, StringComparison.OrdinalIgnoreCase)))
        {
            throw new McpException($"No environment '{environment}'. Known: {string.Join(", ", catalog.Environments.Select(e => e.Id))}.");
        }

        return EnvironmentKnowledgeBuilder.Build(catalog, environment);
    }
}
