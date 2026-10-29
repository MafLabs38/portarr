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

    [HttpGet]
    public IReadOnlyList<StackResource> GetStacks()
    {
        return _fleetService.GetStacks().Select(stack => stack.ToResource()).ToList();
    }
}
