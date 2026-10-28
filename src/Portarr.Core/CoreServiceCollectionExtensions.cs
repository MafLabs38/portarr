using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portarr.Core.SystemInfo;

namespace Portarr.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddPortarrCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISystemStatusService, SystemStatusService>();

        return services;
    }
}
