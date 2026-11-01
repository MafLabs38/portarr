using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Cloudflare;

/// <summary>
/// Lists the Pages projects and Workers of a Cloudflare account (D-53). GET requests only, with a token
/// read from the environment variable the catalog names: <i>Account · Cloudflare Pages · Read</i> and
/// <i>Account · Workers Scripts · Read</i> are enough. Usage and logs are not served.
/// </summary>
public sealed partial class CloudflareWorkloadSource : IWorkloadSource
{
    public const string HttpClientName = "cloudflare";

    public static readonly Uri ApiBaseAddress = new("https://api.cloudflare.com/client/v4/");

    /// <summary>Reason codes of a declared but unusable environment, shown as not connected.</summary>
    public const string TokenMissing = ReasonCodes.TokenMissing;
    public const string AccountInvalid = "account-invalid";

    /// <summary>Pages projects are paginated; a homelab account never has this many pages.</summary>
    private const int MaxPages = 20;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public CloudflareWorkloadSource(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public bool SupportsUsage => false;

    public bool SupportsLogs => false;

    public bool Supports(CatalogEnvironment environment)
    {
        return environment.Cloudflare() is not null;
    }

    public string? GetConfigurationError(CatalogEnvironment environment)
    {
        var account = environment.Cloudflare();
        if (account is null)
        {
            return null;
        }

        // The account id goes into URL paths: only the 32 hexadecimal characters Cloudflare uses.
        if (!AccountIdPattern().IsMatch(account.AccountId))
        {
            return AccountInvalid;
        }

        return string.IsNullOrWhiteSpace(Token(account)) ? TokenMissing : null;
    }

    public async Task<IReadOnlyList<Workload>> ListAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        var account = environment.Cloudflare() ?? throw new InvalidOperationException($"Environment {environment.Id} has no Cloudflare account.");
        if (GetConfigurationError(environment) is { } error)
        {
            throw new InvalidOperationException($"Environment {environment.Id} is not configured: {error}.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        var projects = await ListPagesProjectsAsync(account, timeout.Token);
        var scripts = await GetAsync($"accounts/{account.AccountId}/workers/scripts", account, CloudflareJsonContext.Default.CloudflareEnvelopeListWorkerScript, timeout.Token);

        return projects.Select(project => CloudflareMapper.ToWorkload(environment.Id, project))
            .Concat((scripts.Result ?? []).Select(script => CloudflareMapper.ToWorkload(environment.Id, script)))
            .ToList();
    }

    public Task<WorkloadUsage?> GetUsageAsync(CatalogEnvironment environment, string workloadName, CancellationToken cancellationToken)
    {
        return Task.FromResult<WorkloadUsage?>(null);
    }

    public Task<ILogStream?> OpenLogsAsync(CatalogEnvironment environment, string workloadName, int tail, bool follow, CancellationToken cancellationToken)
    {
        return Task.FromResult<ILogStream?>(null);
    }

    private async Task<List<PagesProject>> ListPagesProjectsAsync(CloudflareAccount account, CancellationToken cancellationToken)
    {
        var projects = new List<PagesProject>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var response = await GetAsync($"accounts/{account.AccountId}/pages/projects?page={page}", account, CloudflareJsonContext.Default.CloudflareEnvelopeListPagesProject, cancellationToken);
            projects.AddRange(response.Result ?? []);

            if (response.ResultInfo?.TotalPages is not { } total || page >= total)
            {
                break;
            }
        }

        return projects;
    }

    private async Task<CloudflareEnvelope<T>> GetAsync<T>(string path, CloudflareAccount account, JsonTypeInfo<CloudflareEnvelope<T>> type, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(account));

        using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync(type, cancellationToken);
        return envelope is { Success: true } ? envelope : throw new InvalidDataException("Cloudflare answered without success.");
    }

    private string? Token(CloudflareAccount account) => _configuration[account.TokenVariable];

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex AccountIdPattern();
}
