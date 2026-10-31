using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Portarr.Reader.Containers;

/// <summary>
/// A filtered, read-only window on the Docker Engine API (D-100, D-102). It is not a proxy: each method asks Docker for one
/// fixed GET path with the few parameters it accepts, and returns only the fields Portarr reads. Every other path, verb or
/// parameter has no route; the container list loses what it carries beyond those fields (a command line often holds a
/// secret); the inspect is reduced to the id and the TTY flag, because the rest holds the container's environment variables.
/// </summary>
public sealed partial class DockerGateway : IDisposable
{
    /// <summary>Most log lines one request may ask for, like Portarr's own limit.</summary>
    public const int MaxLogLines = 5000;

    /// <summary>How long Docker may take to answer a request that is not a stream: a hung call must not hold one of the connections for ever.</summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

    private const int MaxConnections = 32;
    private const int LogBufferSize = 16 * 1024;

    /// <summary>The fields of a listed container that Portarr's Docker connector reads (the others are not forwarded).</summary>
    private static readonly string[] ListedFields = ["Id", "Names", "Image", "State", "Status", "Created", "Labels", "Ports", "Mounts"];

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
        return await GuardAsync(async token =>
        {
            using var response = await _client.GetAsync("info", token);
            if (!response.IsSuccessStatusCode)
            {
                return Upstream(response.StatusCode);
            }

            var info = await ReadAsync(response, token);
            return Json(new JsonObject { ["NCPU"] = (int?)info["NCPU"], ["MemTotal"] = (long?)info["MemTotal"] });
        }, cancellationToken);
    }

    public async Task<IResult> ListAsync(bool all, CancellationToken cancellationToken)
    {
        return await GuardAsync(async token =>
        {
            using var response = await _client.GetAsync($"containers/json?all={Flag(all)}", token);
            if (!response.IsSuccessStatusCode)
            {
                return Upstream(response.StatusCode);
            }

            var list = new JsonArray();
            foreach (var container in (await ReadAsync(response, token)) is JsonArray array ? array : [])
            {
                var kept = new JsonObject();
                foreach (var field in ListedFields)
                {
                    if (container?[field] is { } value)
                    {
                        kept[field] = value.DeepClone();
                    }
                }

                list.Add((JsonNode?)kept);
            }

            return Json(list);
        }, cancellationToken);
    }

    /// <summary>The container's id and whether it has a TTY (the log stream is framed differently): the rest of the inspect holds its environment.</summary>
    public async Task<IResult> InspectAsync(string name, CancellationToken cancellationToken)
    {
        return await GuardAsync(async token =>
        {
            using var response = await _client.GetAsync($"containers/{name}/json", token);
            if (!response.IsSuccessStatusCode)
            {
                return Upstream(response.StatusCode);
            }

            var inspect = await ReadAsync(response, token);
            return Json(new JsonObject { ["Id"] = (string?)inspect["Id"], ["Config"] = new JsonObject { ["Tty"] = (bool?)inspect["Config"]?["Tty"] ?? false } });
        }, cancellationToken);
    }

    /// <summary>
    /// One usage sample, never a stream. <paramref name="oneShot"/> is passed on as asked: Portarr's infrastructure loop asks for
    /// Docker's one-shot sample (no wait for a second reading) every few seconds and measures the CPU itself.
    /// </summary>
    public async Task<IResult> StatsAsync(string name, bool oneShot, CancellationToken cancellationToken)
    {
        return await GuardAsync(async token =>
        {
            using var response = await _client.GetAsync($"containers/{name}/stats?stream=false{(oneShot ? "&one-shot=true" : string.Empty)}", token);
            return response.IsSuccessStatusCode
                ? Results.Content(await response.Content.ReadAsStringAsync(token), "application/json")
                : Upstream(response.StatusCode);
        }, cancellationToken);
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
            var buffer = new byte[LogBufferSize];
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller closed the stream: nothing to report.
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static IResult Json(JsonNode body) => Results.Content(body.ToJsonString(), "application/json");

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Docker answered an empty body.");
    }

    private static IResult Error(int status, string message) => Results.Json(new ErrorResponse(message), ReaderJsonContext.Default.ErrorResponse, statusCode: status);

    /// <summary>Docker's 404 stays a 404 (no such container); any other refusal is the reader's bad gateway, never Docker's text.</summary>
    private static IResult Upstream(HttpStatusCode status)
    {
        return status == HttpStatusCode.NotFound ? Results.NotFound() : Error(StatusCodes.Status502BadGateway, "Docker refused the request.");
    }

    private static IResult Unreachable() => Error(StatusCodes.Status502BadGateway, "Docker is not reachable.");

    /// <summary>Runs a call that is not a stream, with a time limit, turning what can go wrong into a short answer.</summary>
    private static async Task<IResult> GuardAsync(Func<CancellationToken, Task<IResult>> action, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallTimeout);
        try
        {
            return await action(timeout.Token);
        }
        catch (HttpRequestException)
        {
            return Unreachable();
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException or InvalidCastException or FormatException)
        {
            return Error(StatusCodes.Status502BadGateway, "Docker answered something unexpected.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(StatusCodes.Status504GatewayTimeout, "Docker did not answer in time.");
        }
    }

    /// <summary>
    /// The HTTP client of the Docker endpoint, on a Unix socket or over TCP. It repeats Portarr's Docker connector on purpose: the
    /// reader depends on none of Portarr's assemblies.
    /// </summary>
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
