using System.Net.Sockets;
using Portarr.Core.Catalog;

namespace Portarr.Providers.Docker;

/// <summary>
/// One HTTP client per Docker endpoint: the Unix socket (unix:///var/run/docker.sock) or a TCP
/// endpoint, typically a read-only docker-socket-proxy (http://socket-proxy:2375).
/// </summary>
public sealed class DockerHttpClientFactory : IDisposable
{
    private readonly Dictionary<Uri, HttpClient> _clients = [];
    private readonly Lock _lock = new();

    public HttpClient Get(DockerEndpoint endpoint)
    {
        lock (_lock)
        {
            if (!_clients.TryGetValue(endpoint.Url, out var client))
            {
                client = Create(endpoint.Url);
                _clients[endpoint.Url] = client;
            }

            return client;
        }
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
    }

    private static HttpClient Create(Uri url)
    {
        if (url.Scheme == "unix")
        {
            var socketPath = url.AbsolutePath;
            var handler = new SocketsHttpHandler
            {
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

            // The host name is ignored on a Unix socket, but HTTP needs one.
            return new HttpClient(handler) { BaseAddress = new Uri("http://docker/"), Timeout = Timeout.InfiniteTimeSpan };
        }

        var builder = new UriBuilder(url) { Scheme = url.Scheme == "tcp" ? "http" : url.Scheme, Path = "/" };
        if (url.Scheme == "tcp" && url.IsDefaultPort)
        {
            builder.Port = 2375;
        }

        return new HttpClient { BaseAddress = builder.Uri, Timeout = Timeout.InfiniteTimeSpan };
    }
}
