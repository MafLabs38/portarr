using Portarr.Core.Workloads;

namespace Portarr.Providers.Cloudflare.Test;

public sealed class CloudflareMapperFixture
{
    private static readonly DateTimeOffset Deployed = new(2026, 10, 20, 14, 3, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("success", WorkloadState.Running, "Deployed 2026-10-20 14:03 UTC · a1b2c3d")]
    [InlineData("failure", WorkloadState.Exited, "Deployment failed 2026-10-20 14:03 UTC · a1b2c3d")]
    [InlineData("canceled", WorkloadState.Exited, "Deployment canceled 2026-10-20 14:03 UTC · a1b2c3d")]
    [InlineData("active", WorkloadState.Created, "Deploying (build) · a1b2c3d")]
    public void should_map_the_latest_production_deployment(string status, WorkloadState state, string text)
    {
        var workload = CloudflareMapper.ToWorkload("cdn", Project(latest: Deployment("production", status)));

        workload.State.Should().Be(state);
        workload.StatusText.Should().Be(text);
        workload.Name.Should().Be("site");
        workload.Image.Should().Be("Cloudflare Pages");
        workload.Labels.Should().Contain("cloudflare.commit", "a1b2c3d");
    }

    [Fact]
    public void should_use_the_live_deployment_when_the_latest_one_is_a_preview()
    {
        var project = Project(latest: Deployment("preview", "failure"), canonical: Deployment("production", "success"));

        CloudflareMapper.ToWorkload("cdn", project).State.Should().Be(WorkloadState.Running);
    }

    [Fact]
    public void should_be_unknown_without_any_production_deployment()
    {
        var workload = CloudflareMapper.ToWorkload("cdn", Project());

        workload.State.Should().Be(WorkloadState.Unknown);
        workload.StatusText.Should().Be("No production deployment");
    }

    [Fact]
    public void should_map_a_worker_as_running_with_its_last_deployment()
    {
        var workload = CloudflareMapper.ToWorkload("cdn", new WorkerScript("api", Deployed.AddDays(-30), Deployed));

        workload.State.Should().Be(WorkloadState.Running);
        workload.StatusText.Should().Be("Deployed 2026-10-20 14:03 UTC");
        workload.Image.Should().Be("Cloudflare Worker");
        workload.Id.Should().Be("workers/api");
    }

    private static PagesProject Project(PagesDeployment? latest = null, PagesDeployment? canonical = null)
    {
        return new PagesProject("p1", "site", Deployed.AddDays(-100), "main", canonical, latest);
    }

    private static PagesDeployment Deployment(string environment, string status)
    {
        return new PagesDeployment("d1", environment, Deployed, new PagesStage(status == "active" ? "build" : "deploy", status), new PagesTrigger(new PagesTriggerMetadata("main", "a1b2c3d4e5f6")));
    }
}
