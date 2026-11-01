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
    public async Task should_do_nothing_without_a_user_name_and_a_file()
    {
        await Bootstrap(new AuthOptions()).RunAsync(TestContext.Current.CancellationToken);

        await _accounts.DidNotReceive().SetupAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_do_nothing_once_a_user_exists()
    {
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
        await _accounts.Received(1).SetupAsync("root", null, "short", "startup", Arg.Any<CancellationToken>());
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
