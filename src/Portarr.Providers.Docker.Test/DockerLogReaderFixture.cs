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
    public async Task should_cut_and_flag_a_line_longer_than_the_limit()
    {
        using var stream = Frames((1, new string('x', DockerLogReader.MaxLineLength + 10) + "\nnext\n"));

        var lines = await ReadAllAsync(stream, tty: false);

        lines[0].Should().Match<LogLine>(l => l.Text.Length == DockerLogReader.MaxLineLength && l.Truncated);
        lines[1].Should().Match<LogLine>(l => l.Text == "next" && !l.Truncated);
    }

    [Fact]
    public async Task should_keep_a_character_split_across_two_frames()
    {
        var euro = Encoding.UTF8.GetBytes("€\n");
        using var stream = RawFrames((1, euro[..1]), (1, euro[1..]));

        (await ReadAllAsync(stream, tty: false)).Single().Text.Should().Be("€");
    }

    [Fact]
    public async Task should_stop_on_a_frame_size_that_is_not_docker_framing()
    {
        var header = new byte[8];
        header[0] = 1;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), int.MaxValue);
        using var stream = new MemoryStream(header);

        (await ReadAllAsync(stream, tty: false)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_end_cleanly_when_the_connection_closes_mid_frame()
    {
        var header = new byte[8];
        header[0] = 1;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 100);
        using var stream = new MemoryStream([.. header, .. Encoding.UTF8.GetBytes("partial")]);

        (await ReadAllAsync(stream, tty: false)).Should().ContainSingle().Which.Text.Should().Be("partial");
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
        return RawFrames(frames.Select(f => (f.Stream, Encoding.UTF8.GetBytes(f.Text))).ToArray());
    }

    private static MemoryStream RawFrames(params (byte Stream, byte[] Payload)[] frames)
    {
        var buffer = new MemoryStream();
        foreach (var (type, payload) in frames)
        {
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
