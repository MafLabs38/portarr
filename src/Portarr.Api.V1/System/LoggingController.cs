using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Portarr.Core.Accounts;
using Portarr.Core.Instrumentation;

namespace Portarr.Api.V1.System;

[ApiController]
[Route("api/v1/system/logging")]
public sealed class LoggingController : ControllerBase
{
    private readonly ILogLevelService _logLevelService;
    private readonly IValidator<LoggingResource> _validator;

    public LoggingController(ILogLevelService logLevelService, IValidator<LoggingResource> validator)
    {
        _logLevelService = logLevelService;
        _validator = validator;
    }

    /// <summary>The minimum level Portarr logs at.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.SystemView)]
    [ProducesResponseType<LoggingResource>(StatusCodes.Status200OK)]
    public LoggingResource Get()
    {
        return new LoggingResource(_logLevelService.Current.ToString());
    }

    /// <summary>Changes the log level, immediately and across restarts.</summary>
    /// <response code="400">The level is not one of the known levels.</response>
    [HttpPut]
    [Authorize(Policy = Permissions.SettingsManage)]
    [ProducesResponseType<LoggingResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LoggingResource>> UpdateAsync(LoggingResource resource, CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(resource, cancellationToken);

        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        await _logLevelService.SetAsync(Enum.Parse<LogLevel>(resource.Level, ignoreCase: true), cancellationToken);

        return Get();
    }
}
