using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.NodeExporter;

public static class NodeExporterServiceCollectionExtensions
{
    public static IServiceCollection AddNodeExporterProvider(this IServiceCollection services)
    {
        services.AddHttpClient(NodeExporterHostMetricsSource.HttpClientName, client =>
        {
            // Read every second: an answer later than this is a missed point, not a slow one.
            client.Timeout = TimeSpan.FromSeconds(3);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        });
        services.AddSingleton<IHostMetricsSource, NodeExporterHostMetricsSource>();
        services.AddSingleton<IEnvironmentConnector, NodeExporterEnvironmentConnector>();
        services.AddSingleton<IEnvironmentConnector, MachineEnvironmentConnector>();
        services.AddSingleton<MachineSource>();
        services.AddSingleton<IWorkloadSource>(provider => provider.GetRequiredService<MachineSource>());
        services.AddSingleton<IInfrastructureSource>(provider => provider.GetRequiredService<MachineSource>());

        return services;
    }
}
