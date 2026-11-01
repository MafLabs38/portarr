using System.Data.Common;
using Portarr.Core.Configuration;

namespace Portarr.Core.Datastore;

/// <summary>
/// Entry point to Portarr's own database (SQLite or PostgreSQL). Repositories open short-lived
/// connections through it and run explicit SQL with Dapper (ADR 0004).
/// </summary>
public interface IDatabase
{
    DatabaseType Type { get; }

    /// <summary>ADO.NET connection string, also used by the migration runner.</summary>
    string ConnectionString { get; }

    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Engine and version, e.g. "SQLite 3.46.1" or "PostgreSQL 17.2".</summary>
    Task<string> GetEngineVersionAsync(CancellationToken cancellationToken = default);
}
