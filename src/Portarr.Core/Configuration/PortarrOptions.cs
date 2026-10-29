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

    /// <summary>Community icon set, fetched once per icon and cached in the data folder.</summary>
    public Uri IconSourceUrl { get; set; } = new("https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/");

    private static string DefaultDataFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "Portarr");
    }
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
