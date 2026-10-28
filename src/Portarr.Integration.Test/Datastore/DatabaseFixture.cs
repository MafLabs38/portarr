using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;
using Portarr.Core.Datastore;
using Portarr.Core.Datastore.Migration;

namespace Portarr.Integration.Test.Datastore;

[Trait("Category", "Integration")]
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly TestDatabase _testDatabase;
    private Database _database = null!;

    public DatabaseFixture(TestDatabase testDatabase)
    {
        _testDatabase = testDatabase;
    }

    public async ValueTask InitializeAsync()
    {
        _database = new Database(Options.Create(await _testDatabase.CreateOptionsAsync()));
        Migrate();
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    [Fact]
    public void should_apply_migrations_again_without_error()
    {
        var migrate = Migrate;

        migrate.Should().NotThrow();
    }

    [Fact]
    public async Task should_report_the_engine_version()
    {
        var version = await _database.GetEngineVersionAsync(TestContext.Current.CancellationToken);

        version.Should().MatchRegex(_testDatabase.Type == DatabaseType.Postgres ? @"^PostgreSQL \d+" : @"^SQLite \d+\.\d+");
    }

    [Fact]
    public async Task should_return_null_when_a_config_key_is_missing()
    {
        var repository = new ConfigRepository(_database);

        var value = await repository.GetAsync(UniqueKey(), TestContext.Current.CancellationToken);

        value.Should().BeNull();
    }

    [Fact]
    public async Task should_overwrite_a_config_value_when_the_key_exists()
    {
        var repository = new ConfigRepository(_database);
        var key = UniqueKey();

        await repository.SetAsync(key, "first", TestContext.Current.CancellationToken);
        await repository.SetAsync(key, "second", TestContext.Current.CancellationToken);

        (await repository.GetAsync(key, TestContext.Current.CancellationToken)).Should().Be("second");
    }

    private void Migrate()
    {
        new MigrationController(_database, NullLogger<MigrationController>.Instance).Migrate();
    }

    private static string UniqueKey()
    {
        return $"test-{Guid.NewGuid():N}";
    }
}
