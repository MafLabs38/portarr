using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker;

public static class DockerServiceCollectionExtensions
{
    public static IServiceCollection AddDockerProvider(this IServiceCollection services)
    {
        services.AddSingleton<DockerHttpClientFactory>();
        services.AddSingleton<IWorkloadSource, DockerWorkloadSource>();
        services.AddSingleton<IInfrastructureSource, DockerInfrastructureSource>();
        services.AddSingleton<IEnvironmentConnector, DockerEnvironmentConnector>();

        return services;
    }
}
