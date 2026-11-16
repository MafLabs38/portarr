using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Proxmox;

public static class ProxmoxServiceCollectionExtensions
{
    public static IServiceCollection AddProxmoxProvider(this IServiceCollection services)
    {
        services.AddSingleton<ProxmoxHttpClientFactory>();
        services.AddSingleton<ProxmoxClient>();
        services.AddSingleton<ProxmoxSource>();
        services.AddSingleton<IWorkloadSource>(provider => provider.GetRequiredService<ProxmoxSource>());
        services.AddSingleton<IInfrastructureSource>(provider => provider.GetRequiredService<ProxmoxSource>());
        services.AddSingleton<IEnvironmentConnector, ProxmoxEnvironmentConnector>();

        return services;
    }
}
