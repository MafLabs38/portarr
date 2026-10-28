using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker;

/// <summary>
/// Decodes the logs endpoint. Without a TTY, Docker multiplexes stdout and stderr in frames:
/// an 8-byte header (stream type, 3 zero bytes, big-endian payload size) then the payload,
/// which may hold several lines or part of one. With a TTY, the stream is raw stdout.
/// </summary>
internal static class DockerLogReader
{
    private const int HeaderSize = 8;

    public static async IAsyncEnumerable<LogLine> ReadAsync(Stream stream, bool tty, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (tty)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                yield return DockerMapper.ParseLogLine(LogStream.Stdout, line);
            }

            yield break;
        }

        var header = new byte[HeaderSize];
        var pending = new Dictionary<LogStream, StringBuilder>
        {
            [LogStream.Stdout] = new(),
            [LogStream.Stderr] = new(),
        };

        while (await ReadFrameHeaderAsync(stream, header, cancellationToken))
        {
            var logStream = header[0] == 2 ? LogStream.Stderr : LogStream.Stdout;
            var size = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
            var payload = new byte[size];
            await stream.ReadExactlyAsync(payload, cancellationToken);

            var buffer = pending[logStream].Append(Encoding.UTF8.GetString(payload));
            var text = buffer.ToString();
            var lastNewLine = text.LastIndexOf('\n');
            if (lastNewLine < 0)
            {
                continue;
            }

            buffer.Clear().Append(text, lastNewLine + 1, text.Length - lastNewLine - 1);
            foreach (var line in text[..lastNewLine].Split('\n'))
            {
                yield return DockerMapper.ParseLogLine(logStream, line.TrimEnd('\r'));
            }
        }

        foreach (var (logStream, rest) in pending.Where(p => p.Value.Length > 0))
        {
            yield return DockerMapper.ParseLogLine(logStream, rest.ToString());
        }
    }

    private static async Task<bool> ReadFrameHeaderAsync(Stream stream, byte[] header, CancellationToken cancellationToken)
    {
        var read = await stream.ReadAtLeastAsync(header, HeaderSize, throwOnEndOfStream: false, cancellationToken);
        return read == HeaderSize;
    }
}
