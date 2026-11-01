using System.Reflection;

namespace Portarr.Common;

/// <summary>
/// Identity of the running build, read once from the entry assembly.
/// </summary>
public static class BuildInfo
{
    public const string AppName = "Portarr";

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // Strip the "+<commit sha>" suffix the SDK appends to the informational version.
        return informational?.Split('+')[0] ?? "0.0.0";
    }
}
