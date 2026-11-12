using Dapper;
using Portarr.Core.Accounts;
using Portarr.Core.Datastore;

namespace Portarr.Core.Mcp;

/// <summary>
/// What a check of a remote MCP server says (D-114). <c>AuthenticationRequired</c> is a healthy server that answered 401 or
/// 403 to a probe without credentials: it is there and enforcing, nothing more can be said without a credential.
/// </summary>
public enum McpHealthState
{
    Healthy,
    AuthenticationRequired,
    Down,

    /// <summary>Portarr could not run the check and has no earlier result (D-104): nothing is known about the server, and it is not blamed.</summary>
    Unknown,
}

/// <summary>
/// The health of one MCP server: its state, since when it has been up or down, when it was last seen healthy and last
/// checked. <c>Error</c> is a reason code of the checks (<c>timeout</c>, <c>dns-failure</c>, <c>http-502</c>…) when down.
/// </summary>
public sealed record McpServerHealth(
    string ServerId,
    McpHealthState State,
    DateTimeOffset Since,
    DateTimeOffset? LastHealthyAt,
    DateTimeOffset LastCheckedAt,
    string? Error,
    int? StatusCode,
    bool CheckFailed = false)
{
    public bool IsUp => State is McpHealthState.Healthy or McpHealthState.AuthenticationRequired;
}

/// <summary>The health of the servers, from memory (loaded from the database at start, then kept by the monitor).</summary>
public interface IMcpHealth
{
    /// <summary>The health of every server that has been checked, by server id. A server not listed has not been checked yet, or is not checked.</summary>
    IReadOnlyDictionary<string, McpServerHealth> Current { get; }
}

public interface IMcpHealthStore
{
    Task<IReadOnlyList<McpServerHealth>> ReadAllAsync(CancellationToken cancellationToken);

    Task SaveAsync(McpServerHealth health, CancellationToken cancellationToken);

    /// <summary>Forgets the servers that are no longer declared or no longer checked.</summary>
    Task RetainAsync(IReadOnlyCollection<string> serverIds, CancellationToken cancellationToken);
}

/// <summary>Keeps <see cref="McpServerHealth"/> in the database (table <c>McpServerHealth</c>), one row per server.</summary>
public sealed class McpHealthStore : IMcpHealthStore
{
    private readonly IDatabase _database;

    public McpHealthStore(IDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<McpServerHealth>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """SELECT "ServerId", "State", "Since", "LastHealthyAt", "LastCheckedAt", "Error", "StatusCode" FROM "McpServerHealth" """,
            cancellationToken: cancellationToken));
        return rows.Select(Read).OfType<McpServerHealth>().ToList();
    }

    /// <summary>A row as a health, or null when it cannot be read: one bad row costs that server its history, not every server.</summary>
    private static McpServerHealth? Read(Row row)
    {
        try
        {
            return Enum.TryParse<McpHealthState>(row.State, out var state)
                ? new McpServerHealth(row.ServerId, state, Iso.Parse(row.Since), Iso.ParseOrNull(row.LastHealthyAt), Iso.Parse(row.LastCheckedAt), row.Error, row.StatusCode)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public async Task SaveAsync(McpServerHealth health, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var parameters = new
        {
            id = health.ServerId,
            state = health.State.ToString(),
            since = Iso.Format(health.Since),
            lastHealthy = Iso.Format(health.LastHealthyAt),
            lastChecked = Iso.Format(health.LastCheckedAt),
            error = health.Error,
            status = health.StatusCode,
        };

        // Replace the row: UPDATE first, INSERT when nothing was there (the same SQL on SQLite and PostgreSQL).
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """UPDATE "McpServerHealth" SET "State" = @state, "Since" = @since, "LastHealthyAt" = @lastHealthy, "LastCheckedAt" = @lastChecked, "Error" = @error, "StatusCode" = @status WHERE "ServerId" = @id""",
            parameters,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """INSERT INTO "McpServerHealth" ("ServerId", "State", "Since", "LastHealthyAt", "LastCheckedAt", "Error", "StatusCode") VALUES (@id, @state, @since, @lastHealthy, @lastChecked, @error, @status)""",
                parameters,
                cancellationToken: cancellationToken));
        }
    }

    public async Task RetainAsync(IReadOnlyCollection<string> serverIds, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var stored = (await connection.QueryAsync<string>(new CommandDefinition("""SELECT "ServerId" FROM "McpServerHealth" """, cancellationToken: cancellationToken))).ToList();
        foreach (var gone in stored.Where(id => !serverIds.Contains(id)))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """DELETE FROM "McpServerHealth" WHERE "ServerId" = @id""",
                new { id = gone },
                cancellationToken: cancellationToken));
        }
    }

    internal sealed class Row
    {
        public string ServerId { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;

        public string Since { get; set; } = string.Empty;

        public string? LastHealthyAt { get; set; }

        public string LastCheckedAt { get; set; } = string.Empty;

        public string? Error { get; set; }

        public int? StatusCode { get; set; }
    }
}
