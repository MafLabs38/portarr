using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Backups;
using Portarr.Core.Catalog;
using Portarr.Core.Checks;
using Portarr.Core.Data;
using Portarr.Core.Infrastructure;
using Portarr.Core.Maintenance;
using Portarr.Core.Status;
using Portarr.Core.Workloads;
using Portarr.Mcp.Digest;
using Portarr.Mcp.Knowledge;
using Portarr.Test.Common;

namespace Portarr.Mcp.Test;

/// <summary>
/// A small fleet behind the real Core services (catalog parser, fleet and data joins): a hypervisor, a VM on it
/// and a cluster whose node is a VM on it too. Only the connectors' memory (inventory, metrics) is substituted.
/// </summary>
internal sealed class FleetBuilder
{
    public static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    public const string Catalog = """
        environments:
          - { id: hypervisor, name: Hypervisor, address: 10.0.0.10, docker: { url: "http://10.0.0.10:2375" }, access: { ssh: { alias: hypervisor, user: root } } }
          - { id: vm-1, name: VM 1, host: hypervisor, docker: { url: "http://10.0.0.11:2375" } }
          - id: cluster
            name: Cluster
            kubernetes: { server: "https://10.0.0.20:6443", tokenEnv: K8S_TOKEN, nodeHosts: { node-1: hypervisor } }
            notes: Two nodes, one at home and one at a hosting provider.
            machines:
              - name: node-2
                notes: The VPS.
                access: { ssh: { host: vps.homelab.example, port: 2222, user: ubuntu, key: ~/.ssh/vps } }
        links:
          - { id: tunnel, from: node-1, to: node-2, kind: wireguard-tunnel, network: 10.99.0.0/24, notes: node-1 initiates. }
        providers:
          - { id: dns, name: Example DNS, kind: dns, console: "https://dns.example/manager?session=abc", usedBy: [sonarr], notes: The zone homelab.example. }
        stacks:
          - id: media
            name: Media
            members:
              - { app: sonarr, role: critical }
        apps:
          - id: sonarr
            name: Sonarr
            urls: [{ label: Public, href: "https://sonarr.homelab.example/?apikey=s3cr3t#top" }]
            workloads: [{ runtime: docker@vm-1, match: { name: sonarr } }]
          - id: shop
            name: Shop
            workloads: [{ runtime: kubernetes@cluster, match: { namespace: shop, app: shop } }]
        data:
          - id: sonarr-config
            name: Sonarr config
            app: sonarr
            kind: sqlite-app-config
            importance: critical
            where: { environment: vm-1, workload: sonarr, path: /srv/sonarr }
            targets:
              local: { backrest: { plan: sonarr-local } }
              offsite: {}
        """;

    public Dictionary<string, EnvironmentInventory> Inventory { get; } = [];

    /// <summary>What the backup tools said at their last read (none by default: every tool is not configured).</summary>
    public BackupMonitorState Backups { get; set; } = BackupMonitorState.Empty;

    /// <summary>The catalog the fleet is built from; a test replaces a part of <see cref="Catalog"/>.</summary>
    public string CatalogText { get; set; } = Catalog;

    public Dictionary<string, EnvironmentMetrics> Metrics { get; } = [];

    public FleetDigest Digest => Build().Digest;

    public KnowledgeBase Knowledge => Build().Knowledge;

    public static Workload Container(string environment, string name, WorkloadState state = WorkloadState.Running, params WorkloadMount[] mounts) =>
        new(environment, name, name, $"example/{name}:1", state, WorkloadHealth.None, state == WorkloadState.Running ? "Up 1 hour" : "Exited (1) 2 minutes ago", Now, null,
            new Dictionary<string, string> { ["secret.label"] = "do-not-show" }, Mounts: mounts);

    public static Workload Pod(string name) =>
        new("cluster", $"shop/{name}", name, "ghcr.io/example/shop:2", WorkloadState.Running, WorkloadHealth.None, "", Now, null,
            new Dictionary<string, string> { ["app"] = "shop" }, Namespace: "shop", Node: "node-1");

    public FleetBuilder With(string environment, params Workload[] workloads)
    {
        Inventory[environment] = new EnvironmentInventory(environment, true, null, workloads, Now);
        return this;
    }

    public FleetBuilder Unreachable(string environment)
    {
        Inventory[environment] = new EnvironmentInventory(environment, false, "timeout", [], Now);
        return this;
    }

    private (FleetDigest Digest, KnowledgeBase Knowledge) Build()
    {
        var parsed = TestCatalog.Parse(CatalogText);
        parsed.Errors.Should().BeEmpty();
        var catalogService = Substitute.For<ICatalogService>();
        catalogService.Current.Returns(new CatalogState(parsed.Catalog, "/config/catalog.yaml", true, [], Now));
        var inventory = Substitute.For<IWorkloadInventory>();
        inventory.Current.Returns(Inventory);
        var monitor = Substitute.For<IInfrastructureMonitor>();
        monitor.Current.Returns(Metrics);
        var hostMonitor = Substitute.For<IHostMetricsMonitor>();
        hostMonitor.Current.Returns(new Dictionary<HostMachineKey, HostMetricsView>());
        var clock = new FakeTimeProvider(Now);
        var fleet = new FleetService(catalogService, inventory, Substitute.For<ICheckStore>(), clock);

        var data = Substitute.For<IDataService>();
        data.GetReport().Returns(_ => DataService.Build(parsed.Catalog, Backups, Now));
        var maintenance = Substitute.For<IMaintenanceService>();
        maintenance.GetReport().Returns(new MaintenanceReport(
            new MaintenanceSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), [], false, RepositoryScan.NotConfigured, [], [], MaintenanceRefresh.None));

        var source = Substitute.For<IWorkloadSource>();
        source.Supports(Arg.Any<CatalogEnvironment>()).Returns(call => call.Arg<CatalogEnvironment>().Runtime != RuntimeNames.None);

        var digest = new FleetDigest(catalogService, fleet, inventory, monitor, hostMonitor, data, maintenance, [source], clock);
        return (digest, new KnowledgeBase(digest, catalogService, fleet));
    }
}
