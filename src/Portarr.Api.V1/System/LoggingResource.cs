using FluentValidation;
using Microsoft.Extensions.Logging;
using Portarr.Core.Instrumentation;

namespace Portarr.Api.V1.System;

/// <summary>Portarr's log level, by name: Trace, Debug, Information, Warning or Error.</summary>
public sealed record LoggingResource(string Level);

public sealed class LoggingResourceValidator : AbstractValidator<LoggingResource>
{
    public LoggingResourceValidator()
    {
        RuleFor(resource => resource.Level)
            .Must(level => Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed) && LogLevelService.Selectable.Contains(parsed))
            .WithMessage("Level must be one of: Trace, Debug, Information, Warning, Error.");
    }
}
