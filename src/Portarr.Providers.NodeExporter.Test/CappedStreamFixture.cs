namespace Portarr.Providers.NodeExporter.Test;

/// <summary>The byte cap on a scrape: exactly the limit passes, one byte more fails, whichever way the stream is read.</summary>
public sealed class CappedStreamFixture
{
    private const int Limit = 10;

    [Theory]
    [InlineData(Limit, false)]
    [InlineData(Limit + 1, true)]
    public async Task should_fail_only_beyond_the_limit(int size, bool fails)
    {
        await using var capped = new CappedStream(new MemoryStream(new byte[size]), Limit);

        var copy = () => capped.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken);

        if (fails)
        {
            await copy.Should().ThrowAsync<InvalidDataException>();
        }
        else
        {
            await copy.Should().NotThrowAsync();
        }
    }

    [Fact]
    public void should_count_the_synchronous_reads_too()
    {
        using var capped = new CappedStream(new MemoryStream(new byte[Limit + 1]), Limit);
        var buffer = new byte[Limit + 1];

        var read = () => capped.Read(buffer, 0, buffer.Length);

        read.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task should_add_up_the_reads_of_the_array_overload()
    {
        await using var capped = new CappedStream(new MemoryStream(new byte[Limit + 1]), Limit);
        var buffer = new byte[6];

        (await capped.ReadAsync(buffer, 0, 6, TestContext.Current.CancellationToken)).Should().Be(6);
        var second = () => capped.ReadAsync(buffer, 0, 5, TestContext.Current.CancellationToken);

        await second.Should().ThrowAsync<InvalidDataException>("6 + 5 bytes exceed the limit of 10");
    }
}
