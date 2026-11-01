using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Portarr.Core.Maintenance;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Registries.Test;

/// <summary>The OCI registry client against the image registry capability's contract.</summary>
public sealed class ContainerRegistryClientContractFixture : ImageRegistryContract
{
    private sealed class FakeRegistry : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];

        public bool Failing { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            if (Failing)
            {
                throw new HttpRequestException("connection refused");
            }

            return Task.FromResult(request.RequestUri!.PathAndQuery switch
            {
                "/v2/team/app/tags/list?n=1000" => Json("""{ "tags": ["1.0", "1.1"] }"""),
                "/v2/team/app/manifests/1.1" => Json("""{ "config": { "digest": "sha256:config" } }"""),
                "/v2/team/app/blobs/sha256:config" => Json("""{ "created": "2026-01-20T10:00:00Z" }"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    protected override ImageRegistryHarness Arrange()
    {
        var registry = new FakeRegistry();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ContainerRegistryClient.HttpClientName).Returns(_ => new HttpClient(registry));
        return new ImageRegistryHarness(
            new ContainerRegistryClient(factory, new FakeTimeProvider()),
            ImageReference.Parse("registry.example/team/app:1.0")!,
            "1.1",
            ImageReference.Parse("registry.example/team/gone:1.0")!,
            () => registry.Failing = true,
            () => registry.Methods);
    }
}
