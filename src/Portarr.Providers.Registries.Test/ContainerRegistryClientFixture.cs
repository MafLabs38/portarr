using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Portarr.Core.Maintenance;

namespace Portarr.Providers.Registries.Test;

/// <summary>Anonymous tag lookups (D-66): Docker Hub's API, the OCI token flow, honest reasons.</summary>
public sealed class ContainerRegistryClientFixture
{
    private const string Challenge = "Bearer realm=\"https://auth.example/token\",service=\"registry.example\",scope=\"repository:team/app:pull\"";

    private readonly List<HttpRequestMessage> _requests = [];
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _answers = [];

    [Fact]
    public async Task should_read_docker_hub_tags_with_their_date_and_digest_newest_first()
    {
        Json("https://hub.docker.com/v2/namespaces/library/repositories/postgres/tags?page_size=100&ordering=last_updated", """
            { "next": "https://hub.docker.com/v2/namespaces/library/repositories/postgres/tags?page=2&page_size=100&ordering=last_updated",
              "results": [{ "name": "18-alpine", "last_updated": "2026-01-30T07:11:43Z", "digest": "sha256:aa" }] }
            """);
        Json("https://hub.docker.com/v2/namespaces/library/repositories/postgres/tags?page=2&page_size=100&ordering=last_updated", """
            { "next": null, "results": [{ "name": "16-alpine", "last_updated": "2025-11-01T00:00:00Z" }] }
            """);

        var tags = await Subject().ListTagsAsync(ImageReference.Parse("postgres:16-alpine")!, TestContext.Current.CancellationToken);

        tags.Select(t => t.Name).Should().Equal("18-alpine", "16-alpine");
        tags[0].Should().Be(new RegistryTag("18-alpine", new DateTimeOffset(2026, 1, 30, 7, 11, 43, TimeSpan.Zero), "sha256:aa"));
    }

    [Fact]
    public async Task should_get_an_anonymous_pull_token_from_the_challenge_and_retry_with_it()
    {
        _answers["https://registry.example/v2/team/app/tags/list?n=1000"] = request => request.Headers.Authorization?.Parameter == "anon"
            ? Ok("""{ "tags": ["1.0", "1.1"] }""")
            : Unauthorized();
        Json("https://auth.example/token?scope=repository%3Ateam%2Fapp%3Apull&service=registry.example", """{ "token": "anon" }""");

        var tags = await Subject().ListTagsAsync(ImageReference.Parse("registry.example/team/app:1.0")!, TestContext.Current.CancellationToken);

        tags.Select(t => t.Name).Should().Equal("1.0", "1.1");
        _requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    [Theory]
    [InlineData("""{ "access_token": "anon" }""", true)]
    [InlineData("""{ "token": "", "access_token": "anon" }""", true)]
    [InlineData("""{ "token": "" }""", false)]
    public async Task should_read_the_token_under_either_name_and_refuse_an_empty_one(string body, bool accepted)
    {
        _answers["https://registry.example/v2/team/app/tags/list?n=1000"] = request => request.Headers.Authorization?.Parameter == "anon"
            ? Ok("""{ "tags": ["1.0"] }""")
            : Unauthorized();
        Json("https://auth.example/token?scope=repository%3Ateam%2Fapp%3Apull&service=registry.example", body);

        var action = () => Subject().ListTagsAsync(ImageReference.Parse("registry.example/team/app:1.0")!, TestContext.Current.CancellationToken);

        if (accepted)
        {
            (await action()).Select(t => t.Name).Should().Equal("1.0");
        }
        else
        {
            (await action.Should().ThrowAsync<RegistryUnavailableException>()).Which.Reason.Should().Be("refused");
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "not-found")]
    [InlineData(HttpStatusCode.Forbidden, "refused")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate-limited")]
    public async Task should_turn_a_registry_refusal_into_a_reason(HttpStatusCode status, string reason)
    {
        _answers["https://hub.docker.com/v2/namespaces/library/repositories/app/tags?page_size=100&ordering=last_updated"] = _ => new HttpResponseMessage(status);

        var action = () => Subject().ListTagsAsync(ImageReference.Parse("app:1")!, TestContext.Current.CancellationToken);

        (await action.Should().ThrowAsync<RegistryUnavailableException>()).Which.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task should_refuse_a_token_realm_that_is_not_https()
    {
        _answers["https://registry.example/v2/team/app/tags/list?n=1000"] = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            response.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer realm=\"http://10.0.0.1/token\"");
            return response;
        };

        var action = () => Subject().ListTagsAsync(ImageReference.Parse("registry.example/team/app:1.0")!, TestContext.Current.CancellationToken);

        (await action.Should().ThrowAsync<RegistryUnavailableException>()).Which.Reason.Should().Be("refused");
        _requests.Should().OnlyContain(r => r.RequestUri!.Host == "registry.example");
    }

    [Fact]
    public async Task should_describe_a_tag_by_its_digest_and_the_creation_date_of_its_amd64_image()
    {
        _answers["https://registry.example/v2/team/app/manifests/1.1"] = _ =>
        {
            var response = Ok("""{ "manifests": [{ "digest": "sha256:arm", "platform": { "os": "linux", "architecture": "arm64" } }, { "digest": "sha256:amd", "platform": { "os": "linux", "architecture": "amd64" } }] }""");
            response.Headers.Add("Docker-Content-Digest", "sha256:index");
            return response;
        };
        Json("https://registry.example/v2/team/app/manifests/sha256:amd", """{ "config": { "digest": "sha256:config" } }""");
        Json("https://registry.example/v2/team/app/blobs/sha256:config", """{ "created": "2026-01-20T10:00:00Z" }""");

        var tag = await Subject().DescribeAsync(ImageReference.Parse("registry.example/team/app:1.0")!, "1.1", TestContext.Current.CancellationToken);

        tag.Should().Be(new RegistryTag("1.1", new DateTimeOffset(2026, 1, 20, 10, 0, 0, TimeSpan.Zero), "sha256:index"));
    }

    private void Json(string url, string body) => _answers[url] = _ => Ok(body);

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Unauthorized()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.TryAddWithoutValidation("WWW-Authenticate", Challenge);
        return response;
    }

    private ContainerRegistryClient Subject()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ContainerRegistryClient.HttpClientName).Returns(_ => new HttpClient(new Handler(this)));
        return new ContainerRegistryClient(factory, new FakeTimeProvider());
    }

    private sealed class Handler(ContainerRegistryClientFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            fixture._requests.Add(request);
            return Task.FromResult(fixture._answers.TryGetValue(request.RequestUri!.AbsoluteUri, out var answer)
                ? answer(request)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
