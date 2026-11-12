using System.Text.Json;
using Dapper;
using Portarr.Core.Accounts;
using Portarr.Core.Datastore;

namespace Portarr.Core.Mcp.Capture;

/// <summary>A server of the inbox with the hand-over it came in.</summary>
public sealed record McpCaptureRow(
    int RowId,
    int CaptureId,
    string Workstation,
    string Client,
    string Scope,
    DateTimeOffset ReceivedAt,
    string? SubmittedBy,
    string Name,
    string Identity,
    CapturedServer Server);

public interface IMcpCaptureStore
{
    /// <summary>
    /// Stores a capture. The servers still waiting from an earlier capture of the same workstation, client and scope are replaced by this
    /// one (the newest description wins); the ones already accepted or dismissed stay as the trail.
    /// </summary>
    Task<CaptureReceipt> SubmitAsync(CaptureRequest request, string? user, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The servers waiting for the operator, oldest first.</summary>
    Task<IReadOnlyList<McpCaptureRow>> PendingAsync(CancellationToken cancellationToken);

    /// <summary>Marks servers of the inbox accepted or dismissed.</summary>
    Task ResolveAsync(IReadOnlyCollection<int> rowIds, string status, string? user, string? resolvedAs, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Keeps the inbox in the database (tables <c>McpCaptures</c> and <c>McpCaptureServers</c>).</summary>
public sealed class McpCaptureStore : IMcpCaptureStore
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Dismissed = "dismissed";

    /// <summary>Hand-overs kept: the newest ones, accepted and dismissed servers included (the trail of what was decided).</summary>
    public const int KeepCaptures = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _database;

    public McpCaptureStore(IDatabase database)
    {
        _database = database;
    }

    public async Task<CaptureReceipt> SubmitAsync(CaptureRequest request, string? user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var replaced = await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM "McpCaptureServers" WHERE "Status" = @pending AND "CaptureId" IN
                (SELECT "Id" FROM "McpCaptures" WHERE "Workstation" = @workstation AND "Client" = @client AND "Scope" = @scope)
            """,
            new { pending = Pending, workstation = request.Workstation, client = request.Client, scope = request.Scope },
            transaction,
            cancellationToken: cancellationToken));

        var captureId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO "McpCaptures" ("Workstation", "Client", "Scope", "ReceivedAt", "SubmittedBy")
            VALUES (@workstation, @client, @scope, @at, @user) RETURNING "Id"
            """,
            new { workstation = request.Workstation, client = request.Client, scope = request.Scope, at = Iso.Format(now), user },
            transaction,
            cancellationToken: cancellationToken));

        foreach (var server in request.Servers)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO "McpCaptureServers" ("CaptureId", "Name", "Identity", "Payload", "Status")
                VALUES (@captureId, @name, @identity, @payload, @pending)
                """,
                new { captureId, name = server.Name, identity = McpIdentity.Of(server), payload = JsonSerializer.Serialize(server, Json), pending = Pending },
                transaction,
                cancellationToken: cancellationToken));
        }

        // A hand-over left with no server (all replaced) is dropped; the oldest ones beyond the cap go too, with their servers.
        await connection.ExecuteAsync(new CommandDefinition(
            """DELETE FROM "McpCaptures" WHERE "Id" NOT IN (SELECT DISTINCT "CaptureId" FROM "McpCaptureServers")""",
            transaction: transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """DELETE FROM "McpCaptures" WHERE "Id" <= (SELECT MAX("Id") FROM "McpCaptures") - @keep""",
            new { keep = KeepCaptures },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return new CaptureReceipt(captureId, request.Servers.Count, replaced);
    }

    public async Task<IReadOnlyList<McpCaptureRow>> PendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT s."Id" AS RowId, s."CaptureId", c."Workstation", c."Client", c."Scope", c."ReceivedAt", c."SubmittedBy", s."Name", s."Identity", s."Payload"
            FROM "McpCaptureServers" s JOIN "McpCaptures" c ON c."Id" = s."CaptureId"
            WHERE s."Status" = @pending ORDER BY s."Id"
            """,
            new { pending = Pending },
            cancellationToken: cancellationToken));

        var result = new List<McpCaptureRow>();
        foreach (var row in rows)
        {
            if (JsonSerializer.Deserialize<CapturedServer>(row.Payload, Json) is { } server)
            {
                result.Add(new McpCaptureRow(row.RowId, row.CaptureId, row.Workstation, row.Client, row.Scope, Iso.Parse(row.ReceivedAt), row.SubmittedBy, row.Name, row.Identity, server));
            }
        }

        return result;
    }

    public async Task ResolveAsync(IReadOnlyCollection<int> rowIds, string status, string? user, string? resolvedAs, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (rowIds.Count == 0)
        {
            return;
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """UPDATE "McpCaptureServers" SET "Status" = @status, "ResolvedAt" = @at, "ResolvedBy" = @user, "ResolvedAs" = @resolvedAs WHERE "Id" IN @ids AND "Status" = @pending""",
            new { status, at = Iso.Format(now), user, resolvedAs, ids = rowIds.ToArray(), pending = Pending },
            cancellationToken: cancellationToken));
    }

    internal sealed class Row
    {
        public int RowId { get; set; }

        public int CaptureId { get; set; }

        public string Workstation { get; set; } = string.Empty;

        public string Client { get; set; } = string.Empty;

        public string Scope { get; set; } = string.Empty;

        public string ReceivedAt { get; set; } = string.Empty;

        public string? SubmittedBy { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Identity { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;
    }
}
