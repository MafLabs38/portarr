using Portarr.Core.Infrastructure;

namespace Portarr.Providers.NodeExporter.Test;

/// <summary>A hostile or broken agent must not be able to fill Portarr's memory: the size cap counts the bytes read, not the lines returned.</summary>
public sealed class NodeExporterCapFixture
{
    private static readonly HostMachineKey Key = new("server-1", "server-1");

    [Fact]
    public async Task should_stop_an_answer_that_is_one_endless_line_before_it_fills_the_memory()
    {
        var sampler = new NodeExporterSampler();
        await using var endless = new EndlessStream((byte)'n');

        var read = () => sampler.ReadAsync(Key, endless, TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidDataException>();
        endless.BytesServed.Should().BeLessThan(NodeExporterSampler.MaxCharacters + 1024 * 1024, "the reader stopped soon after the cap");
    }

    [Fact]
    public async Task should_still_read_an_answer_just_under_the_cap()
    {
        var sampler = new NodeExporterSampler();
        var filler = string.Concat(Enumerable.Repeat("# a comment line that the parser skips\n", 1000));
        var text = $"{filler}node_memory_MemTotal_bytes 1000\nnode_memory_MemAvailable_bytes 400\n";

        var reading = await sampler.ReadAsync(Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), TestContext.Current.CancellationToken);

        reading.Memory.TotalBytes.Should().Be(1000);
    }

    private sealed class EndlessStream(byte value) : Stream
    {
        public long BytesServed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, value, offset, count);
            BytesServed += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            buffer.Span.Fill(value);
            BytesServed += buffer.Length;
            return ValueTask.FromResult(buffer.Length);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
