using Microsoft.Extensions.Logging;
using Portarr.Core.Configuration;

namespace Portarr.Core.Instrumentation;

public interface ILogLevelService
{
    LogLevel Current { get; }

    /// <summary>Applies the level immediately and keeps it across restarts (F-SYS-05).</summary>
    Task SetAsync(LogLevel level, CancellationToken cancellationToken = default);

    /// <summary>Re-applies the stored level at startup.</summary>
    Task RestoreAsync(CancellationToken cancellationToken = default);
}

public sealed class LogLevelService : ILogLevelService
{
    public const string ConfigKey = "LogLevel";

    /// <summary>Levels an operator can pick; Critical and None would hide errors.</summary>
    public static readonly IReadOnlyList<LogLevel> Selectable =
        [LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error];

    private readonly IConfigRepository _configRepository;
    private readonly IRuntimeLogLevel _runtimeLogLevel;

    public LogLevelService(IConfigRepository configRepository, IRuntimeLogLevel runtimeLogLevel)
    {
        _configRepository = configRepository;
        _runtimeLogLevel = runtimeLogLevel;
    }

    public LogLevel Current => _runtimeLogLevel.Current;

    public async Task SetAsync(LogLevel level, CancellationToken cancellationToken = default)
    {
        if (!Selectable.Contains(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Log level must be Trace, Debug, Information, Warning or Error.");
        }

        await _configRepository.SetAsync(ConfigKey, level.ToString(), cancellationToken);
        _runtimeLogLevel.Apply(level);
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _configRepository.GetAsync(ConfigKey, cancellationToken);

        // An unknown stored value (edited by hand, or from a future version) falls back to the default.
        var level = Enum.TryParse<LogLevel>(stored, ignoreCase: true, out var parsed) && Selectable.Contains(parsed)
            ? parsed
            : LogLevel.Information;

        _runtimeLogLevel.Apply(level);
    }
}
