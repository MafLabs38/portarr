using Dapper;
using Portarr.Core.Datastore;

namespace Portarr.Core.Configuration;

/// <summary>
/// Runtime settings stored in the Config table. Identifiers are quoted so the same SQL runs
/// on SQLite and on PostgreSQL, where FluentMigrator creates case-sensitive names.
/// </summary>
public interface IConfigRepository
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}

public sealed class ConfigRepository : IConfigRepository
{
    private readonly IDatabase _database;

    public ConfigRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """SELECT "Value" FROM "Config" WHERE "Key" = @key""",
            new { key },
            cancellationToken: cancellationToken));
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "Config" ("Key", "Value") VALUES (@key, @value)
            ON CONFLICT ("Key") DO UPDATE SET "Value" = excluded."Value"
            """,
            new { key, value },
            cancellationToken: cancellationToken));
    }
}
