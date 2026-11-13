using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Accounts;
using Portarr.Core.Attention;
using Portarr.Core.Backups;
using Portarr.Core.Catalog;
using Portarr.Core.Catalog.Editing;
using Portarr.Core.Checks;
using Portarr.Core.Configuration;
using Portarr.Core.Data;
using Portarr.Core.Datastore;
using Portarr.Core.Datastore.Migration;
using Portarr.Core.Icons;
using Portarr.Core.Infrastructure;
using Portarr.Core.Infrastructure.Gpu;
using Portarr.Core.Instrumentation;
using Portarr.Core.Maintenance;
using Portarr.Core.Mcp;
using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;
using Portarr.Core.Platforms;
using Portarr.Core.Portal;
using Portarr.Core.Status;
using Portarr.Core.SystemInfo;
using Portarr.Core.Workloads;

namespace Portarr.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddPortarrCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PortarrOptions>()
            .Bind(configuration)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PortarrOptions>, PortarrOptionsValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IDatabase, Database>();
        services.AddSingleton<IMigrationController, MigrationController>();
        services.AddSingleton<IConfigRepository, ConfigRepository>();
        services.AddSingleton<ISystemStatusService, SystemStatusService>();
        services.AddSingleton<ILogLevelService, LogLevelService>();

        services.AddSingleton<ICatalogParser, CatalogParserService>();
        services.AddSingleton<CatalogService>();
        services.AddSingleton<ICatalogService>(provider => provider.GetRequiredService<CatalogService>());
        services.AddHostedService(provider => provider.GetRequiredService<CatalogService>());
        services.AddSingleton<ICatalogAuditLog, CatalogAuditLog>();
        services.AddSingleton<ICatalogEditor, CatalogEditor>();
        services.AddSingleton<WorkloadInventory>();
        services.AddSingleton<IWorkloadInventory>(provider => provider.GetRequiredService<WorkloadInventory>());
        services.AddHostedService(provider => provider.GetRequiredService<WorkloadInventory>());
        services.AddSingleton<IFleetService, FleetService>();

        services.AddSingleton<InfrastructureMonitor>();
        services.AddSingleton<IInfrastructureMonitor>(provider => provider.GetRequiredService<InfrastructureMonitor>());
        services.AddHostedService(provider => provider.GetRequiredService<InfrastructureMonitor>());
        services.AddSingleton<HostMetricsMonitor>();
        services.AddSingleton<IHostMetricsMonitor>(provider => provider.GetRequiredService<HostMetricsMonitor>());
        services.AddHostedService(provider => provider.GetRequiredService<HostMetricsMonitor>());

        services.AddSingleton<BackupMonitor>();
        services.AddSingleton<IBackupMonitor>(provider => provider.GetRequiredService<BackupMonitor>());
        services.AddHostedService(provider => provider.GetRequiredService<BackupMonitor>());
        services.AddSingleton<IDataService, DataService>();

        services.AddSingleton<MaintenanceMonitor>();
        services.AddSingleton<IMaintenanceMonitor>(provider => provider.GetRequiredService<MaintenanceMonitor>());
        services.AddHostedService(provider => provider.GetRequiredService<MaintenanceMonitor>());
        services.AddSingleton<IMaintenanceService, MaintenanceService>();
        services.AddSingleton<IAttentionService, AttentionService>();
        services.AddSingleton<IMcpAttentionSource, McpAttentionSource>();

        services.AddSingleton<ICheckStore, CheckStore>();
        services.AddSingleton<ICheckRunner, HttpCheckRunner>();
        services.AddHostedService<CheckScheduler>();

        // The health of the operator's MCP servers (D-114): the server is checked, never the workstation.
        services.AddSingleton<IMcpHealthStore, McpHealthStore>();
        services.AddSingleton<McpCheckRunner>();
        // The platforms the machines run, their newer versions and their support dates (D-119).
        services.AddSingleton<PlatformMonitor>();
        services.AddSingleton<IPlatformKnowledge>(provider => provider.GetRequiredService<PlatformMonitor>());
        services.AddHostedService(provider => provider.GetRequiredService<PlatformMonitor>());
        services.AddSingleton<IPlatformService, PlatformService>();

        // The graphics cards of the machines, and the workloads that have one attached (D-120).
        services.AddSingleton<GpuMonitor>();
        services.AddSingleton<IGpuMonitor>(provider => provider.GetRequiredService<GpuMonitor>());
        services.AddHostedService(provider => provider.GetRequiredService<GpuMonitor>());

        services.AddSingleton<McpHealthMonitor>();
        services.AddSingleton<IMcpHealth>(provider => provider.GetRequiredService<McpHealthMonitor>());
        services.AddHostedService(provider => provider.GetRequiredService<McpHealthMonitor>());
        services.AddSingleton<IMcpClientRegistry, McpClientRegistry>();
        services.AddSingleton<IMcpSecretStoreRegistry, McpSecretStoreRegistry>();
        services.AddSingleton<IMcpVocabulary, McpVocabulary>();
        services.AddSingleton<IMcpRenderingService, McpRenderingService>();
        services.AddSingleton<IMcpServerService, McpServerService>();
        services.AddSingleton<IMcpWorkstationService, McpWorkstationService>();

        // Timeouts are per check (catalog), not per client.
        services.AddHttpClient(HttpCheckRunner.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(HttpCheckRunner.CreateHandler);

        services.AddHttpClient(IconService.HttpClientName, (provider, client) =>
        {
            client.BaseAddress = provider.GetRequiredService<IOptions<PortarrOptions>>().Value.IconSourceUrl;
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = IconService.MaxIconBytes;
        });
        services.AddSingleton<IIconService, IconService>();
        services.AddSingleton<IPortalService, PortalService>();

        // Local accounts, roles and per-user keys (V7).
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddSingleton<AccountStore>();
        services.AddSingleton<IAuthAuditLog, AuthAuditLog>();
        services.AddSingleton<IAccountService>(provider => new AccountService(
            provider.GetRequiredService<AccountStore>(),
            provider.GetRequiredService<IAuthAuditLog>(),
            provider.GetRequiredService<IOptions<AuthOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<AccountService>>()));
        services.AddSingleton<ISetupCode, SetupCode>();
        services.AddSingleton<IAccountBootstrap, AccountBootstrap>();
        services.AddSingleton<ISystemApiKeySource, ApiKeySource>();
        services.AddSingleton<IApiKeyService, ApiKeyService>();
        services.AddSingleton<IChatAccess, ChatAccess>();
        services.AddSingleton<IAvatarService, AvatarService>();

        return services;
    }
}
