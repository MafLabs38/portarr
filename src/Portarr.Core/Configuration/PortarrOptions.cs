using System.Net;
using System.Net.Sockets;

namespace Portarr.Core.Configuration;

/// <summary>
/// Startup settings, read from appsettings.json then from <c>PORTARR__*</c> environment variables
/// (e.g. <c>PORTARR__DATAFOLDER</c>, <c>PORTARR__DATABASE__TYPE</c>).
/// </summary>
public sealed class PortarrOptions
{
    /// <summary>Folder holding the SQLite database and future backups. Created if missing.</summary>
    public string DataFolder { get; set; } = DefaultDataFolder();

    public DatabaseOptions Database { get; set; } = new();

    /// <summary>Path of catalog.yaml; defaults to the data folder.</summary>
    public string? CatalogFile { get; set; }

    public string EffectiveCatalogFile => CatalogFile ?? Path.Combine(DataFolder, "catalog.yaml");

    /// <summary>The operator's own icons: an <c>icons</c> folder next to the catalog (D-47).</summary>
    public string CustomIconsFolder => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(EffectiveCatalogFile))!, "icons");

    /// <summary>
    /// Live logs of workloads that no catalog app declares. Off by default: until authentication
    /// exists (C2), anyone who reaches Portarr can read the logs it serves.
    /// </summary>
    public bool LogsOfAllWorkloads { get; set; }

    /// <summary>
    /// Reverse proxies allowed to set X-Forwarded-For and X-Forwarded-Proto: comma-separated IPs or
    /// CIDR networks (e.g. <c>10.0.0.2,10.0.1.0/24</c>). Empty: forwarded headers are never trusted,
    /// so a client cannot pick its own address to dodge the per-client rate limits.
    /// </summary>
    public string? TrustedProxies { get; set; }

    /// <summary>The entries of <see cref="TrustedProxies"/>, trimmed.</summary>
    public IReadOnlyList<string> TrustedProxyEntries =>
        (TrustedProxies ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Editing the catalog through the API and the MCP write tools (D-83, D-85).</summary>
    public CatalogOptions Catalog { get; set; } = new();

    /// <summary>
    /// The key API writes require until Portarr has accounts (C2): <c>PORTARR__APIKEY</c>, sent as <c>X-Api-Key</c>
    /// or <c>Authorization: Bearer</c>. Never returned by the API, never logged.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Community icon set, fetched once per icon and cached in the data folder.</summary>
    public Uri IconSourceUrl { get; set; } = new("https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/");

    /// <summary>A trusted proxy entry: a CIDR network, or one address (a /32 or /128 network).</summary>
    public static bool TryParseProxy(string entry, out IPNetwork network)
    {
        if (entry.Contains('/', StringComparison.Ordinal))
        {
            return IPNetwork.TryParse(entry, out network);
        }

        if (IPAddress.TryParse(entry, out var address))
        {
            network = new IPNetwork(address, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
            return true;
        }

        network = default;
        return false;
    }

    private static string DefaultDataFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "Portarr");
    }
}

/// <summary>Section <c>Catalog</c>: <c>PORTARR__CATALOG__WRITABLE</c>, <c>PORTARR__CATALOG__BACKUPS</c>.</summary>
public sealed class CatalogOptions
{
    public const int DefaultBackups = 10;

    /// <summary>Off by default: the catalog is a file the operator owns (D-44); the API edits it only when asked to.</summary>
    public bool Writable { get; set; }

    /// <summary>How many previous versions are kept next to the catalog (<c>catalog.yaml.bak-&lt;timestamp&gt;</c>).</summary>
    public int Backups { get; set; } = DefaultBackups;
}

public sealed class DatabaseOptions
{
    public DatabaseType Type { get; set; } = DatabaseType.Sqlite;

    /// <summary>Required for PostgreSQL; ignored for SQLite, which lives in the data folder.</summary>
    public string? ConnectionString { get; set; }
}

public enum DatabaseType
{
    Sqlite,
    Postgres,
}
