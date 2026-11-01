using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Kubernetes;

/// <summary>
/// Plain HTTPS calls to a Kubernetes API server (D-55): GET only, with a ServiceAccount bearer token read from
/// the environment variable the catalog names, and the cluster CA from the catalog folder. One HTTP client per
/// cluster. No Kubernetes SDK: four read-only endpoints do not justify its size and its own config model.
/// </summary>
public sealed class KubernetesApiClient : IDisposable
{
    /// <summary>Reason codes of a declared but unusable cluster, shown as not connected.</summary>
    public const string TokenMissing = ReasonCodes.TokenMissing;
    public const string CaMissing = "ca-missing";
    public const string CaInvalid = "ca-invalid";

    /// <summary>Largest response read (a pod list of a large homelab cluster is a few MB).</summary>
    private const int MaxResponseBytes = 32 * 1024 * 1024;

    private const int ListPageSize = 500;
    private const int MaxListPages = 20;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly IConfiguration _configuration;
    private readonly string _catalogFolder;
    private readonly Func<KubernetesCluster, X509Certificate2?, HttpMessageHandler> _handlerFactory;
    private readonly ConcurrentDictionary<(Uri Server, string? CaFile), HttpClient> _clients = new();

    public KubernetesApiClient(IConfiguration configuration, IOptions<PortarrOptions> options)
        : this(configuration, options, CreateHandler)
    {
    }

    internal KubernetesApiClient(IConfiguration configuration, IOptions<PortarrOptions> options, Func<KubernetesCluster, X509Certificate2?, HttpMessageHandler> handlerFactory)
    {
        _configuration = configuration;
        _catalogFolder = Path.GetDirectoryName(Path.GetFullPath(options.Value.EffectiveCatalogFile))!;
        _handlerFactory = handlerFactory;
    }

    /// <summary>A reason code when the cluster cannot be called yet: no token in the environment, no readable CA.</summary>
    public string? GetConfigurationError(KubernetesCluster cluster)
    {
        if (string.IsNullOrWhiteSpace(_configuration[cluster.TokenVariable]))
        {
            return TokenMissing;
        }

        if (CaPath(cluster) is not { } path)
        {
            return null;
        }

        if (!File.Exists(path))
        {
            return CaMissing;
        }

        try
        {
            using var certificate = LoadCa(path);
            return null;
        }
        catch (CryptographicException)
        {
            return CaInvalid;
        }
    }

    /// <summary>GET <paramref name="path"/> (relative to the server); null on 404.</summary>
    internal async Task<T?> GetAsync<T>(KubernetesCluster cluster, string path, JsonTypeInfo<T> type, CancellationToken cancellationToken)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration[cluster.TokenVariable]);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Client(cluster).SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(type, timeout.Token) ?? throw new InvalidDataException($"Empty answer from {path}.");
    }

    /// <summary>Every item of a list, across pages (<c>limit</c> and <c>continue</c>); null when the resource does not exist (404: CRD not installed).</summary>
    internal async Task<IReadOnlyList<T>?> ListAsync<T>(KubernetesCluster cluster, string path, JsonTypeInfo<ObjectList<T>> type, CancellationToken cancellationToken)
    {
        var items = new List<T>();
        string? next = null;
        for (var page = 0; page < MaxListPages; page++)
        {
            var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var url = $"{path}{separator}limit={ListPageSize}" + (next is null ? string.Empty : $"&continue={Uri.EscapeDataString(next)}");
            var list = await GetAsync(cluster, url, type, cancellationToken);
            if (list is null)
            {
                return page == 0 ? null : items;
            }

            items.AddRange(list.Items);
            next = list.Metadata?.Continue;
            if (string.IsNullOrEmpty(next))
            {
                return items;
            }
        }

        // The last page still points at more: a list cut short would read as "not found" and mislead, so it is refused.
        throw new InvalidDataException($"{path} has more than {MaxListPages * ListPageSize} items: Portarr refuses to read a truncated list.");
    }

    /// <summary>
    /// GET <paramref name="path"/> as a stream (a pod proxy's text answer); the caller disposes the response.
    /// Failures as <see cref="HttpRequestException"/> with the status.
    /// </summary>
    internal async Task<HttpResponseMessage> GetResponseAsync(KubernetesCluster cluster, string path, string accept, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration[cluster.TokenVariable]);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        var response = await Client(cluster).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException($"GET {path} answered {(int)response.StatusCode}.", null, response.StatusCode);
        }

        return response;
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _clients.Clear();
    }

    private HttpClient Client(KubernetesCluster cluster)
    {
        return _clients.GetOrAdd((cluster.Server, CaPath(cluster)), key =>
        {
            var ca = key.CaFile is null ? null : LoadCa(key.CaFile);
            var server = new UriBuilder(key.Server) { Path = "/" }.Uri;
            return new HttpClient(_handlerFactory(cluster, ca)) { BaseAddress = server, Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = MaxResponseBytes };
        });
    }

    /// <summary>The cluster CA is the only root trusted for this server; host name validation stays on.</summary>
    private static SocketsHttpHandler CreateHandler(KubernetesCluster cluster, X509Certificate2? ca)
    {
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
        if (ca is not null)
        {
            var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
            policy.CustomTrustStore.Add(ca);
            handler.SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = policy };
        }

        return handler;
    }

    private static X509Certificate2 LoadCa(string path) => X509Certificate2.CreateFromPem(File.ReadAllText(path));

    private string? CaPath(KubernetesCluster cluster)
    {
        return cluster.CaFile is null ? null : Path.GetFullPath(Path.Combine(_catalogFolder, cluster.CaFile));
    }
}
