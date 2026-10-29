using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Core.Status;
using Portarr.Core.Workloads;
using Portarr.Http;

namespace Portarr.Api.V1.Workloads;

[ApiController]
[Route("api/v1")]
public sealed class WorkloadsController : ControllerBase
{
    /// <summary>Largest log tail a client can ask for; bigger values are clamped.</summary>
    public const int MaxLogTail = 5000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ICatalogService _catalogService;
    private readonly IWorkloadInventory _inventory;
    private readonly IFleetService _fleetService;
    private readonly IEnumerable<IWorkloadSource> _sources;
    private readonly IOptions<PortarrOptions> _options;

    public WorkloadsController(
        ICatalogService catalogService,
        IWorkloadInventory inventory,
        IFleetService fleetService,
        IEnumerable<IWorkloadSource> sources,
        IOptions<PortarrOptions> options)
    {
        _catalogService = catalogService;
        _inventory = inventory;
        _fleetService = fleetService;
        _sources = sources;
        _options = options;
    }

    [HttpGet("environments")]
    public IReadOnlyList<EnvironmentResource> GetEnvironments()
    {
        var inventory = _inventory.Current;

        return _catalogService.Current.Catalog.Environments.Select(environment =>
        {
            var current = inventory.GetValueOrDefault(environment.Id);
            var connection = (environment.Docker, current) switch
            {
                (null, _) => "not-connected",
                (_, null) => "pending",
                (_, { Reachable: true }) => "connected",
                _ => "unreachable",
            };

            return new EnvironmentResource(
                environment.Id,
                environment.Name,
                environment.Docker is null ? "none" : "docker",
                connection,
                current?.Error,
                current?.Workloads.Count ?? 0,
                current?.RefreshedAt);
        }).ToList();
    }

    [HttpGet("workloads")]
    public IReadOnlyList<WorkloadResource> GetWorkloads()
    {
        return _fleetService.GetWorkloads().Select(w => w.ToResource()).ToList();
    }

    [HttpGet("workloads/{environmentId}/{name}")]
    public ActionResult<WorkloadResource> GetWorkload(string environmentId, string name)
    {
        var workload = Find(environmentId, name);
        return workload is null ? NotFound() : workload.ToResource();
    }

    /// <summary>Live CPU (cores) and memory, read from the connector on each call (never stored, D-03).</summary>
    [HttpGet("workloads/{environmentId}/{name}/usage")]
    [EnableRateLimiting(HttpServiceCollectionExtensions.UsagePolicyName)]
    public async Task<ActionResult<WorkloadUsageResource>> GetUsageAsync(string environmentId, string name, CancellationToken cancellationToken)
    {
        var (environment, source) = Resolve(environmentId);
        if (environment is null || source is null || !CanReadDetails(environmentId, name))
        {
            return NotFound();
        }

        WorkloadUsage? usage;
        try
        {
            usage = await source.GetUsageAsync(environment, name, cancellationToken);
        }
        catch (Exception exception) when (IsConnectorFailure(exception, cancellationToken))
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        return usage is null ? NotFound() : new WorkloadUsageResource(usage.CpuCores, usage.MemoryBytes, usage.MemoryLimitBytes);
    }

    /// <summary>
    /// Server-sent events: the last <paramref name="tail"/> lines (0 to <see cref="MaxLogTail"/>), then new lines live; an
    /// <c>end</c> event when the workload stops writing (e.g. it exited), so clients do not reconnect
    /// and replay the tail. The stream is opened before any byte is sent: failures are plain
    /// 404/502 responses.
    /// </summary>
    [HttpGet("workloads/{environmentId}/{name}/logs")]
    [EnableRateLimiting(HttpServiceCollectionExtensions.LogStreamsPolicyName)]
    public async Task StreamLogsAsync(string environmentId, string name, [FromQuery] int tail = 200, CancellationToken cancellationToken = default)
    {
        var (environment, source) = Resolve(environmentId);
        if (environment is null || source is null || !CanReadDetails(environmentId, name))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ILogStream? logs;
        try
        {
            logs = await source.OpenLogsAsync(environment, name, Math.Clamp(tail, 0, MaxLogTail), follow: true, cancellationToken);
        }
        catch (Exception exception) when (IsConnectorFailure(exception, cancellationToken))
        {
            Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        if (logs is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await using (logs)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            try
            {
                await foreach (var line in logs.ReadAsync(cancellationToken))
                {
                    await Response.WriteAsync($"data: {JsonSerializer.Serialize(line.ToResource(), Json)}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }

                await Response.WriteAsync("event: end\ndata: {}\n\n", cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The browser closed the stream.
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                await TryWriteErrorEventAsync();
            }
        }
    }

    /// <summary>The connector failed or timed out, as opposed to the caller giving up.</summary>
    private static bool IsConnectorFailure(Exception exception, CancellationToken cancellationToken)
    {
        return exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested;
    }

    private async Task TryWriteErrorEventAsync()
    {
        try
        {
            await Response.WriteAsync("event: error\ndata: {}\n\n", CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The client is gone too: nobody to tell.
        }
    }

    private WorkloadView? Find(string environmentId, string name)
    {
        return _fleetService.GetWorkloads().FirstOrDefault(w => w.Workload.EnvironmentId == environmentId && w.Workload.Name == name);
    }

    /// <summary>
    /// Logs and usage reveal what runs inside a container: by default only for workloads that a
    /// catalog app declares (see <see cref="PortarrOptions.LogsOfAllWorkloads"/>).
    /// </summary>
    private bool CanReadDetails(string environmentId, string name)
    {
        var workload = Find(environmentId, name);
        return workload is not null && (workload.AppIds.Count > 0 || _options.Value.LogsOfAllWorkloads);
    }

    private (CatalogEnvironment? Environment, IWorkloadSource? Source) Resolve(string environmentId)
    {
        var environment = _catalogService.Current.Catalog.Environments.FirstOrDefault(e => e.Id == environmentId);
        return (environment, environment is null ? null : _sources.FirstOrDefault(s => s.Supports(environment)));
    }
}
