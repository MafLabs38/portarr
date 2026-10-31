using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Portarr.Core.Catalog;

namespace Portarr.Providers.Docker;

/// <summary>
/// One HTTP client per Docker endpoint: the Unix socket (unix:///var/run/docker.sock) or a TCP
/// endpoint, typically a read-only docker-socket-proxy (http://socket-proxy:2375).
/// </summary>
public sealed class DockerHttpClientFactory : IDisposable
{
    /// <summary>Caps connections to one Docker host: live log streams cannot exhaust it.</summary>
    public const int MaxConnections = 32;

    /// <summary>Docker's default plain-TCP port, for tcp:// endpoints without one.</summary>
    private const int DefaultTcpPort = 2375;

    /// <summary>
    /// Base address of Unix socket clients: the host name is ignored on a Unix socket, but HTTP needs one.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S5332:Using http protocol is insecure. Use https instead.", Justification = "Placeholder base address of a Unix domain socket: nothing leaves the machine.")]
    private const string UnixSocketBaseAddress = "http://docker/";

    private readonly Dictionary<(Uri Url, string? TokenVariable), HttpClient> _clients = [];
    private readonly Lock _lock = new();
    private readonly IConfiguration? _configuration;

    public DockerHttpClientFactory(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }

    /// <summary>The bearer token the endpoint asks for, from its environment variable; null when it asks for none or the variable is empty.</summary>
    public string? TokenOf(DockerEndpoint endpoint)
    {
        return endpoint.TokenVariable is { } variable && _configuration?[variable] is { Length: > 0 } token ? token : null;
    }

    public HttpClient Get(DockerEndpoint endpoint)
    {
        lock (_lock)
        {
            var key = (endpoint.Url, endpoint.TokenVariable);
            if (!_clients.TryGetValue(key, out var client))
            {
                // The token is read at each request, not once: a rotated token is used without a restart.
                client = Create(endpoint.Url, endpoint.TokenVariable is null ? null : () => TokenOf(endpoint));
                _clients[key] = client;
            }

            return client;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }

            _clients.Clear();
        }
    }

    internal static HttpClient Create(Uri url, Func<string?>? token = null)
    {
        if (url.Scheme == "unix")
        {
            var socketPath = url.AbsolutePath;
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

            return new HttpClient(WithToken(handler, token)) { BaseAddress = new Uri(UnixSocketBaseAddress), Timeout = Timeout.InfiniteTimeSpan };
        }

        // A base path is kept (the Portarr reader serves the Docker API under /v1/docker/); a bare address stays at the root.
        var path = url.AbsolutePath.EndsWith('/') ? url.AbsolutePath : url.AbsolutePath + "/";
        var builder = new UriBuilder(url) { Scheme = url.Scheme == "tcp" ? "http" : url.Scheme, Path = path };
        if (url.Scheme == "tcp" && url.IsDefaultPort)
        {
            builder.Port = DefaultTcpPort;
        }

        var tcpHandler = new SocketsHttpHandler { MaxConnectionsPerServer = MaxConnections };
        return new HttpClient(WithToken(tcpHandler, token)) { BaseAddress = builder.Uri, Timeout = Timeout.InfiniteTimeSpan };
    }

    private static HttpMessageHandler WithToken(HttpMessageHandler handler, Func<string?>? token)
    {
        return token is null ? handler : new BearerTokenHandler(token) { InnerHandler = handler };
    }
}
