using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Portarr.Core.Infrastructure;

namespace Portarr.Providers.NodeExporter;

/// <summary>
/// node_exporter read over HTTP (D-72): one GET of its <c>/metrics</c> URL per machine and second, gzip
/// accepted, short timeout, size capped, streamed and filtered to the few families the dashboard needs.
/// node_exporter itself is restricted by network (firewall to Portarr's host); the Portarr reader in front of it (D-100) also asks for a
/// bearer token, read from the environment variable the catalog names, never from the catalog.
/// </summary>
public sealed class NodeExporterHostMetricsSource : IHostMetricsSource
{
    public const string HttpClientName = "node-exporter";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NodeExporterSampler _sampler = new();
    private readonly IConfiguration? _configuration;

    public NodeExporterHostMetricsSource(IHttpClientFactory httpClientFactory, IConfiguration? configuration = null)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public bool Supports(HostMetricsTarget target)
    {
        return target.Url is not null;
    }

    public const string TokenMissing = "token-missing";

    /// <summary><c>token-missing</c> when the environment's own endpoint declares a token whose variable is empty (the reader's, D-100).</summary>
    public string? GetConfigurationError(HostMetricsTarget target)
    {
        return DeclaredVariable(target) is { } variable && string.IsNullOrEmpty(_configuration?[variable]) ? TokenMissing : null;
    }

    public async Task<HostReading> ReadAsync(HostMetricsTarget target, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        if (BearerToken(target) is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await _sampler.ReadAsync(target.Key, stream, cancellationToken);
    }

    /// <summary>
    /// The token of the environment's own endpoint (the Portarr reader asks for one, D-100); a node's exporter reached through
    /// the cluster has none. A declared token whose variable is empty is an authorization failure, not a silent anonymous read.
    /// </summary>
    private string? BearerToken(HostMetricsTarget target)
    {
        if (DeclaredVariable(target) is not { } variable)
        {
            return null;
        }

        return _configuration?[variable] is { Length: > 0 } token
            ? token
            : throw new HttpRequestException($"The token variable {variable} is not set.", null, HttpStatusCode.Unauthorized);
    }

    /// <summary>The token variable the target's own endpoint declares; none for a node's exporter reached another way.</summary>
    private static string? DeclaredVariable(HostMetricsTarget target)
    {
        return target.Environment.HostMetrics is { TokenVariable: { } variable } endpoint && target.Url == endpoint.NodeExporter ? variable : null;
    }
}
