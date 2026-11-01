using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portarr.Core.Accounts;
using Portarr.Core.Configuration;

namespace Portarr.Core.Test.Accounts;

/// <summary>The one-time code that guards the first-run screen.</summary>
public sealed class SetupCodeFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-setup-code-").FullName;

    [Fact]
    public void should_need_no_code_until_one_is_issued()
    {
        var subject = Create();

        subject.Required.Should().BeFalse();
        subject.Matches("anything").Should().BeFalse();
    }

    [Fact]
    public void should_issue_a_code_written_to_the_data_folder_that_matches_in_any_case_and_without_dashes()
    {
        var subject = Create();

        subject.Issue();

        var code = File.ReadAllText(Path.Combine(_folder, SetupCode.FileName)).Trim();
        code.Should().MatchRegex("^[0-9A-F]{4}(-[0-9A-F]{4}){3}$");
        subject.Required.Should().BeTrue();
        subject.Matches(code).Should().BeTrue();
        subject.Matches(code.ToLowerInvariant().Replace("-", string.Empty)).Should().BeTrue();
        subject.Matches($"  {code}  ").Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000-0000-0000-0000")]
    public void should_refuse_a_code_that_is_not_the_issued_one(string? presented)
    {
        var subject = Create();
        subject.Issue();

        subject.Matches(presented).Should().BeFalse();
    }

    [Fact]
    public void should_forget_the_code_and_delete_its_file_once_cleared()
    {
        var subject = Create();
        subject.Issue();
        var code = File.ReadAllText(Path.Combine(_folder, SetupCode.FileName)).Trim();

        subject.Clear();

        subject.Required.Should().BeFalse();
        subject.Matches(code).Should().BeFalse();
        File.Exists(Path.Combine(_folder, SetupCode.FileName)).Should().BeFalse();
    }

    [Fact]
    public void should_issue_nothing_when_the_guard_is_turned_off()
    {
        var subject = Create(new AuthOptions { RequireSetupCode = false });

        subject.Issue();

        subject.Required.Should().BeFalse();
        File.Exists(Path.Combine(_folder, SetupCode.FileName)).Should().BeFalse();
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    private SetupCode Create(AuthOptions? auth = null) =>
        new(Options.Create(new PortarrOptions { DataFolder = _folder }), Options.Create(auth ?? new AuthOptions()), NullLogger<SetupCode>.Instance);
}
