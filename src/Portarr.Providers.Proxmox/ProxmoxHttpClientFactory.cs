using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Portarr.Providers.Proxmox;

/// <summary>
/// One HTTP client per Proxmox node. A node's certificate is self-signed by default: when the catalog gives its fingerprint, that
/// certificate and no other is accepted (a pin), otherwise the system's trust applies as for any https address.
/// </summary>
public sealed class ProxmoxHttpClientFactory : IDisposable
{
    /// <summary>Largest answer read: a node with hundreds of guests is well under it.</summary>
    internal const int MaxResponseBytes = 4 * 1024 * 1024;

    private readonly Dictionary<(Uri Url, string? Fingerprint), HttpClient> _clients = [];
    private readonly Lock _lock = new();
    private readonly Func<ProxmoxEndpoint, HttpClient> _create;

    /// <param name="create">How a node's client is made; only a test gives one (the default pins the certificate).</param>
    public ProxmoxHttpClientFactory(Func<ProxmoxEndpoint, HttpClient>? create = null)
    {
        _create = create ?? Create;
    }

    public HttpClient Get(ProxmoxEndpoint endpoint)
    {
        lock (_lock)
        {
            var key = (endpoint.Url, endpoint.Fingerprint);
            if (!_clients.TryGetValue(key, out var client))
            {
                client = _create(endpoint);
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

    /// <summary>Whether <paramref name="certificate"/> is the pinned one: its SHA-256 equals the fingerprint (colon-separated hexadecimal, any case).</summary>
    internal static bool Matches(X509Certificate? certificate, string fingerprint)
    {
        return certificate is not null && string.Equals(Convert.ToHexString(certificate.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256)), fingerprint.Replace(":", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient Create(ProxmoxEndpoint endpoint)
    {
        var handler = new SocketsHttpHandler { MaxConnectionsPerServer = 4, AllowAutoRedirect = false };
        if (endpoint.Fingerprint is { } pinned)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) => Matches(certificate, pinned);
        }

        return new HttpClient(handler) { BaseAddress = endpoint.Url, Timeout = TimeSpan.FromSeconds(15) };
    }
}
