using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Portarr.Core.Datastore;

namespace Portarr.Core.Accounts;

/// <summary>An API key as listed: never the key itself, only its recognizable prefix.</summary>
public sealed record ApiKeyInfo(int Id, int UserId, string Name, string Prefix, string Scope, DateTimeOffset Created, DateTimeOffset? LastUsed, DateTimeOffset? Expires, DateTimeOffset? Revoked)
{
    public bool IsActive(DateTimeOffset now) => Revoked is null && (Expires is null || Expires > now);
}

/// <summary>A key just created: the only time its value exists outside the hash.</summary>
public sealed record CreatedApiKey(ApiKeyInfo Info, string Key);

public interface IApiKeyService
{
    Task<IReadOnlyList<ApiKeyInfo>> ListAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Creates a key for the actor; <paramref name="scope"/> is <c>read</c> or <c>full</c>.</summary>
    Task<AccountResult<CreatedApiKey>> CreateAsync(AccountActor actor, string name, string scope, DateTimeOffset? expires, CancellationToken cancellationToken);

    /// <summary>Revokes one of the actor's own keys.</summary>
    Task<AccountResult<ApiKeyInfo>> RevokeAsync(AccountActor actor, int keyId, CancellationToken cancellationToken);

    /// <summary>The identity behind a presented <c>ptr_</c> key, with the permissions its scope allows; null when unknown, revoked, expired or its owner is disabled.</summary>
    Task<AccountIdentity?> AuthenticateAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// Per-user API keys (V7, D-97): <c>ptr_</c> plus 256 random bits, shown once and stored as SHA-256. A key is
/// <c>read</c> (the view permissions of its owner's role) or <c>full</c> (the owner's permissions); the permissions are
/// those of the owner's role as it is now, so a key never exceeds it, and a disabled owner's keys stop working.
/// </summary>
public sealed class ApiKeyService : IApiKeyService
{
    public const string KeyPrefix = "ptr_";
    public const string ReadScope = "read";
    public const string FullScope = "full";
    public const int MaxKeysPerUser = 20;

    private static readonly TimeSpan LastUsedGranularity = TimeSpan.FromMinutes(5);

    private readonly IDatabase _database;
    private readonly IAccountService _accounts;
    private readonly IAuthAuditLog _audit;
    private readonly TimeProvider _timeProvider;

    public ApiKeyService(IDatabase database, IAccountService accounts, IAuthAuditLog audit, TimeProvider timeProvider)
    {
        _database = database;
        _accounts = accounts;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    /// <summary>What a scope keeps of the owner's permissions.</summary>
    public static IReadOnlySet<string> Scoped(IReadOnlySet<string> ownerPermissions, string scope) =>
        scope == FullScope ? ownerPermissions : ownerPermissions.Where(Permissions.View.Contains).ToHashSet(StringComparer.Ordinal);

    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public async Task<IReadOnlyList<ApiKeyInfo>> ListAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            Select + """ WHERE "UserId" = @userId ORDER BY "Id" DESC""",
            new { userId },
            cancellationToken: cancellationToken));
        return rows.Select(r => r.ToInfo()).ToList();
    }

    public async Task<AccountResult<CreatedApiKey>> CreateAsync(AccountActor actor, string name, string scope, DateTimeOffset? expires, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var title = name.Trim();
        if (actor.UserId is not { } userId)
        {
            return AccountResult<CreatedApiKey>.Fail(AccountError.Forbidden, "Only a user account can have API keys.");
        }

        if (title.Length is < 1 or > 60 || title.Any(char.IsControl))
        {
            return AccountResult<CreatedApiKey>.Fail(AccountError.Invalid, "name must be 1 to 60 characters on one line.");
        }

        if (scope is not (ReadScope or FullScope))
        {
            return AccountResult<CreatedApiKey>.Fail(AccountError.Invalid, "scope must be 'read' or 'full'.");
        }

        if (expires is { } when && (when <= now || when > now.AddYears(5)))
        {
            return AccountResult<CreatedApiKey>.Fail(AccountError.Invalid, "expires must be in the future, within 5 years.");
        }

        if ((await ListAsync(userId, cancellationToken)).Count(k => k.IsActive(now)) >= MaxKeysPerUser)
        {
            return AccountResult<CreatedApiKey>.Fail(AccountError.Conflict, $"At most {MaxKeysPerUser} active keys per user: revoke one first.");
        }

        var key = KeyPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var prefix = key[..(KeyPrefix.Length + 6)];
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "ApiKeys" ("UserId", "Name", "Prefix", "KeyHash", "Scope", "Created", "Expires")
            VALUES (@userId, @title, @prefix, @hash, @scope, @created, @expires)
            """,
            new { userId, title, prefix, hash = Hash(key), scope, created = Iso.Format(now), expires = Iso.Format(expires) },
            cancellationToken: cancellationToken));
        var row = await connection.QuerySingleAsync<Row>(new CommandDefinition(
            Select + """ WHERE "KeyHash" = @hash""",
            new { hash = Hash(key) },
            cancellationToken: cancellationToken));
        await _audit.AppendAsync("key.create", actor.Name, title, $"{scope} key {prefix}", actor.Client, cancellationToken);
        return AccountResult<CreatedApiKey>.Ok(new CreatedApiKey(row.ToInfo(), key));
    }

    public async Task<AccountResult<ApiKeyInfo>> RevokeAsync(AccountActor actor, int keyId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            Select + """ WHERE "Id" = @keyId AND "UserId" = @userId""",
            new { keyId, userId = actor.UserId ?? -1 },
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return AccountResult<ApiKeyInfo>.Fail(AccountError.NotFound, "No such key.");
        }

        if (row.Revoked is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """UPDATE "ApiKeys" SET "Revoked" = @at WHERE "Id" = @keyId""",
                new { keyId, at = Iso.Format(_timeProvider.GetUtcNow()) },
                cancellationToken: cancellationToken));
            await _audit.AppendAsync("key.revoke", actor.Name, row.Name, $"key {row.Prefix}", actor.Client, cancellationToken);
        }

        var revoked = await connection.QuerySingleAsync<Row>(new CommandDefinition(Select + """ WHERE "Id" = @keyId""", new { keyId }, cancellationToken: cancellationToken));
        return AccountResult<ApiKeyInfo>.Ok(revoked.ToInfo());
    }

    public async Task<AccountIdentity?> AuthenticateAsync(string key, CancellationToken cancellationToken)
    {
        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal) || key.Length > 128)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            Select + """ WHERE "KeyHash" = @hash""",
            new { hash = Hash(key) },
            cancellationToken: cancellationToken));
        if (row is null || !row.ToInfo().IsActive(now))
        {
            return null;
        }

        var owner = await _accounts.ResolveAsync(row.UserId, null, cancellationToken);
        if (owner is null)
        {
            return null;
        }

        if (Iso.ParseOrNull(row.LastUsed) is not { } last || now - last > LastUsedGranularity)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """UPDATE "ApiKeys" SET "LastUsed" = @at WHERE "Id" = @id""",
                new { id = row.Id, at = Iso.Format(now) },
                cancellationToken: cancellationToken));
        }

        return owner with { Permissions = Scoped(owner.Permissions, row.Scope) };
    }

    private const string Select = """SELECT "Id", "UserId", "Name", "Prefix", "Scope", "Created", "LastUsed", "Expires", "Revoked" FROM "ApiKeys" """;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1144:Unused private types or members should be removed", Justification = "Dapper sets the properties when it maps a row.")]
    private sealed class Row
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Prefix { get; set; } = string.Empty;

        public string Scope { get; set; } = ReadScope;

        public string Created { get; set; } = string.Empty;

        public string? LastUsed { get; set; }

        public string? Expires { get; set; }

        public string? Revoked { get; set; }

        public ApiKeyInfo ToInfo() => new(Id, UserId, Name, Prefix, Scope, Iso.Parse(Created), Iso.ParseOrNull(LastUsed), Iso.ParseOrNull(Expires), Iso.ParseOrNull(Revoked));
    }
}
