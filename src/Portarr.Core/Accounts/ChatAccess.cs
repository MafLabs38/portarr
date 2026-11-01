using System.Buffers.Text;
using System.Security.Cryptography;
using Dapper;
using Portarr.Core.Datastore;

namespace Portarr.Core.Accounts;

/// <summary>Who owns a chat conversation: null <see cref="UserId"/> for one that has no owner (made before accounts, or its owner was deleted).</summary>
public sealed record ChatOwnership(string ConversationId, int? UserId, string OwnerName);

/// <summary>The identity a chat token stands for, and the conversation it was minted for.</summary>
public sealed record ChatTokenIdentity(AccountIdentity Identity, string ConversationId);

public interface IChatAccess
{
    /// <summary>Records who created a conversation.</summary>
    Task RegisterAsync(string conversationId, int ownerUserId, string ownerName, CancellationToken cancellationToken);

    Task<ChatOwnership?> FindAsync(string conversationId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, ChatOwnership>> ListAsync(CancellationToken cancellationToken);

    Task ForgetAsync(string conversationId, CancellationToken cancellationToken);

    /// <summary>Whether a user may read, write to, cancel or delete the conversation: its owner, or, when it has none, a user manager.</summary>
    Task<bool> CanAccessAsync(string conversationId, int? userId, bool isUserManager, CancellationToken cancellationToken);

    /// <summary>Mints the token the runner uses as the MCP key for this user's turns in this conversation (<c>ptc_…</c>, shown once, stored as SHA-256).</summary>
    Task<string> MintTokenAsync(int userId, string conversationId, CancellationToken cancellationToken);

    /// <summary>The user and conversation behind a presented token, with the user's permissions as they are now; null when unknown, expired or the user is disabled.</summary>
    Task<ChatTokenIdentity?> AuthenticateTokenAsync(string token, CancellationToken cancellationToken);
}

/// <summary>
/// Per-user chat (V7, D-98): a conversation belongs to the user who made it, and the runner reaches the MCP server with
/// a short-lived token tied to that user and conversation instead of the shared key. A token lasts 30 minutes from its
/// last use (12 hours at most) and only opens <c>/mcp</c>; the permissions are the user's, read at every call.
/// </summary>
public sealed class ChatAccess : IChatAccess
{
    public const string TokenPrefix = "ptc_";
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan TokenMaxLifetime = TimeSpan.FromHours(12);

    private readonly IDatabase _database;
    private readonly IAccountService _accounts;
    private readonly TimeProvider _timeProvider;

    public ChatAccess(IDatabase database, IAccountService accounts, TimeProvider timeProvider)
    {
        _database = database;
        _accounts = accounts;
        _timeProvider = timeProvider;
    }

    public async Task RegisterAsync(string conversationId, int ownerUserId, string ownerName, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """INSERT INTO "ChatConversations" ("Id", "OwnerUserId", "OwnerName", "Created") VALUES (@conversationId, @userId, @name, @created)""",
            new { conversationId, userId = ownerUserId, name = ownerName, created = Iso.Format(_timeProvider.GetUtcNow()) },
            cancellationToken: cancellationToken));
    }

    public async Task<ChatOwnership?> FindAsync(string conversationId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<OwnerRow>(new CommandDefinition(
            """SELECT "Id", "OwnerUserId", "OwnerName" FROM "ChatConversations" WHERE "Id" = @conversationId""",
            new { conversationId },
            cancellationToken: cancellationToken));
        return row is null ? null : new ChatOwnership(row.Id, row.OwnerUserId, row.OwnerName);
    }

    public async Task<IReadOnlyDictionary<string, ChatOwnership>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<OwnerRow>(new CommandDefinition(
            """SELECT "Id", "OwnerUserId", "OwnerName" FROM "ChatConversations" """,
            cancellationToken: cancellationToken));
        return rows.ToDictionary(r => r.Id, r => new ChatOwnership(r.Id, r.OwnerUserId, r.OwnerName), StringComparer.Ordinal);
    }

    public async Task ForgetAsync(string conversationId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""DELETE FROM "ChatTokens" WHERE "ConversationId" = @conversationId""", new { conversationId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("""DELETE FROM "ChatConversations" WHERE "Id" = @conversationId""", new { conversationId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> CanAccessAsync(string conversationId, int? userId, bool isUserManager, CancellationToken cancellationToken)
    {
        var owner = await FindAsync(conversationId, cancellationToken);
        if (owner?.UserId is { } ownerId)
        {
            return userId == ownerId;
        }

        // No owner: made before accounts, or the owner was deleted. The user managers look after those.
        return isUserManager;
    }

    public async Task<string> MintTokenAsync(int userId, string conversationId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var token = TokenPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""DELETE FROM "ChatTokens" WHERE "AbsoluteExpires" < @now OR "Expires" < @now""", new { now = Iso.Format(now) }, cancellationToken: cancellationToken));

        // A message keeps this conversation's earlier tokens alive too: a process the runner already started still holds one.
        await connection.ExecuteAsync(new CommandDefinition(
            """UPDATE "ChatTokens" SET "Expires" = @expires WHERE "ConversationId" = @conversationId AND "AbsoluteExpires" > @expires""",
            new { conversationId, expires = Iso.Format(now + TokenLifetime) },
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "ChatTokens" ("TokenHash", "UserId", "ConversationId", "Created", "Expires", "AbsoluteExpires")
            VALUES (@hash, @userId, @conversationId, @created, @expires, @absolute)
            """,
            new
            {
                hash = ApiKeyService.Hash(token),
                userId,
                conversationId,
                created = Iso.Format(now),
                expires = Iso.Format(now + TokenLifetime),
                absolute = Iso.Format(now + TokenMaxLifetime),
            },
            cancellationToken: cancellationToken));
        return token;
    }

    public async Task<ChatTokenIdentity?> AuthenticateTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal) || token.Length > 128)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TokenRow>(new CommandDefinition(
            """SELECT "Id", "UserId", "ConversationId", "Expires", "AbsoluteExpires" FROM "ChatTokens" WHERE "TokenHash" = @hash""",
            new { hash = ApiKeyService.Hash(token) },
            cancellationToken: cancellationToken));
        if (row is null || Iso.Parse(row.Expires) <= now || Iso.Parse(row.AbsoluteExpires) <= now)
        {
            return null;
        }

        var user = await _accounts.ResolveAsync(row.UserId, null, cancellationToken);
        if (user is null)
        {
            return null;
        }

        // Sliding: a token in use stays valid (up to its absolute end); written only once half its life is gone.
        var extended = new[] { now + TokenLifetime, Iso.Parse(row.AbsoluteExpires) }.Min();
        if (extended - Iso.Parse(row.Expires) > TokenLifetime / 2)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """UPDATE "ChatTokens" SET "Expires" = @expires WHERE "Id" = @id""",
                new { id = row.Id, expires = Iso.Format(extended) },
                cancellationToken: cancellationToken));
        }

        return new ChatTokenIdentity(user, row.ConversationId);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1144:Unused private types or members should be removed", Justification = "Dapper sets the properties when it maps a row.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S3459:Unassigned members should be removed", Justification = "Dapper sets the properties when it maps a row.")]
    private sealed class OwnerRow
    {
        public string Id { get; set; } = string.Empty;

        public int? OwnerUserId { get; set; }

        public string OwnerName { get; set; } = string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1144:Unused private types or members should be removed", Justification = "Dapper sets the properties when it maps a row.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S3459:Unassigned members should be removed", Justification = "Dapper sets the properties when it maps a row.")]
    private sealed class TokenRow
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string ConversationId { get; set; } = string.Empty;

        public string Expires { get; set; } = string.Empty;

        public string AbsoluteExpires { get; set; } = string.Empty;
    }
}
