using Portarr.Core.Accounts;

namespace Portarr.Core.Test.Accounts;

/// <summary>Startup refuses a nonsensical session length or lockout, or half of the unattended first run.</summary>
public sealed class AuthOptionsValidatorFixture
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("root", "/run/secrets/admin", true)]
    [InlineData("root", null, false)]
    [InlineData(null, "/run/secrets/admin", false)]
    [InlineData("  ", "/run/secrets/admin", false)]
    public void should_ask_for_the_user_and_the_file_together(string? user, string? file, bool valid)
    {
        Validate(new AuthOptions { AdminUser = user, AdminPasswordFile = file }).Succeeded.Should().Be(valid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(365, true)]
    [InlineData(366, false)]
    public void should_bound_the_session_length_in_days(int days, bool valid)
    {
        Validate(new AuthOptions { SessionDays = days }).Succeeded.Should().Be(valid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void should_bound_the_failed_attempts_before_a_lockout(int attempts, bool valid)
    {
        Validate(new AuthOptions { MaxFailedAttempts = attempts }).Succeeded.Should().Be(valid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1440, true)]
    [InlineData(1441, false)]
    public void should_bound_the_lockout_in_minutes(int minutes, bool valid)
    {
        Validate(new AuthOptions { LockoutMinutes = minutes }).Succeeded.Should().Be(valid);
    }

    [Fact]
    public void should_accept_the_defaults()
    {
        Validate(new AuthOptions()).Succeeded.Should().BeTrue();
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(AuthOptions options) => new AuthOptionsValidator().Validate(null, options);
}
