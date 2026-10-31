namespace Portarr.Providers.NodeExporter.Test;

/// <summary>The byte cap on a scrape: exactly the limit passes, one byte more fails, whichever way the stream is read.</summary>
public sealed class CappedStreamFixture
{
    private const int Limit = 10;

    [Fact]
    public async Task should_let_exactly_the_limit_through_and_fail_on_one_byte_more()
    {
        await using var atLimit = new CappedStream(new MemoryStream(new byte[Limit]), Limit);
        await using var over = new CappedStream(new MemoryStream(new byte[Limit + 1]), Limit);

        var passing = () => atLimit.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken);
        var failing = () => over.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken);

        await passing.Should().NotThrowAsync();
        await failing.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void should_count_the_synchronous_reads_too()
    {
        using var capped = new CappedStream(new MemoryStream(new byte[Limit + 1]), Limit);
        var buffer = new byte[Limit + 1];

        var read = () => capped.Read(buffer, 0, buffer.Length);

        read.Should().Throw<InvalidDataException>();
    }
}
