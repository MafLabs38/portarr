using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Portarr.Core.Catalog;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.GitHub.Test;

/// <summary>The GitHub connector against the code host capability's contract.</summary>
public sealed class GitHubRepositorySourceContractFixture : CodeRepositorySourceContract
{
    private sealed class FakeGitHub : HttpMessageHandler
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

            var body = request.RequestUri!.AbsolutePath switch
            {
                "/user" => """{ "login": "someone" }""",
                "/users/example" => """{ "login": "example", "type": "Organization" }""",
                "/orgs/example/repos" => """[{ "name": "shop", "full_name": "example/shop", "owner": { "login": "example" }, "html_url": "https://github.com/example/shop", "private": false, "pushed_at": "2026-01-20T00:00:00Z", "default_branch": "main", "archived": false, "fork": false }]""",
                _ => "[]",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    protected override CodeRepositorySourceHarness Arrange()
    {
        var github = new FakeGitHub();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(GitHubRepositorySource.HttpClientName).Returns(_ => new HttpClient(github) { BaseAddress = GitHubRepositorySource.ApiBaseAddress });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GH_TOKEN"] = "test-token" }).Build();
        return new CodeRepositorySourceHarness(
            new GitHubRepositorySource(factory, configuration, new FakeTimeProvider(new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero))),
            new GitHubSource(["example"], "GH_TOKEN", 6, [], [], false, false),
            new FakeOtherSource(),
            () => github.Failing = true,
            () => github.Methods);
    }

    private sealed record FakeOtherSource : ICodeSource
    {
        public string Kind => "gitlab";

        public IReadOnlyList<string> Logins => [];

        public string Fingerprint => "gitlab";
    }
}
