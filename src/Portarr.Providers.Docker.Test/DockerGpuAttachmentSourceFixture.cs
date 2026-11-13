using System.Net;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker.Test;

/// <summary>Which containers have a card attached, from how <c>docker inspect</c> says they were created (D-120).</summary>
public sealed class DockerGpuAttachmentSourceFixture
{
    private static readonly Dictionary<string, string> Inspects = new()
    {
        ["jellyfin"] = """{ "Id": "jellyfin", "Config": { "Tty": false }, "HostConfig": { "Runtime": "nvidia", "DeviceRequests": null } }""",
        ["whisper"] = """{ "Id": "whisper", "Config": { "Tty": false }, "HostConfig": { "Runtime": "runc", "DeviceRequests": [{ "Driver": "", "Capabilities": [["gpu"]] }] } }""",
        ["legacy"] = """{ "Id": "legacy", "Config": { "Tty": false }, "HostConfig": { "Runtime": "runc", "DeviceRequests": [{ "Driver": "nvidia", "Capabilities": [] }] } }""",
        ["web"] = """{ "Id": "web", "Config": { "Tty": false }, "HostConfig": { "Runtime": "runc", "DeviceRequests": null } }""",
        ["old-engine"] = """{ "Id": "old-engine", "Config": { "Tty": false } }""",
    };

    private readonly List<string> _asked = [];
    private readonly HashSet<string> _failing = [];
    private readonly HashSet<string> _oversized = [];
    private readonly List<HttpMethod> _methods = [];

    private DockerGpuAttachmentSource Source => new(_ => new HttpClient(new Handler(this)) { BaseAddress = new Uri("http://docker/") });

    private static Workload Container(string id) => new("server-1", id, id, "example/x:1", WorkloadState.Running, WorkloadHealth.None, "Up", DateTimeOffset.UnixEpoch, null, new Dictionary<string, string>());

    private static IReadOnlyList<Workload> All => Inspects.Keys.Select(Container).ToList();

    private static Core.Catalog.CatalogEnvironment Environment => DockerEnvironments.With("server-1", "Server 1", new DockerEndpoint(new Uri("http://docker:2375")));

    [Fact]
    public async Task should_find_the_containers_created_with_the_nvidia_runtime_or_a_gpu_device_request()
    {
        var attached = await Source.AttachedAsync(Environment, All, TestContext.Current.CancellationToken);

        attached.Should().BeEquivalentTo(["jellyfin", "whisper", "legacy"]);
    }

    [Fact]
    public async Task should_inspect_a_container_once_for_as_long_as_it_exists_and_forget_one_that_is_gone()
    {
        var source = Source;
        await source.AttachedAsync(Environment, All, TestContext.Current.CancellationToken);
        _asked.Clear();

        await source.AttachedAsync(Environment, All, TestContext.Current.CancellationToken);
        _asked.Should().BeEmpty("how a container was created never changes");

        await source.AttachedAsync(Environment, [Container("web")], TestContext.Current.CancellationToken);
        await source.AttachedAsync(Environment, All, TestContext.Current.CancellationToken);
        _asked.Should().BeEquivalentTo(["containers/jellyfin/json", "containers/whisper/json", "containers/legacy/json", "containers/old-engine/json"], "the ones that went away were inspected again when they came back");
    }

    [Fact]
    public async Task should_skip_a_container_that_cannot_be_inspected_without_failing_the_others_and_read_it_again_next_pass()
    {
        var source = Source;
        _failing.Add("jellyfin");

        var first = await source.AttachedAsync(Environment, All, TestContext.Current.CancellationToken);

        first.Should().BeEquivalentTo(["whisper", "legacy"], "one failing inspection does not hide the others");
        _failing.Clear();
        (await source.AttachedAsync(Environment, All, TestContext.Current.CancellationToken)).Should().Contain("jellyfin");
    }

    [Fact]
    public async Task should_skip_a_container_whose_inspect_answer_is_larger_than_the_cap()
    {
        _oversized.Add("jellyfin");

        var attached = await Source.AttachedAsync(Environment, All, TestContext.Current.CancellationToken);

        attached.Should().BeEquivalentTo(["whisper", "legacy"]);
    }

    [Fact]
    public async Task should_inspect_at_most_a_few_containers_per_pass_with_a_get_only()
    {
        var many = Enumerable.Range(0, DockerGpuAttachmentSource.InspectionsPerPass + 5).Select(i => Container($"c{i:D2}")).ToList();

        await Source.AttachedAsync(Environment, many, TestContext.Current.CancellationToken);

        _asked.Should().HaveCount(DockerGpuAttachmentSource.InspectionsPerPass);
        _methods.Should().OnlyContain(method => method == HttpMethod.Get, "Portarr never writes to Docker");
    }

    [Fact]
    public async Task should_inspect_the_rest_on_the_following_passes_so_every_container_is_reached()
    {
        var many = Enumerable.Range(0, DockerGpuAttachmentSource.InspectionsPerPass + 5).Select(i => Container($"c{i:D2}")).ToList();
        var source = Source;

        await source.AttachedAsync(Environment, many, TestContext.Current.CancellationToken);
        await source.AttachedAsync(Environment, many, TestContext.Current.CancellationToken);

        _asked.Should().HaveCount(many.Count).And.OnlyHaveUniqueItems("the 25 containers are inspected once each over two passes");
    }

    [Fact]
    public void should_support_a_docker_environment_and_no_other()
    {
        Source.Supports(Environment).Should().BeTrue();
        Source.Supports(new Core.Catalog.CatalogEnvironment("cdn", "CDN")).Should().BeFalse();
    }

    private sealed class Handler(DockerGpuAttachmentSourceFixture owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            owner._methods.Add(request.Method);
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            owner._asked.Add(path);
            var id = path.Split('/')[1];
            if (owner._failing.Contains(id))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            var body = owner._oversized.Contains(id) ? new string(' ', 600 * 1024) + "{}" : Inspects.GetValueOrDefault(id, "{ \"Id\": \"x\", \"Config\": { \"Tty\": false } }");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
