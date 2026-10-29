using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Core.Datastore;
using Portarr.Core.Datastore.Migration;
using Portarr.Core.Icons;
using Portarr.Core.Instrumentation;
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

        services.AddSingleton<CatalogService>();
        services.AddSingleton<ICatalogService>(provider => provider.GetRequiredService<CatalogService>());
        services.AddHostedService(provider => provider.GetRequiredService<CatalogService>());
        services.AddSingleton<WorkloadInventory>();
        services.AddSingleton<IWorkloadInventory>(provider => provider.GetRequiredService<WorkloadInventory>());
        services.AddHostedService(provider => provider.GetRequiredService<WorkloadInventory>());
        services.AddSingleton<IFleetService, FleetService>();

        services.AddHttpClient(IconService.HttpClientName, (provider, client) =>
        {
            client.BaseAddress = provider.GetRequiredService<IOptions<PortarrOptions>>().Value.IconSourceUrl;
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<IIconService, IconService>();

        return services;
    }
}
