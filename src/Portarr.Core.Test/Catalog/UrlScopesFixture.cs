using Portarr.Core.Catalog;

namespace Portarr.Core.Test.Catalog;

public sealed class UrlScopesFixture
{
    [Theory]
    [InlineData("http://10.0.0.10:8096", UrlScope.Lan)]
    [InlineData("http://172.20.1.5", UrlScope.Lan)]
    [InlineData("http://192.168.1.20:8086", UrlScope.Lan)]
    [InlineData("http://127.0.0.1:8484", UrlScope.Lan)]
    [InlineData("http://[fd00::10]:80", UrlScope.Lan)]
    [InlineData("http://localhost:3000", UrlScope.Lan)]
    [InlineData("http://nas.local", UrlScope.Lan)]
    [InlineData("http://server-1:8080", UrlScope.Lan)]
    [InlineData("https://jellyfin.homelab.example", UrlScope.Public)]
    [InlineData("http://172.32.0.1", UrlScope.Public)]
    [InlineData("http://8.8.8.8", UrlScope.Public)]
    public void should_infer_the_scope_from_the_host(string url, UrlScope expected)
    {
        UrlScopes.Infer(new Uri(url)).Should().Be(expected);
    }
}
