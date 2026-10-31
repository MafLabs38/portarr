using System.Text.Json;
using AwesomeAssertions;
using Portarr.Core.Backups;
using Portarr.Core.Catalog;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>How a job of the backup tool behaves in a scenario.</summary>
public enum ToolJobBehavior
{
    /// <summary>Its last attempt succeeded, at <see cref="BackupSourceContract.SuccessAt"/>.</summary>
    Succeeded,

    /// <summary>It succeeded at <see cref="BackupSourceContract.SuccessAt"/>, then failed at <see cref="BackupSourceContract.FailureAt"/>.</summary>
    FailedAfterSuccess,

    /// <summary>The job exists and nothing ran yet.</summary>
    NeverRan,
}

public enum ToolFault
{
    None,

    /// <summary>The tool answers 403: the account lacks the permission.</summary>
    Refused,

    /// <summary>Nothing answers.</summary>
    Unreachable,
}

public sealed record ToolJob(string Reference, ToolJobBehavior Behavior);

/// <summary>What the backup tool holds in a scenario: its jobs, or the way reading it fails.</summary>
public sealed record ToolState(IReadOnlyList<ToolJob> Jobs, ToolFault Fault = ToolFault.None);

/// <summary>A request the connector sent to the tool.</summary>
public sealed record RecordedRequest(HttpMethod Method, string Path);

/// <summary>
/// A connector wired to a fake tool that holds a <see cref="ToolState"/>: the source under test, an environment it
/// supports, one it does not, what it called, and the secrets the tool's answers carry (which must never come out).
/// </summary>
public sealed record BackupToolHarness(
    IBackupSource Source,
    CatalogEnvironment Environment,
    CatalogEnvironment EnvironmentWithoutTool,
    Func<IReadOnlyList<RecordedRequest>> Requests,
    IReadOnlyList<string> Secrets);

/// <summary>
/// The contract of the backup capability (<see cref="IBackupSource"/>, D-62), run against every connector that
/// declares it: the same scenarios, whatever the tool (Backrest, K8up, Kubernetes CronJobs). A connector joins by
/// inheriting this class and arranging its fake tool for a <see cref="ToolState"/> (CLAUDE.md rule 8).
/// </summary>
public abstract class BackupSourceContract
{
    /// <summary>The newest success of a job that succeeded.</summary>
    public static readonly DateTimeOffset SuccessAt = new(2026, 1, 30, 3, 0, 0, TimeSpan.Zero);

    /// <summary>The failure that follows the success of a job that <see cref="ToolJobBehavior.FailedAfterSuccess"/>.</summary>
    public static readonly DateTimeOffset FailureAt = new(2026, 1, 31, 3, 0, 0, TimeSpan.Zero);

    /// <summary>The kind the connector reports (<see cref="BackupSourceKinds"/>).</summary>
    protected abstract string ExpectedKind { get; }

    /// <summary>The POST calls the connector may make: some tools only read through POST (Backrest's Connect protocol). Empty for a pure GET connector.</summary>
    protected virtual IReadOnlySet<string> ReadOnlyPostPaths => new HashSet<string>();

    /// <summary>Wires the connector to a fake tool in the given state.</summary>
    protected abstract BackupToolHarness Arrange(ToolState state);

    private static ToolState Holding(params ToolJob[] jobs) => new(jobs);

    [Fact]
    public void should_declare_one_of_the_kinds_the_catalog_names()
    {
        var harness = Arrange(Holding());

        harness.Source.Kind.Should().Be(ExpectedKind).And.BeOneOf(BackupSourceKinds.Backrest, BackupSourceKinds.K8up, BackupSourceKinds.CronJob);
    }

    [Fact]
    public void should_support_the_environments_it_can_read_and_no_other()
    {
        var harness = Arrange(Holding());

        harness.Source.Supports(harness.Environment).Should().BeTrue();
        harness.Source.Supports(harness.EnvironmentWithoutTool).Should().BeFalse();
    }

    [Fact]
    public async Task should_list_every_job_of_the_tool_under_the_reference_the_catalog_names()
    {
        var harness = Arrange(Holding(new ToolJob("alpha", ToolJobBehavior.Succeeded), new ToolJob("beta", ToolJobBehavior.NeverRan)));

        var jobs = await harness.Source.ListJobsAsync(harness.Environment, TestContext.Current.CancellationToken);

        jobs.Select(j => j.Reference).Should().BeEquivalentTo(Expected("alpha", "beta"));
        jobs.Should().AllSatisfy(j => j.Name.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public async Task should_list_no_job_for_a_tool_that_has_none()
    {
        var harness = Arrange(Holding());

        var jobs = await harness.Source.ListJobsAsync(harness.Environment, TestContext.Current.CancellationToken);

        jobs.Should().BeEmpty("a tool without jobs is an answer, not a failure");
    }

    [Fact]
    public async Task should_report_the_newest_success_of_a_job_that_succeeded()
    {
        var harness = Arrange(Holding(new ToolJob("alpha", ToolJobBehavior.Succeeded)));

        var run = (await harness.Source.ListJobsAsync(harness.Environment, TestContext.Current.CancellationToken)).Single().Run;

        run.Should().NotBeNull();
        run.LastResult.Should().Be(BackupResult.Success);
        run.LastSuccessAt.Should().BeCloseTo(SuccessAt, TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task should_report_a_failure_after_the_last_success_and_keep_the_success()
    {
        var harness = Arrange(Holding(new ToolJob("alpha", ToolJobBehavior.FailedAfterSuccess)));

        var run = (await harness.Source.ListJobsAsync(harness.Environment, TestContext.Current.CancellationToken)).Single().Run;

        run.Should().NotBeNull();
        run.LastResult.Should().Be(BackupResult.Failed);
        run.LastRunAt.Should().BeCloseTo(FailureAt, TimeSpan.FromMinutes(30));
        run.LastSuccessAt.Should().BeCloseTo(SuccessAt, TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task should_report_no_success_for_a_job_that_exists_and_never_ran()
    {
        var harness = Arrange(Holding(new ToolJob("alpha", ToolJobBehavior.NeverRan)));

        var job = (await harness.Source.ListJobsAsync(harness.Environment, TestContext.Current.CancellationToken)).Single();

        (job.Run?.LastSuccessAt).Should().BeNull();
        (job.Run?.LastResult).Should().NotBe(BackupResult.Success);
    }

    [Fact]
    public async Task should_fail_instead_of_answering_when_the_tool_refuses_the_account()
    {
        var harness = Arrange(new ToolState([new ToolJob("alpha", ToolJobBehavior.Succeeded)], ToolFault.Refused));

        var read = () => harness.Source.ListJobsAsync(harness.Environment, TestContext.Current.CancellationToken);

        (await read.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_fail_instead_of_answering_when_the_tool_cannot_be_reached()
    {
        var harness = Arrange(new ToolState([new ToolJob("alpha", ToolJobBehavior.Succeeded)], ToolFault.Unreachable));

        var read = () => harness.Source.ListJobsAsync(harness.Environment, TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<Exception>("an empty list would read as 'no jobs' and hide that the tool is down");
    }

    [Fact]
    public async Task should_only_read_the_tool()
    {
        var harness = Arrange(Holding(new ToolJob("alpha", ToolJobBehavior.Succeeded), new ToolJob("beta", ToolJobBehavior.FailedAfterSuccess)));

        await harness.Source.ListJobsAsync(harness.Environment, TestContext.Current.CancellationToken);

        var requests = harness.Requests();
        requests.Should().NotBeEmpty();
        requests.Where(r => r.Method != HttpMethod.Get).Should().AllSatisfy(r =>
        {
            r.Method.Should().Be(HttpMethod.Post, "no PUT, PATCH or DELETE: Portarr never changes a backup tool (D-43)");
            ReadOnlyPostPaths.Should().Contain(r.Path, "a POST is allowed only for the read methods of tools that read through POST");
        });
    }

    [Fact]
    public async Task should_never_return_the_secrets_a_tool_answers_with()
    {
        var harness = Arrange(Holding(new ToolJob("alpha", ToolJobBehavior.Succeeded)));

        var jobs = await harness.Source.ListJobsAsync(harness.Environment, TestContext.Current.CancellationToken);

        var text = JsonSerializer.Serialize(jobs);
        foreach (var secret in harness.Secrets)
        {
            text.Should().NotContain(secret);
        }
    }

    /// <summary>The references a connector gives its jobs: the plain name, or <c>namespace/name</c> for Kubernetes tools.</summary>
    protected virtual string[] Expected(params string[] names) => names;
}
