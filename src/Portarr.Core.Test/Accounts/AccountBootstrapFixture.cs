using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portarr.Core.Accounts;

namespace Portarr.Core.Test.Accounts;

/// <summary>The unattended first run (F-SET-07): an administrator from a user name and a password file, once.</summary>
public sealed class AccountBootstrapFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-bootstrap-").FullName;
    private readonly IAccountService _accounts = Substitute.For<IAccountService>();
    private readonly ISetupCode _setupCode = Substitute.For<ISetupCode>();

    public AccountBootstrapFixture()
    {
        _accounts.SetupRequiredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _accounts.SetupAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(AccountResult<AccountIdentity>.Fail(AccountError.Invalid, "refused"));
    }

    [Fact]
    public async Task should_create_the_administrator_from_the_password_file_without_its_line_ending()
    {
        var file = PasswordFile("a long throwaway passphrase\r\n");

        await Bootstrap(new AuthOptions { AdminUser = "root", AdminPasswordFile = file }).RunAsync(TestContext.Current.CancellationToken);

        await _accounts.Received(1).SetupAsync("root", null, "a long throwaway passphrase", "startup", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_do_nothing_without_a_user_and_a_file_or_once_a_user_exists()
    {
        await Bootstrap(new AuthOptions()).RunAsync(TestContext.Current.CancellationToken);

        _accounts.SetupRequiredAsync(Arg.Any<CancellationToken>()).Returns(false);
        await Bootstrap(new AuthOptions { AdminUser = "root", AdminPasswordFile = PasswordFile("a long throwaway passphrase") }).RunAsync(TestContext.Current.CancellationToken);

        await _accounts.DidNotReceive().SetupAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_issue_a_setup_code_when_nobody_was_created()
    {
        await Bootstrap(new AuthOptions()).RunAsync(TestContext.Current.CancellationToken);

        _setupCode.Received(1).Issue();
    }

    [Fact]
    public async Task should_issue_no_setup_code_when_the_administrator_was_created_or_one_exists()
    {
        _accounts.SetupAsync("root", null, "a long throwaway passphrase", "startup", Arg.Any<CancellationToken>())
            .Returns(AccountResult<AccountIdentity>.Ok(new AccountIdentity(1, "root", "root", "Admin", new HashSet<string>(), "stamp", null)));
        await Bootstrap(new AuthOptions { AdminUser = "root", AdminPasswordFile = PasswordFile("a long throwaway passphrase") }).RunAsync(TestContext.Current.CancellationToken);

        _accounts.SetupRequiredAsync(Arg.Any<CancellationToken>()).Returns(false);
        await Bootstrap(new AuthOptions()).RunAsync(TestContext.Current.CancellationToken);

        _setupCode.DidNotReceive().Issue();
    }

    [Fact]
    public async Task should_leave_the_setup_screen_available_when_the_password_file_cannot_be_read()
    {
        var run = () => Bootstrap(new AuthOptions { AdminUser = "root", AdminPasswordFile = Path.Combine(_folder, "missing") }).RunAsync(TestContext.Current.CancellationToken);

        await run.Should().NotThrowAsync();
        await _accounts.DidNotReceive().SetupAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_fail_the_start_when_the_account_is_refused()
    {
        var run = () => Bootstrap(new AuthOptions { AdminUser = "root", AdminPasswordFile = PasswordFile("short") }).RunAsync(TestContext.Current.CancellationToken);

        await run.Should().NotThrowAsync("the setup screen stays available");
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("root", "/run/secrets/admin", true)]
    [InlineData("root", null, false)]
    [InlineData(null, "/run/secrets/admin", false)]
    public void should_ask_for_the_user_and_the_file_together(string? user, string? file, bool valid)
    {
        var result = new AuthOptionsValidator().Validate(null, new AuthOptions { AdminUser = user, AdminPasswordFile = file });

        result.Succeeded.Should().Be(valid);
    }

    [Theory]
    [InlineData(0, 5, 5, false)]
    [InlineData(366, 5, 5, false)]
    [InlineData(7, 0, 5, false)]
    [InlineData(7, 5, 0, false)]
    [InlineData(7, 5, 1441, false)]
    [InlineData(7, 5, 5, true)]
    public void should_refuse_a_nonsensical_session_length_or_lockout(int days, int attempts, int minutes, bool valid)
    {
        var result = new AuthOptionsValidator().Validate(null, new AuthOptions { SessionDays = days, MaxFailedAttempts = attempts, LockoutMinutes = minutes });

        result.Succeeded.Should().Be(valid);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    private string PasswordFile(string content)
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, content);
        return path;
    }

    private AccountBootstrap Bootstrap(AuthOptions options) => new(_accounts, _setupCode, Options.Create(options), NullLogger<AccountBootstrap>.Instance);
}
