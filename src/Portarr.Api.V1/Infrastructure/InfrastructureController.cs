using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Status;
using Portarr.Http;

namespace Portarr.Api.V1.Infrastructure;

[ApiController]
[Route("api/v1/infrastructure")]
public sealed class InfrastructureController : ControllerBase
{
    /// <summary>A comment line sent when no read happened for this long, so proxies keep the stream open.</summary>
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ICatalogService _catalogService;
    private readonly IInfrastructureMonitor _monitor;
    private readonly IHostMetricsMonitor _hostMonitor;
    private readonly IFleetService _fleetService;

    public InfrastructureController(ICatalogService catalogService, IInfrastructureMonitor monitor, IHostMetricsMonitor hostMonitor, IFleetService fleetService)
    {
        _catalogService = catalogService;
        _monitor = monitor;
        _hostMonitor = hostMonitor;
        _fleetService = fleetService;
    }

    /// <summary>Every environment with its machines, their live window of points and their workloads sorted by consumption.</summary>
    /// <remarks>In catalog order (D-54). Values come from memory: this call never reaches a connector.</remarks>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InfrastructureEnvironmentResource>>(StatusCodes.Status200OK)]
    public IReadOnlyList<InfrastructureEnvironmentResource> Get()
    {
        return InfrastructureResourceBuilder.Build(_catalogService.Current.Catalog, _monitor.Current, _fleetService.GetWorkloads(), _hostMonitor.Current);
    }

    /// <summary>Live host metrics as server-sent events.</summary>
    /// <remarks>
    /// D-72: every read of a host agent as it happens, one <c>host</c> event per machine and
    /// second, without history (the snapshot of <c>GET /api/v1/infrastructure</c> is the initial state; the client
    /// appends each read as a point). A slow client loses reads, never delays the monitor. Counted with the log streams.
    /// </remarks>
    /// <response code="429">Too many open streams.</response>
    [HttpGet("stream")]
    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Blocker Bug", "S5034:ValueTask should be consumed once", Justification = "Each MoveNextAsync() ValueTask is converted once with AsTask() and only that Task is awaited.")]
    [EnableRateLimiting(HttpServiceCollectionExtensions.LogStreamsPolicyName)]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task StreamAsync(CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await Response.WriteAsync("retry: 2000\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            await using var reads = _hostMonitor.WatchAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            var next = reads.MoveNextAsync().AsTask();
            while (true)
            {
                var done = await Task.WhenAny(next, Task.Delay(KeepAlive, cancellationToken));
                if (done != next)
                {
                    await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    continue;
                }

                if (!await next)
                {
                    return;
                }

                await Response.WriteAsync($"event: host\ndata: {JsonSerializer.Serialize(reads.Current.ToResource(withHistory: false), Json)}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                next = reads.MoveNextAsync().AsTask();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser closed the stream.
        }
        catch (IOException)
        {
            // The connection dropped: EventSource reconnects by itself.
        }
    }
}
