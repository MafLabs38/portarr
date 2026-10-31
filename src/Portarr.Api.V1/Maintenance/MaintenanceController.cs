using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;

namespace Portarr.Api.V1.Maintenance;

[ApiController]
[Route("api/v1/maintenance")]
[Produces("application/json")]
public sealed class MaintenanceController : ControllerBase
{
    private readonly IMaintenanceService _maintenanceService;
    private readonly IMaintenanceMonitor _monitor;
    private readonly ICatalogService _catalogService;
    private readonly TimeProvider _timeProvider;

    public MaintenanceController(IMaintenanceService maintenanceService, IMaintenanceMonitor monitor, ICatalogService catalogService, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _maintenanceService = maintenanceService;
        _monitor = monitor;
        _catalogService = catalogService;
    }

    /// <summary>What is vulnerable, outdated or late in the fleet: the operator's repositories and the running images against their registries.</summary>
    /// <remarks>D-64 to D-67. Built from memory (last scan, last lookups): this call reaches neither GitHub nor a registry.</remarks>
    /// <response code="200">The maintenance report, with when the data was last read.</response>
    [HttpGet]
    [ProducesResponseType<MaintenanceResource>(StatusCodes.Status200OK)]
    public MaintenanceResource Get()
    {
        return _maintenanceService.GetReport().ToResource(_catalogService.Current.Catalog);
    }

    /// <summary>Rescans the code sources and the registries now, without waiting for their cadence.</summary>
    /// <remarks>
    /// A read of external APIs, no change to the infrastructure (D-31). The scan runs in the background; poll
    /// <c>GET /api/v1/maintenance</c> until <c>refresh.refreshing</c> is false. GitHub's rate-limit guard still
    /// applies to every request. Accepted at most once every 60 seconds.
    /// </remarks>
    /// <response code="202">The refresh was accepted; the body tells when the next one is accepted.</response>
    /// <response code="429">A refresh was accepted less than 60 seconds ago; <c>Retry-After</c> tells how long to wait.</response>
    [HttpPost("refresh")]
    [ProducesResponseType<MaintenanceRefreshResource>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public IActionResult Refresh()
    {
        var request = _monitor.RequestRefresh();
        if (!request.Accepted)
        {
            var wait = (int)Math.Ceiling(Math.Max(1, (request.RetryAt - _timeProvider.GetUtcNow()).TotalSeconds));
            Response.Headers.RetryAfter = wait.ToString(CultureInfo.InvariantCulture);
            return Problem(
                title: "Refresh too frequent",
                detail: $"A refresh was accepted less than {(int)MaintenanceMonitor.RefreshCooldown.TotalSeconds} seconds ago.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        return Accepted(_monitor.Refresh.ToResource());
    }
}
