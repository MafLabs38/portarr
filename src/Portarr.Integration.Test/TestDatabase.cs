using Npgsql;
using Portarr.Core.Configuration;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Portarr.Integration.Test.TestDatabase))]

namespace Portarr.Integration.Test;

/// <summary>
/// The database every integration test runs against, chosen by <c>PORTARR_TEST_DATABASE</c>
/// (<c>sqlite</c> by default, <c>postgres</c> starts a real server with Testcontainers).
/// CI runs the suite once per engine (TESTING.md §3).
/// </summary>
public sealed class TestDatabase : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public DatabaseType Type { get; } =
        string.Equals(Environment.GetEnvironmentVariable("PORTARR_TEST_DATABASE"), "postgres", StringComparison.OrdinalIgnoreCase)
            ? DatabaseType.Postgres
            : DatabaseType.Sqlite;

    public async ValueTask InitializeAsync()
    {
        if (Type == DatabaseType.Postgres)
        {
            _postgres = new PostgreSqlBuilder("postgres:17-alpine").WithCommand("-c", "max_connections=500").Build();
            await _postgres.StartAsync();
        }
    }

    /// <summary>
    /// Settings for one test: its own data folder and, on PostgreSQL, its own empty database,
    /// so tests running in parallel never migrate the same schema at the same time.
    /// </summary>
    public async Task<PortarrOptions> CreateOptionsAsync()
    {
        return new PortarrOptions
        {
            DataFolder = Directory.CreateTempSubdirectory("portarr-test-").FullName,
            Database = new DatabaseOptions
            {
                Type = Type,
                ConnectionString = _postgres is null ? null : await CreatePostgresDatabaseAsync(_postgres),
            },
        };
    }

    private static async Task<string> CreatePostgresDatabaseAsync(PostgreSqlContainer postgres)
    {
        var name = $"portarr_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = name }.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }
}
