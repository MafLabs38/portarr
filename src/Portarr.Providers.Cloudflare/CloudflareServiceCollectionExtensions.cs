using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Cloudflare;

public static class CloudflareServiceCollectionExtensions
{
    /// <summary>Largest Cloudflare API response read (a page of projects with their deployments).</summary>
    private const int MaxResponseBytes = 4 * 1024 * 1024;

    public static IServiceCollection AddCloudflareProvider(this IServiceCollection services)
    {
        services.AddHttpClient(CloudflareWorkloadSource.HttpClientName, client =>
        {
            client.BaseAddress = CloudflareWorkloadSource.ApiBaseAddress;
            client.Timeout = TimeSpan.FromSeconds(15);
            client.MaxResponseContentBufferSize = MaxResponseBytes;
        });
        services.AddSingleton<IWorkloadSource, CloudflareWorkloadSource>();
        services.AddSingleton<IEnvironmentConnector, CloudflareEnvironmentConnector>();

        return services;
    }
}
