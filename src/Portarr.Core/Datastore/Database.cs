using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Npgsql;
using Portarr.Core.Configuration;

namespace Portarr.Core.Datastore;

public sealed class Database : IDatabase
{
    public const string SqliteFileName = "portarr.db";

    public Database(IOptions<PortarrOptions> options)
    {
        var settings = options.Value;
        Type = settings.Database.Type;
        ConnectionString = Type switch
        {
            DatabaseType.Sqlite => BuildSqliteConnectionString(settings.DataFolder),
            DatabaseType.Postgres => settings.Database.ConnectionString!,
            _ => throw new NotSupportedException($"Unsupported database type {Type}."),
        };
    }

    public DatabaseType Type { get; }

    public string ConnectionString { get; }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        DbConnection connection = Type == DatabaseType.Sqlite
            ? new SqliteConnection(ConnectionString)
            : new NpgsqlConnection(ConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<string> GetEngineVersionAsync(CancellationToken cancellationToken = default)
    {
        var sql = Type == DatabaseType.Sqlite ? "SELECT sqlite_version()" : "SHOW server_version";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var version = await connection.ExecuteScalarAsync<string>(new CommandDefinition(sql, cancellationToken: cancellationToken));

        // PostgreSQL may append build details: "17.2 (Debian 17.2-1.pgdg120+1)".
        var shortVersion = version?.Split(' ')[0] ?? "unknown";
        return Type == DatabaseType.Sqlite ? $"SQLite {shortVersion}" : $"PostgreSQL {shortVersion}";
    }

    private static string BuildSqliteConnectionString(string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);

        return new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataFolder, SqliteFileName),
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
    }
}
