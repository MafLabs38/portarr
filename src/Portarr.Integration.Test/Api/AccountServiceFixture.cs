using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Portarr.Core.Accounts;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// The rules of the account services, on the real stores with a controllable clock (V7): the first-run race, the lockout
/// and when it ends, the last Admin, deleted users, the expiry of keys, chat tokens and sessions. The HTTP surface is
/// AccountsFixture and EndpointSecurityFixture.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AccountServiceFixture
{
    private const string Password = PortarrFactory.Password;

    private readonly TestDatabase _database;

    public AccountServiceFixture(TestDatabase database)
    {
        _database = database;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<PortarrFactory> FactoryAsync() => new(await _database.CreateOptionsAsync());

    private static async Task<(AccountIdentity Admin, IAccountService Accounts)> SetUpAsync(PortarrFactory factory)
    {
        var accounts = factory.Services.GetRequiredService<IAccountService>();
        var admin = await accounts.SetupAsync("admin", null, Password, null, Token);
        return (admin.Value!, accounts);
    }

    private static AccountActor ActorOf(AccountIdentity identity) => new(identity.Username, identity.UserId, null);

    private static async Task<int> RoleIdAsync(IAccountService accounts, string name) => (await accounts.ListRolesAsync(Token)).Single(r => r.Name == name).Id;

    [Fact]
    public async Task should_set_up_once_and_say_no_to_everyone_after()
    {
        await using var factory = await FactoryAsync();
        var accounts = factory.Services.GetRequiredService<IAccountService>();

        var first = await accounts.SetupAsync("admin", null, Password, null, Token);
        var second = await accounts.SetupAsync("other", null, Password, null, Token);

        first.Succeeded.Should().BeTrue();
        second.Error.Should().Be(AccountError.Conflict);
        (await accounts.SetupRequiredAsync(Token)).Should().BeFalse();
    }

    [Fact]
    public async Task should_lock_the_account_for_the_configured_time_and_not_a_second_less()
    {
        await using var factory = await FactoryAsync();
        var (_, accounts) = await SetUpAsync(factory);
        var options = factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value;

        for (var i = 0; i < options.MaxFailedAttempts; i++)
        {
            (await accounts.LoginAsync("admin", "not the password", null, Token)).Succeeded.Should().BeFalse();
        }

        (await accounts.LoginAsync("admin", Password, null, Token)).Succeeded.Should().BeFalse("locked, even with the right password");
        factory.Clock.Advance(options.Lockout - TimeSpan.FromSeconds(1));
        (await accounts.LoginAsync("admin", Password, null, Token)).Succeeded.Should().BeFalse("one second early");
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await accounts.LoginAsync("admin", Password, null, Token)).Succeeded.Should().BeTrue("the lockout is over");
    }

    [Fact]
    public async Task should_keep_the_last_enabled_admin_from_every_way_out()
    {
        await using var factory = await FactoryAsync();
        var (admin, accounts) = await SetUpAsync(factory);
        var system = new AccountActor("system", null, null);
        var adminRole = await RoleIdAsync(accounts, "Admin");
        var operatorRole = await RoleIdAsync(accounts, "Operator");
        var second = (await accounts.CreateUserAsync(ActorOf(admin), "second", null, Password, adminRole, Token)).Value!;

        (await accounts.UpdateUserAsync(system, second.Id, null, null, true, Token)).Succeeded.Should().BeTrue("another Admin remains");
        (await accounts.UpdateUserAsync(system, admin.UserId!.Value, null, null, true, Token)).Error.Should().Be(AccountError.Conflict);
        (await accounts.UpdateUserAsync(system, admin.UserId!.Value, null, operatorRole, null, Token)).Error.Should().Be(AccountError.Conflict);
        (await accounts.DeleteUserAsync(system, admin.UserId!.Value, Token)).Error.Should().Be(AccountError.Conflict);
        (await accounts.UpdateUserAsync(system, second.Id, null, null, false, Token)).Succeeded.Should().BeTrue();
        (await accounts.DeleteUserAsync(system, admin.UserId!.Value, Token)).Succeeded.Should().BeTrue("a second enabled Admin exists again");
    }

    [Fact]
    public async Task should_answer_not_found_for_a_user_deleted_under_a_password_or_profile_change()
    {
        await using var factory = await FactoryAsync();
        var (admin, accounts) = await SetUpAsync(factory);
        var viewerRole = await RoleIdAsync(accounts, "Guest");
        var gone = (await accounts.CreateUserAsync(ActorOf(admin), "gone", null, Password, viewerRole, Token)).Value!;
        await accounts.DeleteUserAsync(ActorOf(admin), gone.Id, Token);

        var reset = await accounts.ResetPasswordAsync(ActorOf(admin), gone.Id, "another long passphrase", Token);
        var profile = await accounts.UpdateProfileAsync(new AccountActor("gone", gone.Id, null), "Gone", Token);
        var avatar = await accounts.SetAvatarAsync(ActorOf(admin), gone.Id, null, Token);

        reset.Error.Should().Be(AccountError.NotFound);
        profile.Error.Should().Be(AccountError.NotFound);
        avatar.Error.Should().Be(AccountError.NotFound);
    }

    [Fact]
    public async Task should_refuse_a_user_name_that_differs_only_by_case()
    {
        await using var factory = await FactoryAsync();
        var (admin, accounts) = await SetUpAsync(factory);

        var sameName = await accounts.CreateUserAsync(ActorOf(admin), "ADMIN", null, Password, await RoleIdAsync(accounts, "Guest"), Token);

        sameName.Error.Should().Be(AccountError.Conflict);
    }

    [Fact]
    public async Task should_end_a_key_at_its_expiry_and_a_revoked_one_at_once()
    {
        await using var factory = await FactoryAsync();
        var (admin, _) = await SetUpAsync(factory);
        var keys = factory.Services.GetRequiredService<IApiKeyService>();
        var actor = ActorOf(admin);
        var expiring = (await keys.CreateAsync(actor, "expiring", ApiKeyService.FullScope, factory.Clock.GetUtcNow().AddDays(1), Token)).Value!;
        var revoked = (await keys.CreateAsync(actor, "revoked", ApiKeyService.FullScope, null, Token)).Value!;
        await keys.RevokeAsync(actor, revoked.Info.Id, Token);

        (await keys.AuthenticateAsync(expiring.Key, Token)).Should().NotBeNull();
        (await keys.AuthenticateAsync(revoked.Key, Token)).Should().BeNull();
        factory.Clock.Advance(TimeSpan.FromDays(1));
        (await keys.AuthenticateAsync(expiring.Key, Token)).Should().BeNull("it expires at the instant");
    }

    [Fact]
    public async Task should_expire_a_chat_token_after_its_idle_time_and_after_its_absolute_time_whatever_its_use()
    {
        await using var factory = await FactoryAsync();
        var (admin, _) = await SetUpAsync(factory);
        var chat = factory.Services.GetRequiredService<IChatAccess>();
        const string conversation = "0d5c2f1e-7a3b-4c8d-9e0f-1a2b3c4d5e6f";
        var token = await chat.MintTokenAsync(admin.UserId!.Value, conversation, Token);

        (await chat.AuthenticateTokenAsync(token, Token)).Should().NotBeNull();
        factory.Clock.Advance(ChatAccess.TokenLifetime + TimeSpan.FromMinutes(1));
        (await chat.AuthenticateTokenAsync(token, Token)).Should().BeNull("idle for longer than a token lasts");

        var fresh = await chat.MintTokenAsync(admin.UserId!.Value, conversation, Token);
        for (var i = 0; i < 30; i++)
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(25));
            await chat.MintTokenAsync(admin.UserId!.Value, conversation, Token);
        }

        (await chat.AuthenticateTokenAsync(fresh, Token)).Should().BeNull("a token never outlives its absolute lifetime, however it is renewed");
    }

    [Fact]
    public async Task should_not_hand_a_disabled_users_chat_token_an_identity()
    {
        await using var factory = await FactoryAsync();
        var (admin, accounts) = await SetUpAsync(factory);
        var chat = factory.Services.GetRequiredService<IChatAccess>();
        var operatorRole = await RoleIdAsync(accounts, "Operator");
        var user = (await accounts.CreateUserAsync(ActorOf(admin), "chatty", null, Password, operatorRole, Token)).Value!;
        var token = await chat.MintTokenAsync(user.Id, "0d5c2f1e-7a3b-4c8d-9e0f-1a2b3c4d5e6f", Token);
        (await chat.AuthenticateTokenAsync(token, Token)).Should().NotBeNull();

        await accounts.UpdateUserAsync(ActorOf(admin), user.Id, null, null, true, Token);

        (await chat.AuthenticateTokenAsync(token, Token)).Should().BeNull();
    }

    [Fact]
    public async Task should_end_a_session_after_the_configured_days()
    {
        await using var factory = await FactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();
        var days = factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value.SessionDays;
        (await client.GetAsync("/api/v1/auth/me", Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        factory.Clock.Advance(TimeSpan.FromDays(days) + TimeSpan.FromMinutes(1));

        (await client.GetAsync("/api/v1/auth/me", Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
