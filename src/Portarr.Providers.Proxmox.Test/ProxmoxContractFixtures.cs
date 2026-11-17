using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Proxmox.Test;

/// <summary>A fake Proxmox API (a node and one running container) and the source wired to it, shared by the capability contracts.</summary>
internal sealed class FakeProxmox
{
    private static readonly ProxmoxEndpoint Endpoint = new(new Uri("https://10.0.0.30:8006/api2/json/"), "hv", "portarr@pve!readonly", "PORTARR_PROXMOX_TOKEN", null);

    public static readonly CatalogEnvironment Environment = new("hv", "Hypervisor", null, Connectors: new Dictionary<string, object> { ["proxmox"] = Endpoint }, ConnectorRuntime: "proxmox");

    public static readonly CatalogEnvironment Unsupported = new("server-1", "Server 1");

    private ToolFault _fault = ToolFault.None;

    public List<HttpMethod> Methods { get; } = [];

    public ProxmoxSource Source { get; }

    public FakeProxmox()
    {
        var factory = new ProxmoxHttpClientFactory(_ => new HttpClient(new Handler(this)) { BaseAddress = Endpoint.Url });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PORTARR_PROXMOX_TOKEN"] = "the-secret" }).Build();
        Source = new ProxmoxSource(new ProxmoxClient(factory, configuration), TimeProvider.System);
    }

    public void Fail(ToolFault fault) => _fault = fault;

    private sealed class Handler(FakeProxmox owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            owner.Methods.Add(request.Method);
            if (owner._fault == ToolFault.Unreachable)
            {
                throw new HttpRequestException("connection refused");
            }

            var body = request.RequestUri!.AbsolutePath switch
            {
                "/api2/json/nodes" => """{ "data": [ { "node": "hv", "status": "online", "maxcpu": 8, "cpu": 0.25, "mem": 8000000000, "maxmem": 32000000000 } ] }""",
                "/api2/json/nodes/hv/lxc" => """{ "data": [ { "vmid": 103, "name": "jellyfin", "status": "running", "cpus": 4, "cpu": 0.5, "mem": 700000000, "maxmem": 4294967296, "uptime": 7300 } ] }""",
                _ => null,
            };
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

/// <summary>The Proxmox workload connector against the workload contract, with a fake Proxmox API (D-31, D-128).</summary>
public sealed class ProxmoxWorkloadSourceContractFixture : WorkloadSourceContract
{
    protected override WorkloadSourceHarness Arrange()
    {
        var api = new FakeProxmox();
        return new WorkloadSourceHarness(api.Source, FakeProxmox.Environment, FakeProxmox.Unsupported, "jellyfin", api.Fail, () => api.Methods.ToList());
    }
}

/// <summary>The Proxmox infrastructure connector against the infrastructure contract, with a fake Proxmox API (D-54, D-128).</summary>
public sealed class ProxmoxInfrastructureSourceContractFixture : InfrastructureSourceContract
{
    protected override InfrastructureSourceHarness Arrange()
    {
        var api = new FakeProxmox();
        var workloads = api.Source.ListAsync(FakeProxmox.Environment, CancellationToken.None).GetAwaiter().GetResult();
        api.Methods.Clear();
        return new InfrastructureSourceHarness(api.Source, FakeProxmox.Environment, FakeProxmox.Unsupported, workloads, api.Fail, () => api.Methods.ToList());
    }
}

/// <summary>The <c>proxmox:</c> section against the connector contract (ADR 0013).</summary>
public sealed class ProxmoxEnvironmentConnectorContractFixture : EnvironmentConnectorContract
{
    protected override IEnvironmentConnector Connector { get; } = new ProxmoxEnvironmentConnector();

    protected override string ValidSection => "{ url: \"https://10.0.0.30:8006\", tokenId: \"portarr@pve!readonly\", tokenEnv: PORTARR_PROXMOX_TOKEN }";

    protected override string? SectionWithCredentialsInUrl => "{ url: \"https://user:pass@10.0.0.30:8006\", tokenId: \"portarr@pve!readonly\", tokenEnv: PORTARR_PROXMOX_TOKEN }";

    protected override string? SectionWithLiteralSecret => "{ url: \"https://10.0.0.30:8006\", tokenId: \"portarr@pve!readonly\", tokenEnv: \"s3cret value\" }";

    protected override string? SectionWithSecretOutsideNamespace => "{ url: \"https://10.0.0.30:8006\", tokenId: \"portarr@pve!readonly\", tokenEnv: DATABASE_PASSWORD }";
}
