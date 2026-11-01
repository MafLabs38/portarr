using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Portarr.Core.Catalog;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Cloudflare.Test;

/// <summary>The Cloudflare workload connector against the workload contract, with a fake Cloudflare API (D-31, D-53).</summary>
public sealed class CloudflareWorkloadSourceContractFixture : WorkloadSourceContract
{
    private const string AccountId = "0123456789abcdef0123456789abcdef";

    protected override WorkloadSourceHarness Arrange()
    {
        var methods = new List<HttpMethod>();
        var fault = ToolFault.None;
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(CloudflareWorkloadSource.HttpClientName)
            .Returns(_ => new HttpClient(new FakeApi(() => fault, methods)) { BaseAddress = CloudflareWorkloadSource.ApiBaseAddress });
        var source = new CloudflareWorkloadSource(factory, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CF_TOKEN"] = "test-token" }).Build());

        return new WorkloadSourceHarness(
            source,
            CloudflareEnvironments.With(new CloudflareAccount(AccountId, "CF_TOKEN")),
            new CatalogEnvironment("server-1", "Server 1"),
            "site",
            failure => fault = failure,
            () => methods.ToList());
    }

    private sealed class FakeApi(Func<ToolFault> fault, List<HttpMethod> methods) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            methods.Add(request.Method);
            if (fault() == ToolFault.Unreachable)
            {
                throw new HttpRequestException("connection refused");
            }

            var body = request.RequestUri!.AbsolutePath.EndsWith("/pages/projects", StringComparison.Ordinal)
                ? """{ "success": true, "result_info": { "page": 1, "total_pages": 1 }, "result": [{ "id": "1", "name": "site" }] }"""
                : """{ "success": true, "result": [] }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
