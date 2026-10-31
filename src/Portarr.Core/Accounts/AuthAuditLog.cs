using Dapper;
using Microsoft.Extensions.Logging;
using Portarr.Core.Datastore;

namespace Portarr.Core.Accounts;

public interface IAuthAuditLog
{
    /// <summary>Records one event. A failure to write is logged, never thrown: signing in must not depend on the audit.</summary>
    Task AppendAsync(string eventName, string? actor, string? subject, string? detail, string? client, CancellationToken cancellationToken);

    /// <summary>The latest events, newest first.</summary>
    Task<IReadOnlyList<AuthAuditEntry>> ReadAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>
/// The audit of accounts (V7, D-96) in the database: sign-ins and failures, setup, changes to users, roles and API keys.
/// Text is cut to length and stripped of control characters (a user name typed on the login page is untrusted). Never a
/// password, a key or a token. The latest 5000 lines are kept.
/// </summary>
public sealed partial class AuthAuditLog : IAuthAuditLog
{
    public const int KeepLines = 5000;
    private const int PruneEvery = 200;

    private readonly IDatabase _database;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthAuditLog> _logger;
    private int _sincePrune;

    public AuthAuditLog(IDatabase database, TimeProvider timeProvider, ILogger<AuthAuditLog> logger)
    {
        _database = database;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task AppendAsync(string eventName, string? actor, string? subject, string? detail, string? client, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _database.OpenConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                """INSERT INTO "AuthAudit" ("At", "Event", "Actor", "Subject", "Detail", "Client") VALUES (@at, @eventName, @actor, @subject, @detail, @client)""",
                new
                {
                    at = Iso.Format(_timeProvider.GetUtcNow()),
                    eventName,
                    actor = Clean(actor, 64),
                    subject = Clean(subject, 100),
                    detail = Clean(detail, 500),
                    client = Clean(client, 64),
                },
                cancellationToken: cancellationToken));

            if (Interlocked.Increment(ref _sincePrune) >= PruneEvery)
            {
                Interlocked.Exchange(ref _sincePrune, 0);
                await connection.ExecuteAsync(new CommandDefinition(
                    """DELETE FROM "AuthAudit" WHERE "Id" <= (SELECT MAX("Id") FROM "AuthAudit") - @keep""",
                    new { keep = KeepLines },
                    cancellationToken: cancellationToken));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogNotWritten(eventName, exception.GetType().Name);
        }
    }

    public async Task<IReadOnlyList<AuthAuditEntry>> ReadAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """SELECT "At", "Event", "Actor", "Subject", "Detail", "Client" FROM "AuthAudit" ORDER BY "Id" DESC LIMIT @limit""",
            new { limit = Math.Clamp(limit, 1, 1000) },
            cancellationToken: cancellationToken));
        return rows.Select(r => new AuthAuditEntry(Iso.Parse(r.At), r.Event, r.Actor, r.Subject, r.Detail, r.Client)).ToList();
    }

    /// <summary>One line of text of a bounded length: control characters (new lines included) become spaces.</summary>
    internal static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var text = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length <= maxLength ? text : text[..maxLength];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The auth audit could not record {Event} ({Error})")]
    private partial void LogNotWritten(string @event, string error);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1144:Unused private types or members should be removed", Justification = "Dapper sets the properties when it maps a row.")]
    private sealed class Row
    {
        public string At { get; set; } = string.Empty;

        public string Event { get; set; } = string.Empty;

        public string? Actor { get; set; }

        public string? Subject { get; set; }

        public string? Detail { get; set; }

        public string? Client { get; set; }
    }
}
