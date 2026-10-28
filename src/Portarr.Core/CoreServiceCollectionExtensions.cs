using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;
using Portarr.Core.Datastore;
using Portarr.Core.Datastore.Migration;
using Portarr.Core.Instrumentation;
using Portarr.Core.SystemInfo;

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

        return services;
    }
}
