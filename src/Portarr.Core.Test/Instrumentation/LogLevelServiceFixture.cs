using Microsoft.Extensions.Logging;
using Portarr.Core.Configuration;
using Portarr.Core.Instrumentation;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Instrumentation;

public sealed class LogLevelServiceFixture : CoreTest<LogLevelService>
{
    [Fact]
    public async Task should_store_and_apply_the_level_when_set()
    {
        await Subject.SetAsync(LogLevel.Debug, TestContext.Current.CancellationToken);

        await Mocker.Get<IConfigRepository>().Received(1).SetAsync(LogLevelService.ConfigKey, "Debug", Arg.Any<CancellationToken>());
        Mocker.Get<IRuntimeLogLevel>().Received(1).Apply(LogLevel.Debug);
    }

    [Theory]
    [InlineData(LogLevel.Critical)]
    [InlineData(LogLevel.None)]
    public async Task should_refuse_levels_that_would_hide_errors(LogLevel level)
    {
        var set = () => Subject.SetAsync(level, TestContext.Current.CancellationToken);

        await set.Should().ThrowAsync<ArgumentOutOfRangeException>();
        Mocker.Get<IRuntimeLogLevel>().DidNotReceive().Apply(Arg.Any<LogLevel>());
    }

    [Fact]
    public async Task should_apply_the_stored_level_when_restoring()
    {
        Mocker.Get<IConfigRepository>().GetAsync(LogLevelService.ConfigKey, Arg.Any<CancellationToken>()).Returns("Warning");

        await Subject.RestoreAsync(TestContext.Current.CancellationToken);

        Mocker.Get<IRuntimeLogLevel>().Received(1).Apply(LogLevel.Warning);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Loud")]
    [InlineData("None")]
    public async Task should_fall_back_to_information_when_the_stored_level_is_missing_or_invalid(string? stored)
    {
        Mocker.Get<IConfigRepository>().GetAsync(LogLevelService.ConfigKey, Arg.Any<CancellationToken>()).Returns(stored);

        await Subject.RestoreAsync(TestContext.Current.CancellationToken);

        Mocker.Get<IRuntimeLogLevel>().Received(1).Apply(LogLevel.Information);
    }
}
