using System.Net;
using Portarr.Core.Platforms;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Platforms.Test;

public sealed class K3sReleaseLineSourceContractFixture : ReleaseLineSourceContract
{
    protected override ReleaseLineHarness Arrange()
    {
        var broken = false;
        var http = new StubHttp(_ => broken ? new HttpResponseMessage(HttpStatusCode.BadGateway) : StubHttp.Json(K3sReleaseLineSourceFixture.Channels));
        return new ReleaseLineHarness(new K3sReleaseLineSource(http), "k3s", () => broken = true);
    }
}

public sealed class K3sReleaseLineSourceFixture
{
    /// <summary>The shape of the real channels answer: named channels, one per line, and pre-release channels with no version.</summary>
    internal const string Channels = """
        { "type": "collection", "data": [
          { "id": "stable", "name": "stable", "latest": "v1.32.2+k3s1" },
          { "id": "latest", "name": "latest", "latest": "v1.33.1+k3s1" },
          { "id": "testing", "name": "testing", "latest": "v1.34.0-rc1+k3s1" },
          { "id": "v1.31", "name": "v1.31", "latest": "v1.31.6+k3s1" },
          { "id": "v1.31-testing", "name": "v1.31-testing", "latest": null },
          { "id": "v1.32", "name": "v1.32", "latest": "v1.32.2+k3s1" },
          { "id": "v1.33", "name": "v1.33", "latest": "v1.33.1+k3s1" },
          { "id": "v1.34", "name": "v1.34", "latest": "v1.34.0-rc1+k3s1" },
          { "id": "v1.30", "name": "v1.30", "latest": "not a version" }
        ] }
        """;

    [Fact]
    public async Task should_list_the_release_lines_newest_first_and_skip_what_is_not_a_line()
    {
        var source = new K3sReleaseLineSource(new StubHttp(_ => StubHttp.Json(Channels)));

        var lines = await source.ReadAsync(TestContext.Current.CancellationToken);

        lines.Should().Equal(new ReleaseLine("1.33", "v1.33.1+k3s1"), new ReleaseLine("1.32", "v1.32.2+k3s1"), new ReleaseLine("1.31", "v1.31.6+k3s1"));
    }

    [Fact]
    public async Task should_ask_the_k3s_channels_with_a_get_and_nothing_else()
    {
        var http = new StubHttp(_ => StubHttp.Json(Channels));

        await new K3sReleaseLineSource(http).ReadAsync(TestContext.Current.CancellationToken);

        http.Requests.Should().Equal(K3sReleaseLineSource.ChannelsUrl);
    }

    [Theory]
    [InlineData("""{ "type": "collection" }""")]
    [InlineData("""{ "data": "x" }""")]
    [InlineData("not json")]
    public async Task should_refuse_an_answer_that_is_not_the_channels(string body)
    {
        var source = new K3sReleaseLineSource(new StubHttp(_ => StubHttp.Json(body)));

        var act = () => source.ReadAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
    }
}
