using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure.Gpu;

namespace Portarr.Providers.GpuExporter;

public static class GpuExporterServiceCollectionExtensions
{
    public static IServiceCollection AddGpuExporterProvider(this IServiceCollection services)
    {
        services.AddHttpClient(GpuExporterSource.HttpClientName, client =>
        {
            // The exporter asks the driver on every read: allow it a little longer than node_exporter.
            client.Timeout = TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        });
        services.AddSingleton<IGpuSource, GpuExporterSource>();
        services.AddSingleton<IEnvironmentConnector, GpuExporterEnvironmentConnector>();

        return services;
    }
}
