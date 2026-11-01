using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Portarr.Core.Configuration;

namespace Portarr.Core.Datastore.Migration;

public interface IMigrationController
{
    /// <summary>Applies pending migrations; safe to call on every start.</summary>
    void Migrate();
}

public sealed partial class MigrationController : IMigrationController
{
    private readonly IDatabase _database;
    private readonly ILogger<MigrationController> _logger;

    public MigrationController(IDatabase database, ILogger<MigrationController> logger)
    {
        _database = database;
        _logger = logger;
    }

    public void Migrate()
    {
        // The runner gets its own container: it only lives for the duration of the migration.
        using var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(runner =>
            {
                if (_database.Type == DatabaseType.Sqlite)
                {
                    runner.AddSQLite();
                }
                else
                {
                    runner.AddPostgres();
                }

                runner.WithGlobalConnectionString(_database.ConnectionString)
                    .ScanIn(typeof(MigrationController).Assembly).For.Migrations();
            })
            .BuildServiceProvider(validateScopes: false);

        LogMigrating(_database.Type);
        services.GetRequiredService<IMigrationRunner>().MigrateUp();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrating {DatabaseType} database")]
    private partial void LogMigrating(DatabaseType databaseType);
}
