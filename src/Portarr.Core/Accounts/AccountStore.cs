using System.Data.Common;
using System.Globalization;
using Dapper;
using Portarr.Core.Configuration;
using Portarr.Core.Datastore;

namespace Portarr.Core.Accounts;

/// <summary>Timestamps are stored as ISO 8601 text (UTC): one format that SQLite and PostgreSQL both sort and read the same way.</summary>
internal static class Iso
{
    public static string Format(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    public static string? Format(DateTimeOffset? value) => value is null ? null : Format(value.Value);

    public static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTimeOffset? ParseOrNull(string? value) => string.IsNullOrEmpty(value) ? null : Parse(value);
}

/// <summary>A user row with its password hash: only the account service sees it.</summary>
internal sealed class UserRow
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public int RoleId { get; set; }

    public string RoleName { get; set; } = string.Empty;

    public string? AvatarFile { get; set; }

    public string Created { get; set; } = string.Empty;

    public string? LastLogin { get; set; }

    public bool Disabled { get; set; }

    public int FailedAttempts { get; set; }

    public string? LockoutEnd { get; set; }

    public string SecurityStamp { get; set; } = string.Empty;

    public AccountUser ToUser() => new(Id, Username, DisplayName, RoleId, RoleName, AvatarFile, Iso.Parse(Created), Iso.ParseOrNull(LastLogin), Disabled, Iso.ParseOrNull(LockoutEnd));
}

/// <summary>What a new user is made of.</summary>
internal sealed record NewUser(string Username, string DisplayName, string PasswordHash, int RoleId, DateTimeOffset Created);

internal sealed class RoleRow
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool BuiltIn { get; set; }

    public int Users { get; set; }
}

/// <summary>
/// SQL of users and roles (Dapper, quoted identifiers so one statement runs on SQLite and PostgreSQL). No rule lives
/// here: <see cref="AccountService"/> decides, this only reads and writes.
/// </summary>
internal sealed class AccountStore
{
    private const string UserSelect = """
        SELECT u."Id", u."Username", u."DisplayName", u."PasswordHash", u."RoleId", r."Name" AS "RoleName", u."AvatarFile",
               u."Created", u."LastLogin", u."Disabled", u."FailedAttempts", u."LockoutEnd", u."SecurityStamp"
        FROM "Users" u JOIN "Roles" r ON r."Id" = u."RoleId"
        """;

    private const string InsertUserSql = """
        INSERT INTO "Users" ("Username", "UsernameNormalized", "DisplayName", "PasswordHash", "RoleId", "Created", "Disabled", "FailedAttempts", "SecurityStamp")
        VALUES (@Username, @Normalized, @DisplayName, @PasswordHash, @RoleId, @Created, @False, 0, @Stamp)
        """;

    private readonly IDatabase _database;

    public AccountStore(IDatabase database)
    {
        _database = database;
    }

    public static string Normalize(string username) => username.Trim().ToLowerInvariant();

    public async Task<int> CountUsersAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("""SELECT COUNT(*) FROM "Users" """, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Creates the first administrator, and only when no user exists: one statement inside a transaction (on PostgreSQL
    /// with the table locked), so two requests racing for the setup cannot both win. Null when a user already exists.
    /// </summary>
    public async Task<int?> TryCreateFirstAdminAsync(NewUser user, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (_database.Type == DatabaseType.Postgres)
        {
            await connection.ExecuteAsync(new CommandDefinition("""LOCK TABLE "Users" IN SHARE ROW EXCLUSIVE MODE""", transaction: transaction, cancellationToken: cancellationToken));
        }

        var inserted = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "Users" ("Username", "UsernameNormalized", "DisplayName", "PasswordHash", "RoleId", "Created", "Disabled", "FailedAttempts", "SecurityStamp")
            SELECT @Username, @Normalized, @DisplayName, @PasswordHash, @RoleId, @Created, @False, 0, @Stamp
            WHERE NOT EXISTS (SELECT 1 FROM "Users")
            """,
            Parameters(user),
            transaction,
            cancellationToken: cancellationToken));
        if (inserted == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """SELECT "Id" FROM "Users" WHERE "UsernameNormalized" = @Normalized""",
            new { Normalized = Normalize(user.Username) },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<int> InsertUserAsync(NewUser user, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(InsertUserSql, Parameters(user), cancellationToken: cancellationToken));
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """SELECT "Id" FROM "Users" WHERE "UsernameNormalized" = @Normalized""",
            new { Normalized = Normalize(user.Username) },
            cancellationToken: cancellationToken));
    }

    public async Task<UserRow?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            UserSelect + """ WHERE u."UsernameNormalized" = @Normalized""",
            new { Normalized = Normalize(username) },
            cancellationToken: cancellationToken));
    }

    public async Task<UserRow?> FindByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            UserSelect + """ WHERE u."Id" = @id""",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<UserRow>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<UserRow>(new CommandDefinition(UserSelect + """ ORDER BY u."UsernameNormalized" """, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>Enabled users holding the built-in Admin role, not counting <paramref name="exceptUserId"/>.</summary>
    public async Task<int> CountEnabledAdminsAsync(int? exceptUserId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM "Users" u JOIN "Roles" r ON r."Id" = u."RoleId"
            WHERE r."Name" = @admin AND r."BuiltIn" = @true AND u."Disabled" = @false AND u."Id" <> @except
            """,
            new { admin = BuiltInRoles.Admin, @true = true, @false = false, except = exceptUserId ?? -1 },
            cancellationToken: cancellationToken));
    }

    public Task SetDisplayNameAsync(int id, string displayName, CancellationToken cancellationToken) =>
        ExecuteAsync("""UPDATE "Users" SET "DisplayName" = @displayName WHERE "Id" = @id""", new { id, displayName }, cancellationToken);

    /// <summary>
    /// Stores a new password, which ends the user's sessions (new stamp) and also revokes their API keys and chat tokens: a
    /// password changed because it leaked must not leave a key that outlives it.
    /// </summary>
    public async Task SetPasswordAsync(int id, string passwordHash, string stamp, string revokedAt, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """UPDATE "Users" SET "PasswordHash" = @passwordHash, "SecurityStamp" = @stamp, "FailedAttempts" = 0, "LockoutEnd" = NULL WHERE "Id" = @id""",
            new { id, passwordHash, stamp },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """UPDATE "ApiKeys" SET "Revoked" = @revokedAt WHERE "UserId" = @id AND "Revoked" IS NULL""",
            new { id, revokedAt },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("""DELETE FROM "ChatTokens" WHERE "UserId" = @id""", new { id }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Stores a hash computed with newer parameters; the session stays valid.</summary>
    public Task SetPasswordHashOnlyAsync(int id, string passwordHash, CancellationToken cancellationToken) =>
        ExecuteAsync("""UPDATE "Users" SET "PasswordHash" = @passwordHash WHERE "Id" = @id""", new { id, passwordHash }, cancellationToken);

    public Task SetRoleAsync(int id, int roleId, CancellationToken cancellationToken) =>
        ExecuteAsync("""UPDATE "Users" SET "RoleId" = @roleId WHERE "Id" = @id""", new { id, roleId }, cancellationToken);

    public Task SetDisabledAsync(int id, bool disabled, string stamp, CancellationToken cancellationToken) =>
        ExecuteAsync(
            """UPDATE "Users" SET "Disabled" = @disabled, "SecurityStamp" = @stamp, "FailedAttempts" = 0, "LockoutEnd" = NULL WHERE "Id" = @id""",
            new { id, disabled, stamp },
            cancellationToken);

    public Task SetAvatarAsync(int id, string? avatarFile, CancellationToken cancellationToken) =>
        ExecuteAsync("""UPDATE "Users" SET "AvatarFile" = @avatarFile WHERE "Id" = @id""", new { id, avatarFile }, cancellationToken);

    public Task RecordLoginAsync(int id, DateTimeOffset at, CancellationToken cancellationToken) =>
        ExecuteAsync(
            """UPDATE "Users" SET "LastLogin" = @at, "FailedAttempts" = 0, "LockoutEnd" = NULL WHERE "Id" = @id""",
            new { id, at = Iso.Format(at) },
            cancellationToken);

    /// <summary>Counts one more failed sign-in in a single statement (parallel guesses all count) and returns the total.</summary>
    public async Task<int> IncrementFailuresAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """UPDATE "Users" SET "FailedAttempts" = "FailedAttempts" + 1 WHERE "Id" = @id""",
            new { id },
            cancellationToken: cancellationToken));
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """SELECT "FailedAttempts" FROM "Users" WHERE "Id" = @id""",
            new { id },
            cancellationToken: cancellationToken));
    }

    public Task LockAsync(int id, DateTimeOffset until, CancellationToken cancellationToken) =>
        ExecuteAsync(
            """UPDATE "Users" SET "FailedAttempts" = 0, "LockoutEnd" = @until WHERE "Id" = @id""",
            new { id, until = Iso.Format(until) },
            cancellationToken);

    public Task DeleteAsync(int id, CancellationToken cancellationToken) =>
        ExecuteAsync("""DELETE FROM "Users" WHERE "Id" = @id""", new { id }, cancellationToken);

    public async Task<IReadOnlyList<RoleDefinition>> ListRolesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<RoleRow>(new CommandDefinition(
            """
            SELECT r."Id", r."Name", r."Description", r."BuiltIn", (SELECT COUNT(*) FROM "Users" u WHERE u."RoleId" = r."Id") AS "Users"
            FROM "Roles" r ORDER BY r."BuiltIn" DESC, r."Id"
            """,
            cancellationToken: cancellationToken))).AsList();
        var permissions = (await connection.QueryAsync<(int RoleId, string Permission)>(new CommandDefinition(
            """SELECT "RoleId", "Permission" FROM "RolePermissions" """,
            cancellationToken: cancellationToken))).ToLookup(p => p.RoleId, p => p.Permission);

        return rows.Select(row => new RoleDefinition(
            row.Id,
            row.Name,
            row.Description,
            row.BuiltIn,
            row.BuiltIn
                ? Permissions.Ordered(BuiltInRoles.Find(row.Name)?.Permissions ?? new HashSet<string>())
                : Permissions.Ordered(permissions[row.Id]),
            row.Users)).ToList();
    }

    public async Task<int> InsertRoleAsync(string name, string? description, IEnumerable<string> permissions, DateTimeOffset created, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "Roles" ("Name", "NameNormalized", "Description", "BuiltIn", "Created")
            VALUES (@name, @normalized, @description, @false, @created)
            """,
            new { name, normalized = Normalize(name), description, @false = false, created = Iso.Format(created) },
            transaction,
            cancellationToken: cancellationToken));
        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """SELECT "Id" FROM "Roles" WHERE "NameNormalized" = @normalized""",
            new { normalized = Normalize(name) },
            transaction,
            cancellationToken: cancellationToken));
        await WritePermissionsAsync(connection, transaction, id, permissions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task UpdateRoleAsync(int id, string name, string? description, IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """UPDATE "Roles" SET "Name" = @name, "NameNormalized" = @normalized, "Description" = @description WHERE "Id" = @id""",
            new { id, name, normalized = Normalize(name), description },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("""DELETE FROM "RolePermissions" WHERE "RoleId" = @id""", new { id }, transaction, cancellationToken: cancellationToken));
        await WritePermissionsAsync(connection, transaction, id, permissions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task DeleteRoleAsync(int id, CancellationToken cancellationToken) =>
        ExecuteAsync("""DELETE FROM "Roles" WHERE "Id" = @id""", new { id }, cancellationToken);

    /// <summary>The permissions a custom role stores (built-in roles are defined in code).</summary>
    public async Task<IReadOnlySet<string>> GetStoredPermissionsAsync(int roleId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var keys = await connection.QueryAsync<string>(new CommandDefinition(
            """SELECT "Permission" FROM "RolePermissions" WHERE "RoleId" = @roleId""",
            new { roleId },
            cancellationToken: cancellationToken));
        return keys.ToHashSet(StringComparer.Ordinal);
    }

    private static async Task WritePermissionsAsync(DbConnection connection, DbTransaction transaction, int roleId, IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        foreach (var permission in permissions.Distinct(StringComparer.Ordinal))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """INSERT INTO "RolePermissions" ("RoleId", "Permission") VALUES (@roleId, @permission)""",
                new { roleId, permission },
                transaction,
                cancellationToken: cancellationToken));
        }
    }

    private static object Parameters(NewUser user) => new
    {
        user.Username,
        Normalized = Normalize(user.Username),
        user.DisplayName,
        user.PasswordHash,
        user.RoleId,
        Created = Iso.Format(user.Created),
        False = false,
        Stamp = NewStamp(),
    };

    public static string NewStamp() => Guid.NewGuid().ToString("N");

    private async Task ExecuteAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }
}
