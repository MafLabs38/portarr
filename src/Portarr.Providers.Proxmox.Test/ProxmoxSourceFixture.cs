using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Proxmox.Test;

public sealed class ProxmoxSourceFixture
{
    private static readonly ProxmoxEndpoint Endpoint = new(new Uri("https://10.0.0.30:8006/api2/json/"), "hv", "portarr@pve!readonly", "PORTARR_PROXMOX_TOKEN", null);

    private static readonly CatalogEnvironment Environment = new("hv", "Hypervisor", null, Connectors: new Dictionary<string, object> { ["proxmox"] = Endpoint }, ConnectorRuntime: "proxmox");

    private readonly List<HttpRequestMessage> _requests = [];
    private readonly Dictionary<string, string?> _configuration = new() { ["PORTARR_PROXMOX_TOKEN"] = "the-secret" };
    private HttpStatusCode _status = HttpStatusCode.OK;
    private string? _bodyForEverything;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));

    private const string Nodes = """{ "data": [ { "node": "other", "status": "online", "maxcpu": 2, "cpu": 0.9 }, { "node": "hv", "status": "online", "maxcpu": 8, "cpu": 0.25, "mem": 8000000000, "maxmem": 32000000000 } ] }""";

    private const string Containers = """
        { "data": [
          { "vmid": 103, "name": "jellyfin", "status": "running", "cpus": 4, "cpu": 0.5, "mem": 700000000, "maxmem": 4294967296, "disk": 4700000000, "maxdisk": 16000000000, "uptime": 7300, "tags": "gpu;staging" },
          { "vmid": "104", "name": "wiki", "status": "stopped", "cpus": 1, "cpu": 0, "mem": 0, "maxmem": 1073741824, "uptime": 0 }
        ] }
        """;

    private ProxmoxSource Source()
    {
        var factory = new ProxmoxHttpClientFactory(_ => new HttpClient(new Handler(this)) { BaseAddress = Endpoint.Url });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build();
        return new ProxmoxSource(new ProxmoxClient(factory, configuration), _clock);
    }

    [Fact]
    public async Task should_list_the_containers_as_workloads_with_a_read_only_token()
    {
        var workloads = await Source().ListAsync(Environment, TestContext.Current.CancellationToken);

        workloads.Select(w => (w.Id, w.Name, w.Image, w.State)).Should().Equal(("103", "jellyfin", "Proxmox LXC", WorkloadState.Running), ("104", "wiki", "Proxmox LXC", WorkloadState.Exited));
        workloads[0].StatusText.Should().Be("Up 2 hours");
        workloads[0].Node.Should().Be("hv");
        workloads[0].Labels.Should().Contain("proxmox.tags", "gpu;staging");
        _requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
        _requests[0].RequestUri!.AbsolutePath.Should().Be("/api2/json/nodes/hv/lxc");
        _requests[0].Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be("PVEAPIToken=portarr@pve!readonly=the-secret");
    }

    [Fact]
    public async Task should_sample_the_node_as_one_machine_and_the_running_containers_as_its_consumers()
    {
        var source = Source();
        var workloads = await source.ListAsync(Environment, TestContext.Current.CancellationToken);

        var sample = await source.SampleAsync(Environment, workloads, TestContext.Current.CancellationToken);

        var machine = sample.Machines.Should().ContainSingle().Subject;
        (machine.Id, machine.Kind, machine.CpuCapacityCores, machine.CpuCores, machine.MemoryBytes, machine.MemoryCapacityBytes, machine.Scope)
            .Should().Be(("hv", ProxmoxSource.MachineKind, 8, 2, 8_000_000_000, 32_000_000_000, UsageScope.Host));
        sample.Workloads.Should().ContainSingle().Which.Should().Be(new WorkloadUsageSample("103", 2, 700_000_000));
    }

    [Fact]
    public async Task should_give_the_usage_of_a_running_container_against_its_limit()
    {
        var usage = await Source().GetUsageAsync(Environment, "jellyfin", TestContext.Current.CancellationToken);

        usage.Should().Be(new WorkloadUsage(2, 700_000_000, 4_294_967_296));
        (await Source().GetUsageAsync(Environment, "wiki", TestContext.Current.CancellationToken)).Should().BeNull("a stopped container uses nothing");
    }

    [Fact]
    public async Task should_fail_when_the_node_is_not_the_one_declared()
    {
        var source = Source();
        var other = Environment with { Connectors = new Dictionary<string, object> { ["proxmox"] = Endpoint with { Node = "elsewhere" } } };

        await FluentActions.Awaiting(() => source.SampleAsync(other, [], TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void should_report_a_missing_token_as_not_configured_without_calling_the_node()
    {
        Source().GetConfigurationError(Environment).Should().BeNull();

        _configuration.Clear();

        Source().GetConfigurationError(Environment).Should().Be("token-missing");
        _requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task should_fail_loudly_when_proxmox_refuses_or_breaks_without_leaking_the_token(HttpStatusCode status)
    {
        _status = status;

        var act = () => Source().ListAsync(Environment, TestContext.Current.CancellationToken);

        var failure = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        failure.Message.Should().NotContain("the-secret").And.NotContain("secret-detail", "the body of the answer stays out of the failure");
    }

    [Theory]
    [InlineData("""{ "errors": { "node": "no such node" } }""")]
    [InlineData("""{ "data": "not a list" }""")]
    [InlineData("[]")]
    public async Task should_treat_any_answer_that_is_not_a_list_as_one_failure(string body)
    {
        _bodyForEverything = body;

        var act = () => Source().ListAsync(Environment, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task should_refuse_an_answer_past_the_size_cap()
    {
        _bodyForEverything = "{ \"data\": [\"" + new string('x', 5 * 1024 * 1024) + "\"] }";

        var act = () => Source().ListAsync(Environment, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>("a runaway answer is not a Proxmox node");
    }

    [Fact]
    public async Task should_not_read_without_a_token_and_say_why()
    {
        _configuration["PORTARR_PROXMOX_TOKEN"] = string.Empty;

        Source().GetConfigurationError(Environment).Should().Be(ReasonCodes.TokenMissing);
        var act = () => Source().ListAsync(Environment, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_say_so_when_the_node_is_not_among_the_nodes_proxmox_lists()
    {
        _bodyForEverything = """{ "data": [ { "node": "somewhere-else", "status": "online" } ] }""";

        var act = () => Source().SampleAsync(Environment, [], TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidDataException>()).Which.Message.Should().Contain("hv");
    }

    [Fact]
    public async Task should_report_an_offline_node_as_not_ready_with_no_figures()
    {
        _bodyForEverything = """{ "data": [ { "node": "hv", "status": "offline" } ] }""";

        var sample = await Source().SampleAsync(Environment, [], TestContext.Current.CancellationToken);

        var machine = sample.Machines.Should().ContainSingle().Subject;
        machine.CpuCapacityCores.Should().BeNull();
        machine.MemoryCapacityBytes.Should().BeNull();
    }

    [Fact]
    public void should_build_a_handler_that_never_follows_a_redirect_checks_the_address_and_pins_only_when_asked()
    {
        var pinned = ProxmoxHttpClientFactory.CreateHandler(Endpoint with { Fingerprint = new string('A', 64) });
        var unpinned = ProxmoxHttpClientFactory.CreateHandler(Endpoint);

        pinned.AllowAutoRedirect.Should().BeFalse();
        pinned.ConnectCallback.Should().NotBeNull("the address policy is checked when it connects");
        pinned.SslOptions.RemoteCertificateValidationCallback.Should().NotBeNull();
        unpinned.SslOptions.RemoteCertificateValidationCallback.Should().BeNull("without a fingerprint the system's trust applies, never accept-any");
    }

    [Fact]
    public void should_pin_the_certificate_by_its_sha256_whatever_the_case_of_the_fingerprint()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=pve", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var hash = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));
        var fingerprint = string.Join(':', Enumerable.Range(0, hash.Length / 2).Select(i => hash.Substring(i * 2, 2)));

        ProxmoxHttpClientFactory.Matches(certificate, fingerprint).Should().BeTrue();
        ProxmoxHttpClientFactory.Matches(certificate, fingerprint.ToLowerInvariant()).Should().BeTrue();
        ProxmoxHttpClientFactory.Matches(certificate, (fingerprint[0] == 'A' ? 'B' : 'A') + fingerprint[1..]).Should().BeFalse("the first digit differs whatever the key");
        ProxmoxHttpClientFactory.Matches(null, fingerprint).Should().BeFalse();
    }

    [Theory]
    [InlineData(30, "seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(600, "10 minutes")]
    [InlineData(3600, "1 hour")]
    [InlineData(7300, "2 hours")]
    [InlineData(86_400, "24 hours")]
    [InlineData(200_000, "2 days")]
    public void should_say_the_uptime_in_a_few_words(long seconds, string expected)
    {
        ProxmoxMapper.Duration(seconds).Should().Be(expected);
    }

    private sealed class Handler(ProxmoxSourceFixture owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            owner._requests.Add(request);
            if (owner._status != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(owner._status) { Content = new StringContent("""{ "errors": "secret-detail" }""") });
            }

            var body = owner._bodyForEverything ?? request.RequestUri!.AbsolutePath switch
            {
                "/api2/json/nodes" => Nodes,
                "/api2/json/nodes/hv/lxc" => Containers,
                _ => null,
            };
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
