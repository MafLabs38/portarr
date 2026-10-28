using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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

    [HttpGet]
    public LoggingResource Get()
    {
        return new LoggingResource(_logLevelService.Current.ToString());
    }

    [HttpPut]
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
