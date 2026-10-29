using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker;

/// <summary>
/// Decodes the logs endpoint. Without a TTY, Docker multiplexes stdout and stderr in frames:
/// an 8-byte header (stream type, 3 zero bytes, big-endian payload size) then the payload,
/// which may hold several lines or part of one. With a TTY, the stream is raw stdout.
/// Lines longer than <see cref="MaxLineLength"/> are cut and flagged, so a container writing
/// without newlines cannot make Portarr's memory grow.
/// </summary>
internal static class DockerLogReader
{
    public const int MaxLineLength = 16 * 1024;
    public const int MaxFrameSize = 1024 * 1024;

    private const int HeaderSize = 8;

    public static async IAsyncEnumerable<LogLine> ReadAsync(Stream stream, bool tty, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lines = new Dictionary<LogStream, LineSplitter> { [LogStream.Stdout] = new(), [LogStream.Stderr] = new() };
        var chunks = tty ? ReadRawAsync(stream, cancellationToken) : ReadFramesAsync(stream, cancellationToken);

        await foreach (var (logStream, chunk) in chunks)
        {
            foreach (var line in lines[logStream].Push(chunk.Span))
            {
                yield return ToLogLine(logStream, line);
            }
        }

        foreach (var (logStream, splitter) in lines)
        {
            if (splitter.Flush() is { } rest)
            {
                yield return ToLogLine(logStream, rest);
            }
        }
    }

    private static LogLine ToLogLine(LogStream logStream, (string Text, bool Truncated) line)
    {
        return DockerMapper.ParseLogLine(logStream, line.Text) with { Truncated = line.Truncated };
    }

    /// <summary>With a TTY: raw stdout. The buffer is reused, each chunk is consumed before the next read.</summary>
    private static async IAsyncEnumerable<(LogStream Stream, ReadOnlyMemory<byte> Chunk)> ReadRawAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            yield return (LogStream.Stdout, buffer.AsMemory(0, read));
        }
    }

    /// <summary>Without a TTY: multiplexed frames. A connection closed mid-frame ends the stream with what was received.</summary>
    private static async IAsyncEnumerable<(LogStream Stream, ReadOnlyMemory<byte> Chunk)> ReadFramesAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        while (await stream.ReadAtLeastAsync(header, HeaderSize, throwOnEndOfStream: false, cancellationToken) == HeaderSize)
        {
            var logStream = header[0] == 2 ? LogStream.Stderr : LogStream.Stdout;
            var size = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
            if (size is < 0 or > MaxFrameSize)
            {
                // Not the Docker framing (or a corrupted stream): stop rather than allocate it.
                yield break;
            }

            var payload = new byte[size];
            var read = await stream.ReadAtLeastAsync(payload, size, throwOnEndOfStream: false, cancellationToken);
            yield return (logStream, payload.AsMemory(0, read));
            if (read < size)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Splits decoded text into lines. Keeps the UTF-8 decoder state between chunks so a character
    /// split across two frames is not garbled, and only scans the new chunk for newlines.
    /// </summary>
    private sealed class LineSplitter
    {
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder _pending = new();
        private bool _truncated;

        public List<(string Text, bool Truncated)> Push(ReadOnlySpan<byte> bytes)
        {
            var chars = new char[_decoder.GetCharCount(bytes, flush: false)];
            _decoder.GetChars(bytes, chars, flush: false);

            var lines = new List<(string, bool)>();
            foreach (var character in chars)
            {
                if (character == '\n')
                {
                    lines.Add(Take());
                }
                else if (_pending.Length < MaxLineLength)
                {
                    _pending.Append(character);
                }
                else
                {
                    _truncated = true;
                }
            }

            return lines;
        }

        public (string Text, bool Truncated)? Flush()
        {
            return _pending.Length > 0 ? Take() : null;
        }

        private (string Text, bool Truncated) Take()
        {
            var line = (_pending.ToString().TrimEnd('\r'), _truncated);
            _pending.Clear();
            _truncated = false;
            return line;
        }
    }
}
