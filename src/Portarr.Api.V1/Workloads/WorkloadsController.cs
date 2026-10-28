using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Catalog;
using Portarr.Core.Status;
using Portarr.Core.Workloads;

namespace Portarr.Api.V1.Workloads;

[ApiController]
[Route("api/v1")]
public sealed class WorkloadsController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ICatalogService _catalogService;
    private readonly IWorkloadInventory _inventory;
    private readonly IFleetService _fleetService;
    private readonly IEnumerable<IWorkloadSource> _sources;

    public WorkloadsController(
        ICatalogService catalogService,
        IWorkloadInventory inventory,
        IFleetService fleetService,
        IEnumerable<IWorkloadSource> sources)
    {
        _catalogService = catalogService;
        _inventory = inventory;
        _fleetService = fleetService;
        _sources = sources;
    }

    [HttpGet("environments")]
    public IReadOnlyList<EnvironmentResource> GetEnvironments()
    {
        var inventory = _inventory.Current;

        return _catalogService.Current.Catalog.Environments.Select(environment =>
        {
            var current = inventory.GetValueOrDefault(environment.Id);
            return new EnvironmentResource(
                environment.Id,
                environment.Name,
                environment.Docker is null ? "none" : "docker",
                current?.Reachable ?? false,
                current?.Error ?? (current is null ? "Not inventoried yet" : null),
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
    public async Task<ActionResult<WorkloadUsageResource>> GetUsageAsync(string environmentId, string name, CancellationToken cancellationToken)
    {
        var (environment, source) = Resolve(environmentId);
        if (environment is null || source is null || Find(environmentId, name) is null)
        {
            return NotFound();
        }

        var usage = await source.GetUsageAsync(environment, name, cancellationToken);
        return usage is null ? NotFound() : new WorkloadUsageResource(usage.CpuCores, usage.MemoryBytes, usage.MemoryLimitBytes);
    }

    /// <summary>
    /// Server-sent events: the last <paramref name="tail"/> lines, then new lines live.
    /// Each event's data is a <see cref="LogLineResource"/>.
    /// </summary>
    [HttpGet("workloads/{environmentId}/{name}/logs")]
    public async Task StreamLogsAsync(string environmentId, string name, [FromQuery] int tail = 200, CancellationToken cancellationToken = default)
    {
        var (environment, source) = Resolve(environmentId);
        if (environment is null || source is null || Find(environmentId, name) is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.Body.FlushAsync(cancellationToken);

        try
        {
            await foreach (var line in source.StreamLogsAsync(environment, name, tail, follow: true, cancellationToken))
            {
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(line.ToResource(), Json)}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser closed the stream.
        }
    }

    private WorkloadView? Find(string environmentId, string name)
    {
        return _fleetService.GetWorkloads().FirstOrDefault(w => w.Workload.EnvironmentId == environmentId && w.Workload.Name == name);
    }

    private (CatalogEnvironment? Environment, IWorkloadSource? Source) Resolve(string environmentId)
    {
        var environment = _catalogService.Current.Catalog.Environments.FirstOrDefault(e => e.Id == environmentId);
        return (environment, environment is null ? null : _sources.FirstOrDefault(s => s.Supports(environment)));
    }
}
