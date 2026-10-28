using System.Buffers.Binary;
using System.Text;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker.Test;

public sealed class DockerLogReaderFixture
{
    [Fact]
    public async Task should_demultiplex_stdout_and_stderr_frames()
    {
        using var stream = Frames((1, "2026-01-01T00:00:00Z out one\n"), (2, "2026-01-01T00:00:01Z err one\n"));

        var lines = await ReadAllAsync(stream, tty: false);

        lines.Select(l => (l.Stream, l.Text)).Should().Equal((LogStream.Stdout, "out one"), (LogStream.Stderr, "err one"));
    }

    [Fact]
    public async Task should_join_a_line_split_across_frames_and_split_several_lines_in_one_frame()
    {
        using var stream = Frames((1, "first li"), (1, "ne\nsecond\nthi"), (1, "rd\n"));

        var lines = await ReadAllAsync(stream, tty: false);

        lines.Select(l => l.Text).Should().Equal("first line", "second", "third");
    }

    [Fact]
    public async Task should_flush_a_last_line_without_newline()
    {
        using var stream = Frames((1, "done"));

        var lines = await ReadAllAsync(stream, tty: false);

        lines.Should().ContainSingle().Which.Text.Should().Be("done");
    }

    [Fact]
    public async Task should_read_raw_lines_when_the_container_has_a_tty()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("one\r\ntwo\n"));

        var lines = await ReadAllAsync(stream, tty: true);

        lines.Select(l => (l.Stream, l.Text)).Should().Equal((LogStream.Stdout, "one"), (LogStream.Stdout, "two"));
    }

    private static MemoryStream Frames(params (byte Stream, string Text)[] frames)
    {
        var buffer = new MemoryStream();
        foreach (var (type, text) in frames)
        {
            var payload = Encoding.UTF8.GetBytes(text);
            var header = new byte[8];
            header[0] = type;
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), payload.Length);
            buffer.Write(header);
            buffer.Write(payload);
        }

        buffer.Position = 0;
        return buffer;
    }

    private static async Task<List<LogLine>> ReadAllAsync(Stream stream, bool tty)
    {
        var lines = new List<LogLine>();
        await foreach (var line in DockerLogReader.ReadAsync(stream, tty, TestContext.Current.CancellationToken))
        {
            lines.Add(line);
        }

        return lines;
    }
}
