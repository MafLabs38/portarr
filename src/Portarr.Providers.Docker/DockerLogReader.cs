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

        if (tty)
        {
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                foreach (var line in lines[LogStream.Stdout].Push(buffer.AsSpan(0, read)))
                {
                    yield return DockerMapper.ParseLogLine(LogStream.Stdout, line.Text) with { Truncated = line.Truncated };
                }
            }
        }
        else
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
                await stream.ReadExactlyAsync(payload, cancellationToken);
                foreach (var line in lines[logStream].Push(payload))
                {
                    yield return DockerMapper.ParseLogLine(logStream, line.Text) with { Truncated = line.Truncated };
                }
            }
        }

        foreach (var (logStream, splitter) in lines)
        {
            if (splitter.Flush() is { } rest)
            {
                yield return DockerMapper.ParseLogLine(logStream, rest.Text) with { Truncated = rest.Truncated };
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
