using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Backups;
using Portarr.Core.Catalog;

namespace Portarr.Providers.Backrest;

public static class BackrestServiceCollectionExtensions
{
    /// <summary>Largest answer read (a configuration, or 200 operations of a plan).</summary>
    private const int MaxResponseBytes = 16 * 1024 * 1024;

    public static IServiceCollection AddBackrestProvider(this IServiceCollection services)
    {
        services.AddHttpClient(BackrestBackupSource.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(20);
                client.MaxResponseContentBufferSize = MaxResponseBytes;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Portarr");
            })
            // The credentials go to the address the catalog names, never to wherever a redirect points.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(10) });
        services.AddSingleton<IBackupSource, BackrestBackupSource>();
        services.AddSingleton<IEnvironmentConnector, BackrestEnvironmentConnector>();

        return services;
    }
}
