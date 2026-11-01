using Portarr.Core.Accounts;

namespace Portarr.Core.Test.Accounts;

/// <summary>The pure rules of accounts (V7): the permission list, the built-in roles, key scopes and input checks.</summary>
public sealed class AccountRulesFixture
{
    [Fact]
    public void should_define_the_built_in_roles_from_the_closed_permission_list()
    {
        var admin = BuiltInRoles.Find("admin")!.Permissions;
        var op = BuiltInRoles.Find("Operator")!.Permissions;
        var observer = BuiltInRoles.Find("Observer")!.Permissions;
        var guest = BuiltInRoles.Find("Guest")!.Permissions;

        admin.Should().BeEquivalentTo(Permissions.Keys);
        op.Should().NotContain([Permissions.UsersManage, Permissions.SettingsManage, Permissions.SecretsView]).And.Contain([Permissions.CatalogWrite, Permissions.ChatUse]);
        observer.Should().OnlyContain(p => p.EndsWith(".view", StringComparison.Ordinal)).And.NotContain(Permissions.SecretsView).And.NotContain(Permissions.ChatUse);
        guest.Should().BeEquivalentTo([Permissions.AppsView]);
        Permissions.Assignable.Should().NotContain(Permissions.SecretsView);
    }

    [Fact]
    public void should_keep_a_read_key_to_the_view_permissions_of_the_owner_and_a_full_key_to_all_of_them()
    {
        var owner = new HashSet<string> { Permissions.AppsView, Permissions.CatalogWrite, Permissions.ChatUse, Permissions.DataView };

        ApiKeyService.Scoped(owner, ApiKeyService.ReadScope).Should().BeEquivalentTo([Permissions.AppsView, Permissions.DataView]);
        ApiKeyService.Scoped(owner, ApiKeyService.FullScope).Should().BeEquivalentTo(owner);
        ApiKeyService.Scoped(new HashSet<string> { Permissions.AppsView }, ApiKeyService.FullScope).Should().NotContain(Permissions.UsersManage);
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("a.b-c_d", true)]
    [InlineData("ab", false)]
    [InlineData("-admin", false)]
    [InlineData("has space", false)]
    [InlineData("a\nb", false)]
    [InlineData("admin\n", false)]
    [InlineData("abc", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456", false)]
    [InlineData("", false)]
    public void should_check_user_names(string username, bool valid)
    {
        (AccountService.ValidateUsername(username) is null).Should().Be(valid);
    }

    [Theory]
    [InlineData("a long enough passphrase", "someone", true)]
    [InlineData("short", "someone", false)]
    [InlineData("someone-someone", "someone-someone", false)]
    public void should_check_passwords(string password, string username, bool valid)
    {
        (AccountService.ValidatePassword(password, username) is null).Should().Be(valid);
    }

    [Fact]
    public void should_stop_a_key_the_instant_it_expires_or_is_revoked()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        ApiKeyInfo Key(DateTimeOffset? expires, DateTimeOffset? revoked) => new(1, 1, "ci", "ptr_abc123", "full", now.AddDays(-1), null, expires, revoked);

        Key(null, null).IsActive(now).Should().BeTrue("a key without an expiry lasts");
        Key(now.AddSeconds(1), null).IsActive(now).Should().BeTrue();
        Key(now, null).IsActive(now).Should().BeFalse("it expires at the instant, not after");
        Key(now.AddSeconds(-1), null).IsActive(now).Should().BeFalse();
        Key(null, now.AddHours(-1)).IsActive(now).Should().BeFalse("a revoked key never comes back");
    }

    [Fact]
    public void should_hash_a_key_to_64_hex_characters_and_clean_audit_text()
    {
        ApiKeyService.Hash("ptr_x").Should().MatchRegex("^[0-9a-f]{64}$");
        AuthAuditLog.Clean("evil\r\nINJECTED", 64).Should().Be("evil  INJECTED");
        AuthAuditLog.Clean(new string('a', 300), 64)!.Length.Should().Be(64);
        AuthAuditLog.Clean("  ", 64).Should().BeNull();
    }
}
