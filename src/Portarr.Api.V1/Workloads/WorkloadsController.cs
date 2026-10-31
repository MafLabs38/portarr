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

    /// <summary>The environments of the catalog (Docker hosts, Kubernetes clusters) and whether Portarr reaches them.</summary>
    [HttpGet("environments")]
    [ProducesResponseType<IReadOnlyList<EnvironmentResource>>(StatusCodes.Status200OK)]
    public IReadOnlyList<EnvironmentResource> GetEnvironments()
    {
        var inventory = _inventory.Current;

        return _catalogService.Current.Catalog.Environments.Select(environment =>
        {
            var current = inventory.GetValueOrDefault(environment.Id);
            var supported = _sources.Any(s => s.Supports(environment));
            var connection = EnvironmentConnection.Of(supported, current);

            return new EnvironmentResource(
                environment.Id,
                environment.Name,
                environment.Runtime,
                connection,
                current?.Error,
                current?.Workloads.Count ?? 0,
                current?.RefreshedAt);
        }).ToList();
    }

    /// <summary>Every known workload (container or pod) with its state, health and the apps it belongs to.</summary>
    [HttpGet("workloads")]
    [ProducesResponseType<IReadOnlyList<WorkloadResource>>(StatusCodes.Status200OK)]
    public IReadOnlyList<WorkloadResource> GetWorkloads()
    {
        var sources = _catalogService.Current.Catalog.Environments.ToDictionary(e => e.Id, e => _sources.FirstOrDefault(s => s.Supports(e)));
        return _fleetService.GetWorkloads().Select(w => w.ToResource(sources.GetValueOrDefault(w.Workload.EnvironmentId))).ToList();
    }

    /// <summary>One workload by environment and name.</summary>
    /// <response code="404">No such workload.</response>
    [HttpGet("workloads/{environmentId}/{name}")]
    [ProducesResponseType<WorkloadResource>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<WorkloadResource> GetWorkload(string environmentId, string name)
    {
        var workload = Find(environmentId, name);
        return workload is null ? NotFound() : workload.ToResource(Resolve(environmentId).Source);
    }

    /// <summary>Live CPU (cores) and memory of a workload.</summary>
    /// <remarks>Read from the connector on each call, never stored (D-03). Only for workloads a catalog app declares, unless <c>LogsOfAllWorkloads</c> is set.</remarks>
    /// <response code="404">No such workload, or its connector or catalog does not allow reading its usage.</response>
    /// <response code="429">Too many calls from this client (30 per 10 seconds).</response>
    /// <response code="502">The connector failed or timed out.</response>
    [HttpGet("workloads/{environmentId}/{name}/usage")]
    [EnableRateLimiting(HttpServiceCollectionExtensions.UsagePolicyName)]
    [ProducesResponseType<WorkloadUsageResource>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<WorkloadUsageResource>> GetUsageAsync(string environmentId, string name, CancellationToken cancellationToken)
    {
        var (environment, source) = Resolve(environmentId);
        var workload = Find(environmentId, name);
        if (environment is null || source is null || workload is null || !source.SupportsUsage || !CanReadDetails(workload))
        {
            return NotFound();
        }

        WorkloadUsage? usage;
        try
        {
            usage = await source.GetUsageAsync(environment, workload.Workload, cancellationToken);
        }
        catch (Exception exception) when (IsConnectorFailure(exception, cancellationToken))
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        return usage is null ? NotFound() : new WorkloadUsageResource(usage.CpuCores, usage.MemoryBytes, usage.MemoryLimitBytes);
    }

    /// <summary>Live logs of a workload as server-sent events.</summary>
    /// <remarks>
    /// The last <paramref name="tail"/> lines (0 to <see cref="MaxLogTail"/>), then new lines live; an
    /// <c>end</c> event when the workload stops writing (e.g. it exited), so clients do not reconnect
    /// and replay the tail. The stream is opened before any byte is sent: failures are plain
    /// 404/502 responses. At most 4 open streams per client and 16 in total.
    /// </remarks>
    /// <response code="404">No such workload, or its logs cannot be read.</response>
    /// <response code="429">Too many open streams.</response>
    /// <response code="502">The connector failed or timed out.</response>
    [HttpGet("workloads/{environmentId}/{name}/logs")]
    [EnableRateLimiting(HttpServiceCollectionExtensions.LogStreamsPolicyName)]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task StreamLogsAsync(string environmentId, string name, [FromQuery] int tail = 200, CancellationToken cancellationToken = default)
    {
        var (environment, source) = Resolve(environmentId);
        var workload = Find(environmentId, name);
        if (environment is null || source is null || workload is null || !source.SupportsLogs || !CanReadDetails(workload))
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
    private bool CanReadDetails(WorkloadView workload)
    {
        return workload.AppIds.Count > 0 || _options.Value.LogsOfAllWorkloads;
    }

    private (CatalogEnvironment? Environment, IWorkloadSource? Source) Resolve(string environmentId)
    {
        var environment = _catalogService.Current.Catalog.Environments.FirstOrDefault(e => e.Id == environmentId);
        return (environment, environment is null ? null : _sources.FirstOrDefault(s => s.Supports(environment)));
    }
}
