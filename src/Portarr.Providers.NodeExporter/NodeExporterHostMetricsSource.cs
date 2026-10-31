using System.Net.Http.Headers;
using Portarr.Core.Infrastructure;

namespace Portarr.Providers.NodeExporter;

/// <summary>
/// node_exporter read over HTTP (D-72): one GET of its <c>/metrics</c> URL per machine and second, gzip
/// accepted, short timeout, size capped, streamed and filtered to the few families the dashboard needs.
/// No credentials: node_exporter is restricted by network (firewall to Portarr's host), never by a secret in the catalog.
/// </summary>
public sealed class NodeExporterHostMetricsSource : IHostMetricsSource
{
    public const string HttpClientName = "node-exporter";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NodeExporterSampler _sampler = new();

    public NodeExporterHostMetricsSource(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public bool Supports(HostMetricsTarget target)
    {
        return target.Url is not null;
    }

    public async Task<HostReading> ReadAsync(HostMetricsTarget target, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await _sampler.ReadAsync(target.Key, stream, cancellationToken);
    }
}
