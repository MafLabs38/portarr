using Microsoft.Extensions.Logging;

namespace Portarr.Core.Instrumentation;

/// <summary>
/// The minimum level of Portarr's own logs, changeable while running. Implemented by the host
/// on top of its logging library, so the Core does not depend on it.
/// </summary>
public interface IRuntimeLogLevel
{
    LogLevel Current { get; }

    void Apply(LogLevel level);
}
