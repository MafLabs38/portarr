using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portarr.Core.Datastore;
using Portarr.Core.Datastore.Migration;
using Portarr.Core.Mcp;

namespace Portarr.Integration.Test.Datastore;

/// <summary>The figures that survive a restart (D-114): the real SQL and the migration, on SQLite and on PostgreSQL.</summary>
[Trait("Category", "Integration")]
public sealed class McpHealthStoreFixture : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 30, 15, TimeSpan.Zero);

    private readonly TestDatabase _testDatabase;
    private McpHealthStore _store = null!;

    public McpHealthStoreFixture(TestDatabase testDatabase)
    {
        _testDatabase = testDatabase;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var database = new Database(Options.Create(await _testDatabase.CreateOptionsAsync()));
        new MigrationController(database, NullLogger<MigrationController>.Instance).Migrate();
        _store = new McpHealthStore(database);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task should_read_back_what_was_saved_with_its_times_and_its_empty_columns()
    {
        var down = new McpServerHealth("memory", McpHealthState.Down, Now.AddDays(-12), Now.AddDays(-12), Now, "timeout", null);
        var neverHealthy = new McpServerHealth("search", McpHealthState.Down, Now, null, Now, "http-502", 502);

        await _store.SaveAsync(down, Token);
        await _store.SaveAsync(neverHealthy, Token);

        var all = await _store.ReadAllAsync(Token);
        all.Single(h => h.ServerId == "memory").Should().BeEquivalentTo(down);
        all.Single(h => h.ServerId == "search").Should().BeEquivalentTo(neverHealthy);
    }

    [Fact]
    public async Task should_replace_the_row_of_a_server_when_it_is_saved_again()
    {
        await _store.SaveAsync(new McpServerHealth("replace", McpHealthState.Down, Now, null, Now, "timeout", null), Token);
        var healthy = new McpServerHealth("replace", McpHealthState.Healthy, Now.AddMinutes(5), Now.AddMinutes(5), Now.AddMinutes(5), null, 200);

        await _store.SaveAsync(healthy, Token);

        (await _store.ReadAllAsync(Token)).Where(h => h.ServerId == "replace").Should().ContainSingle().Which.Should().BeEquivalentTo(healthy);
    }

    [Fact]
    public async Task should_forget_the_servers_that_are_no_longer_checked_and_keep_the_others()
    {
        await _store.SaveAsync(new McpServerHealth("keep", McpHealthState.Healthy, Now, Now, Now, null, 200), Token);
        await _store.SaveAsync(new McpServerHealth("gone", McpHealthState.Healthy, Now, Now, Now, null, 200), Token);

        await _store.RetainAsync(["keep"], Token);

        var ids = (await _store.ReadAllAsync(Token)).Select(h => h.ServerId).ToList();
        ids.Should().Contain("keep").And.NotContain("gone");
    }
}
