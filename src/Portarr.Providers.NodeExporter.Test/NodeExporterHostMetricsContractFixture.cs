using System.Net;
using System.Text;
using NSubstitute;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.NodeExporter.Test;

/// <summary>node_exporter read over HTTP passes the host metrics contract (CLAUDE.md rule 8).</summary>
public sealed class NodeExporterHostMetricsContractFixture : HostMetricsSourceContract
{
    protected override HostMetricsHarness Arrange()
    {
        var requests = new List<RecordedRequest>();
        var text = string.Empty;
        var fault = ToolFault.None;
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(NodeExporterHostMetricsSource.HttpClientName).Returns(_ => new HttpClient(new FakeAgent(() => text, () => fault, requests)));
        var environment = new CatalogEnvironment("server-1", "Server 1", null);

        return new HostMetricsHarness(
            new NodeExporterHostMetricsSource(factory),
            new HostMetricsTarget(environment, "server-1", new Uri("http://10.0.0.10:9100/metrics")),
            new HostMetricsTarget(environment, "server-1", null, 9100),
            served => text = served,
            failure => fault = failure,
            () => requests.ToList());
    }

    private sealed class FakeAgent(Func<string> text, Func<ToolFault> fault, List<RecordedRequest> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath));
            return fault() switch
            {
                ToolFault.Unreachable => throw new HttpRequestException("connection refused"),
                ToolFault.Refused => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text(), Encoding.UTF8, "text/plain") }),
            };
        }
    }
}
