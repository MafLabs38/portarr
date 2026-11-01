using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;
using Portarr.Core.Backups;
using Portarr.Core.Catalog;

namespace Portarr.Providers.Backrest;

/// <summary>
/// Reads Backrest (D-80), the restic front-end that runs on a host: its plans and their last backups. Backrest
/// speaks the Connect protocol, whose calls are POSTs of JSON; Portarr calls two methods only, <c>GetConfig</c>
/// and <c>GetOperations</c>, and none of those that start, forget, restore or change anything. Targets Backrest
/// v1.14.x. A plan writes to one repository, so a copy in two repositories is two plans.
/// <para>
/// Backrest knows a single kind of account (basic authentication, full access): a read-only role does not exist.
/// Its configuration answer carries repository passwords; Portarr deserializes plan ids, repository ids and
/// schedules only, and keeps nothing else.
/// </para>
/// </summary>
public sealed class BackrestBackupSource : IBackupSource
{
    public const string HttpClientName = "backrest";

    public const string CredentialsMissing = "credentials-missing";

    /// <summary>Operations read per plan: backups are a fraction of them (index, forget, hooks, prune come with each run).</summary>
    private const int OperationsPerPlan = 200;

    private const int Parallelism = 4;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public BackrestBackupSource(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public string Kind => BackupSourceKinds.Backrest;

    public bool Supports(CatalogEnvironment environment)
    {
        return environment.Backrest() is not null;
    }

    public async Task<IReadOnlyList<BackupJob>> ListJobsAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        var endpoint = environment.Backrest() ?? throw new InvalidOperationException($"Environment {environment.Id} has no Backrest.");
        var authorization = Authorization(endpoint);
        using var client = _httpClientFactory.CreateClient(HttpClientName);

        var config = await PostAsync(client, endpoint, authorization, "GetConfig", "{}", BackrestJsonContext.Default.BackrestConfig, cancellationToken);
        var plans = config.Plans ?? [];

        var jobs = new BackupJob[plans.Count];
        await Parallel.ForEachAsync(
            plans.Select((plan, index) => (plan, index)),
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
            async (item, token) =>
            {
                var request = new GetOperationsRequest(new OperationSelector(item.plan.Id), OperationsPerPlan);
                var json = JsonSerializer.Serialize(request, BackrestJsonContext.Default.GetOperationsRequest);
                var operations = await PostAsync(client, endpoint, authorization, "GetOperations", json, BackrestJsonContext.Default.OperationList, token);
                jobs[item.index] = ToJob(item.plan, operations.Operations ?? []);
            });

        return jobs;
    }

    internal static BackupJob ToJob(BackrestPlan plan, IReadOnlyList<BackrestOperation> operations)
    {
        // Dry runs make no snapshot; a backup still running says nothing yet; a cancelled one was not an attempt.
        var finished = operations
            .Where(o => o.OperationBackup is { DryRun: not true } && o.Status is OperationStatus.Success or OperationStatus.Warning or OperationStatus.Error)
            .OrderByDescending(o => o.UnixTimeStartMs)
            .ToList();

        var last = finished.FirstOrDefault();
        var lastSuccess = finished.FirstOrDefault(o => o.Status is OperationStatus.Success or OperationStatus.Warning);
        BackupRun? run = null;
        if (last is not null)
        {
            run = new BackupRun(
                TimeOf(last),
                last.Status == OperationStatus.Error ? BackupResult.Failed : BackupResult.Success,
                lastSuccess is null ? null : TimeOf(lastSuccess),
                lastSuccess?.OperationBackup?.LastStatus?.Summary?.TotalBytesProcessed,
                SnapshotOf(lastSuccess));
        }

        return new BackupJob(plan.Id, plan.Id, Describe(plan.Schedule), string.IsNullOrEmpty(plan.Repo) ? [] : [plan.Repo], run);
    }

    private static string? SnapshotOf(BackrestOperation? operation)
    {
        return operation?.SnapshotId ?? operation?.OperationBackup?.LastStatus?.Summary?.SnapshotId;
    }

    /// <summary>"0 3 * * *", "every 2 d", "every 6 h" or "disabled": the schedule as the plan states it.</summary>
    internal static string? Describe(BackrestSchedule? schedule)
    {
        return schedule switch
        {
            null => null,
            { Disabled: true } => "disabled",
            { Cron: { Length: > 0 } cron } => cron,
            { MaxFrequencyDays: > 0 } => $"every {schedule.MaxFrequencyDays} d",
            { MaxFrequencyHours: > 0 } => $"every {schedule.MaxFrequencyHours} h",
            _ => null,
        };
    }

    private static DateTimeOffset TimeOf(BackrestOperation operation)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(operation.UnixTimeEndMs > 0 ? operation.UnixTimeEndMs : operation.UnixTimeStartMs);
    }

    private AuthenticationHeaderValue? Authorization(BackrestEndpoint endpoint)
    {
        if (endpoint.UserVariable is null || endpoint.PasswordVariable is null)
        {
            return null;
        }

        var user = _configuration[endpoint.UserVariable];
        var password = _configuration[endpoint.PasswordVariable];
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
        {
            throw new BackupSourceUnavailableException(CredentialsMissing, notSetUp: true);
        }

        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
    }

    private static async Task<T> PostAsync<T>(HttpClient client, BackrestEndpoint endpoint, AuthenticationHeaderValue? authorization, string method, string json, JsonTypeInfo<T> type, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint.Url, $"v1.Backrest/{method}"))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = authorization;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Connect-Protocol-Version", "1");

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"POST {method} answered {(int)response.StatusCode}.", null, response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync(type, cancellationToken)
            ?? throw new InvalidDataException($"Empty answer from {method}.");
    }
}
