using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;
using Portarr.Core.Instrumentation;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Portarr.Host.Logging;

public static class LoggingServiceCollectionExtensions
{
    private const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff}|{Level:u3}|{SourceContext}|{Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Console and rolling files in <c>&lt;data folder&gt;/logs</c> (7 days, 10 MB per file), with a
    /// minimum level changeable at runtime. Framework noise stays at Warning whatever the level.
    /// </summary>
    public static IServiceCollection AddPortarrLogging(this IServiceCollection services)
    {
        var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
        services.AddSingleton<IRuntimeLogLevel>(new SerilogRuntimeLogLevel(levelSwitch));

        services.AddSerilog((provider, logger) => logger
            .MinimumLevel.ControlledBy(levelSwitch)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: OutputTemplate, formatProvider: null)
            .WriteTo.File(
                Path.Combine(provider.GetRequiredService<IOptions<PortarrOptions>>().Value.DataFolder, "logs", "portarr.txt"),
                outputTemplate: OutputTemplate,
                formatProvider: null,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true));

        return services;
    }

    /// <summary>
    /// One line per HTTP request: Debug when it succeeds (visible only while investigating),
    /// Error for server errors.
    /// </summary>
    public static WebApplication UsePortarrRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, exception) =>
            exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                ? LogEventLevel.Error
                : LogEventLevel.Debug);

        return app;
    }
}
