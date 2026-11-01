using System.Globalization;
using FluentMigrator;

namespace Portarr.Core.Datastore.Migration;

/// <summary>
/// Local accounts, roles, per-user API keys, chat ownership and tokens, and the auth audit (V7, D-92 to D-98).
/// Timestamps are ISO 8601 text, the same on SQLite and PostgreSQL. A role's permissions are stored only for custom
/// roles: the four built-in ones (seeded here) are defined in code. <c>ExternalIssuer</c> and <c>ExternalSubject</c> are
/// empty until OIDC (step 2 of the vertical) maps an identity provider's user onto an account.
/// </summary>
[Migration(2, "accounts")]
public sealed class Accounts : FluentMigrator.Migration
{
    public override void Up()
    {
        Create.Table("Roles")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString(64).NotNullable()
            .WithColumn("NameNormalized").AsString(64).NotNullable().Unique()
            .WithColumn("Description").AsString(200).Nullable()
            .WithColumn("BuiltIn").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Created").AsString(40).NotNullable();

        Create.Table("RolePermissions")
            .WithColumn("RoleId").AsInt32().NotNullable().ForeignKey("Roles", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Permission").AsString(64).NotNullable();
        Create.Index("IX_RolePermissions_RoleId_Permission").OnTable("RolePermissions").OnColumn("RoleId").Ascending().OnColumn("Permission").Ascending().WithOptions().Unique();

        // Literals on purpose: a migration never follows later changes of the code.
        var created = DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        foreach (var (name, description) in new[]
        {
            ("Admin", "Everything, settings and users included."),
            ("Operator", "Everything except settings, users and secrets."),
            ("Observer", "Every page, read only; no chat, no change."),
            ("Guest", "The apps launcher only."),
        })
        {
            Insert.IntoTable("Roles").Row(new { Name = name, NameNormalized = name.ToLowerInvariant(), Description = description, BuiltIn = true, Created = created });
        }

        Create.Table("Users")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Username").AsString(64).NotNullable()
            .WithColumn("UsernameNormalized").AsString(64).NotNullable().Unique()
            .WithColumn("DisplayName").AsString(100).NotNullable()
            .WithColumn("PasswordHash").AsString(512).NotNullable()
            .WithColumn("RoleId").AsInt32().NotNullable().ForeignKey("Roles", "Id")
            .WithColumn("AvatarFile").AsString(100).Nullable()
            .WithColumn("Created").AsString(40).NotNullable()
            .WithColumn("LastLogin").AsString(40).Nullable()
            .WithColumn("Disabled").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("FailedAttempts").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("LockoutEnd").AsString(40).Nullable()
            .WithColumn("SecurityStamp").AsString(64).NotNullable()
            .WithColumn("ExternalIssuer").AsString(200).Nullable()
            .WithColumn("ExternalSubject").AsString(200).Nullable();
        Create.Index("IX_Users_External").OnTable("Users").OnColumn("ExternalIssuer").Ascending().OnColumn("ExternalSubject").Ascending().WithOptions().Unique();

        Create.Table("ApiKeys")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("UserId").AsInt32().NotNullable().ForeignKey("Users", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Name").AsString(100).NotNullable()
            .WithColumn("Prefix").AsString(16).NotNullable()
            .WithColumn("KeyHash").AsString(64).NotNullable().Unique()
            .WithColumn("Scope").AsString(8).NotNullable()
            .WithColumn("Created").AsString(40).NotNullable()
            .WithColumn("LastUsed").AsString(40).Nullable()
            .WithColumn("Expires").AsString(40).Nullable()
            .WithColumn("Revoked").AsString(40).Nullable();

        Create.Table("ChatConversations")
            .WithColumn("Id").AsString(36).PrimaryKey()
            .WithColumn("OwnerUserId").AsInt32().Nullable().ForeignKey("Users", "Id").OnDelete(System.Data.Rule.SetNull)
            .WithColumn("OwnerName").AsString(64).NotNullable()
            .WithColumn("Created").AsString(40).NotNullable();

        Create.Table("ChatTokens")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TokenHash").AsString(64).NotNullable().Unique()
            .WithColumn("UserId").AsInt32().NotNullable().ForeignKey("Users", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ConversationId").AsString(36).NotNullable()
            .WithColumn("Created").AsString(40).NotNullable()
            .WithColumn("Expires").AsString(40).NotNullable()
            .WithColumn("AbsoluteExpires").AsString(40).NotNullable();

        Create.Table("AuthAudit")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("At").AsString(40).NotNullable()
            .WithColumn("Event").AsString(40).NotNullable()
            .WithColumn("Actor").AsString(64).Nullable()
            .WithColumn("Subject").AsString(100).Nullable()
            .WithColumn("Detail").AsString(500).Nullable()
            .WithColumn("Client").AsString(64).Nullable();
    }

    public override void Down()
    {
        Delete.Table("AuthAudit");
        Delete.Table("ChatTokens");
        Delete.Table("ChatConversations");
        Delete.Table("ApiKeys");
        Delete.Table("Users");
        Delete.Table("RolePermissions");
        Delete.Table("Roles");
    }
}
