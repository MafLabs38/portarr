using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Kubernetes.Test;

/// <summary>
/// A Kubernetes API server for the contract suites: it answers by path (<see cref="Answer"/>), refuses or goes
/// silent when a scenario says so, and records every call. The connector under test talks to it through the real
/// <see cref="KubernetesApiClient"/>.
/// </summary>
public sealed class FakeCluster : IDisposable
{
    public const string Token = "never-shown-token";

    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-k8s-contract-").FullName;
    private readonly List<RecordedRequest> _requests = [];

    public FakeCluster(Func<string, string?> answer)
    {
        Answer = answer;
        Client = new KubernetesApiClient(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CLUSTER_TOKEN"] = Token }).Build(),
            Options.Create(new PortarrOptions { CatalogFile = Path.Combine(_folder, "catalog.yaml") }),
            (_, _) => new Handler(this));
    }

    /// <summary>The body for a path and query (<c>/apis/x?limit=500</c>), or null for a 404.</summary>
    public Func<string, string?> Answer { get; }

    public ToolFault Fault { get; set; }

    public KubernetesApiClient Client { get; }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    public static CatalogEnvironment Environment(KubernetesNodeExporter? nodeExporter = null) =>
        new("cluster", "Cluster", null, Kubernetes: new KubernetesCluster(new Uri("https://10.0.0.10:6443"), "CLUSTER_TOKEN", null, NodeExporter: nodeExporter));

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    private sealed class Handler(FakeCluster cluster) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (cluster._requests)
            {
                cluster._requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath));
            }

            if (cluster.Fault == ToolFault.Unreachable)
            {
                throw new HttpRequestException("connection refused");
            }

            if (cluster.Fault == ToolFault.Refused)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
            }

            var body = cluster.Answer(request.RequestUri.PathAndQuery);
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
