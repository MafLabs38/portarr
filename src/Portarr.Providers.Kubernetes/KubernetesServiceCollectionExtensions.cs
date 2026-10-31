using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Kubernetes;

public static class KubernetesServiceCollectionExtensions
{
    public static IServiceCollection AddKubernetesProvider(this IServiceCollection services)
    {
        services.AddSingleton<KubernetesApiClient>();
        services.AddSingleton<IWorkloadSource, KubernetesWorkloadSource>();
        services.AddSingleton<IInfrastructureSource, KubernetesInfrastructureSource>();
        services.AddSingleton<IHostMetricsSource, KubernetesNodeExporterSource>();

        return services;
    }
}
