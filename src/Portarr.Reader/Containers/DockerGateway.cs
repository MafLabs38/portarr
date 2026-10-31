using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Portarr.Reader.Containers;

/// <summary>
/// A filtered, read-only window on the Docker Engine API (D-100). It is not a proxy: each method asks Docker for one
/// fixed GET path with the few parameters it accepts, and returns only what Portarr reads. Every other path, verb or
/// parameter has no route, and container environment variables are never forwarded (the inspect is reduced to the id
/// and the TTY flag).
/// </summary>
public sealed partial class DockerGateway : IDisposable
{
    /// <summary>Most log lines one request may ask for, like Portarr's own limit.</summary>
    public const int MaxLogLines = 5000;

    private const int MaxConnections = 32;

    /// <summary>Base address of a Unix socket client: the host name is ignored on a socket, but HTTP needs one.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S5332:Using http protocol is insecure. Use https instead.", Justification = "Placeholder base address of a Unix domain socket: nothing leaves the machine.")]
    private const string UnixSocketBaseAddress = "http://docker/";

    private readonly HttpClient _client;

    public DockerGateway(ReaderOptions options)
    {
        _client = Create(new Uri(options.Docker));
    }

    /// <summary>Container names go into URL paths: only Docker's own name characters are accepted.</summary>
    public static bool IsValidName(string name) => NamePattern().IsMatch(name);

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,254}$")]
    private static partial Regex NamePattern();

    /// <summary>The host's capacity: the CPU count and the memory, nothing else of <c>/info</c>.</summary>
    public async Task<IResult> InfoAsync(CancellationToken cancellationToken)
    {
        return await GuardAsync(async () =>
        {
            using var response = await _client.GetAsync("info", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Upstream(response.StatusCode);
            }

            var info = await ReadObjectAsync(response, cancellationToken);
            return Json(new JsonObject { ["NCPU"] = (int?)info["NCPU"], ["MemTotal"] = (long?)info["MemTotal"] });
        });
    }

    public async Task<IResult> ListAsync(bool all, CancellationToken cancellationToken)
    {
        return await GuardAsync(async () =>
        {
            using var response = await _client.GetAsync($"containers/json?all={(all ? "true" : "false")}", cancellationToken);
            return response.IsSuccessStatusCode
                ? Results.Content(await response.Content.ReadAsStringAsync(cancellationToken), "application/json")
                : Upstream(response.StatusCode);
        });
    }

    /// <summary>The container's id and whether it has a TTY (the log stream is framed differently): the rest of the inspect holds its environment.</summary>
    public async Task<IResult> InspectAsync(string name, CancellationToken cancellationToken)
    {
        return await GuardAsync(async () =>
        {
            using var response = await _client.GetAsync($"containers/{name}/json", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Upstream(response.StatusCode);
            }

            var inspect = await ReadObjectAsync(response, cancellationToken);
            return Json(new JsonObject { ["Id"] = (string?)inspect["Id"], ["Config"] = new JsonObject { ["Tty"] = (bool?)inspect["Config"]?["Tty"] ?? false } });
        });
    }

    /// <summary>One usage sample: Docker waits about a second for two readings, so the CPU delta is meaningful.</summary>
    public async Task<IResult> StatsAsync(string name, CancellationToken cancellationToken)
    {
        return await GuardAsync(async () =>
        {
            using var response = await _client.GetAsync($"containers/{name}/stats?stream=false", cancellationToken);
            return response.IsSuccessStatusCode
                ? Results.Content(await response.Content.ReadAsStringAsync(cancellationToken), "application/json")
                : Upstream(response.StatusCode);
        });
    }

    /// <summary>
    /// Streams the container's logs as Docker frames them. The response ends when the container stops writing or the
    /// caller goes away; every chunk is flushed so a followed log is live.
    /// </summary>
    public async Task LogsAsync(HttpContext context, string name, LogQuery query, CancellationToken cancellationToken)
    {
        var path = $"containers/{name}/logs?stdout={Flag(query.Stdout)}&stderr={Flag(query.Stderr)}&timestamps={Flag(query.Timestamps)}&tail={Math.Clamp(query.Tail, 0, MaxLogLines)}&follow={Flag(query.Follow)}";
        try
        {
            using var response = await _client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await Upstream(response.StatusCode).ExecuteAsync(context);
                return;
            }

            context.Response.ContentType = "application/octet-stream";
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (HttpRequestException) when (!context.Response.HasStarted)
        {
            await Unreachable().ExecuteAsync(context);
        }
        catch (OperationCanceledException)
        {
            // The caller closed the stream: nothing to report.
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static IResult Json(JsonObject body) => Results.Content(body.ToJsonString(), "application/json");

    private static async Task<JsonObject> ReadObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject
            ?? throw new InvalidDataException("Docker answered something other than a JSON object.");
    }

    /// <summary>Docker's 404 stays a 404 (no such container); any other refusal is the reader's bad gateway, never Docker's text.</summary>
    private static IResult Upstream(HttpStatusCode status)
    {
        return status == HttpStatusCode.NotFound
            ? Results.NotFound()
            : Results.Json(new { message = "Docker refused the request." }, statusCode: StatusCodes.Status502BadGateway);
    }

    private static IResult Unreachable()
    {
        return Results.Json(new { message = "Docker is not reachable." }, statusCode: StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> GuardAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (HttpRequestException)
        {
            return Unreachable();
        }
        catch (InvalidDataException)
        {
            return Results.Json(new { message = "Docker answered something unexpected." }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static HttpClient Create(Uri endpoint)
    {
        if (endpoint.Scheme == "unix")
        {
            var socketPath = endpoint.AbsolutePath;
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = MaxConnections,
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            };

            return new HttpClient(handler) { BaseAddress = new Uri(UnixSocketBaseAddress), Timeout = Timeout.InfiniteTimeSpan };
        }

        var builder = new UriBuilder(endpoint) { Scheme = endpoint.Scheme == "tcp" ? "http" : endpoint.Scheme, Path = "/" };
        return new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = MaxConnections }) { BaseAddress = builder.Uri, Timeout = Timeout.InfiniteTimeSpan };
    }
}

/// <summary>The only parameters of a logs request: which streams, timestamps, how many lines, and whether to follow.</summary>
public readonly record struct LogQuery(bool Stdout, bool Stderr, bool Timestamps, int Tail, bool Follow);
