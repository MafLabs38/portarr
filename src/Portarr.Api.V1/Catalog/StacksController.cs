using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Status;

namespace Portarr.Api.V1.Catalog;

[ApiController]
[Route("api/v1/stacks")]
public sealed class StacksController : ControllerBase
{
    private readonly IFleetService _fleetService;

    public StacksController(IFleetService fleetService)
    {
        _fleetService = fleetService;
    }

    /// <summary>The stacks of the catalog (groups of apps) with their status.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StackResource>>(StatusCodes.Status200OK)]
    public IReadOnlyList<StackResource> GetStacks()
    {
        return _fleetService.GetStacks().Select(stack => stack.ToResource()).ToList();
    }
}
