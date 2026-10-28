using Portarr.Core.Instrumentation;
using Serilog.Core;
using Serilog.Events;

namespace Portarr.Host.Logging;

/// <summary>Maps Portarr's runtime log level onto Serilog's level switch.</summary>
public sealed class SerilogRuntimeLogLevel : IRuntimeLogLevel
{
    private readonly LoggingLevelSwitch _levelSwitch;

    public SerilogRuntimeLogLevel(LoggingLevelSwitch levelSwitch)
    {
        _levelSwitch = levelSwitch;
    }

    public LogLevel Current => _levelSwitch.MinimumLevel switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        _ => LogLevel.Critical,
    };

    public void Apply(LogLevel level)
    {
        _levelSwitch.MinimumLevel = level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            _ => LogEventLevel.Fatal,
        };
    }
}
