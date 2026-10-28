using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Portarr.Core.Datastore;

/// <summary>Readiness: Portarr only serves traffic when its database answers.</summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDatabase _database;

    public DatabaseHealthCheck(IDatabase database)
    {
        _database = database;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await _database.GetEngineVersionAsync(cancellationToken);
            return HealthCheckResult.Healthy(version);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database unreachable", exception);
        }
    }
}
