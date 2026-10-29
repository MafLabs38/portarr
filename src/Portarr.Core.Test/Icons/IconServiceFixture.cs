using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Configuration;
using Portarr.Core.Icons;

namespace Portarr.Core.Test.Icons;

public sealed class IconServiceFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-icons-").FullName;
    private readonly FakeHandler _cdn = new();
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    private string CustomFolder => Path.Combine(_folder, "icons");

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        _cdn.Dispose();
    }

    [Fact]
    public async Task should_prefer_the_operator_icon_over_the_community_set()
    {
        Directory.CreateDirectory(CustomFolder);
        await File.WriteAllBytesAsync(Path.Combine(CustomFolder, "trading.png"), [1, 2, 3], TestContext.Current.CancellationToken);
        _cdn.Respond("svg/trading.svg", "<svg/>");

        var icon = await Subject().GetAsync("trading", TestContext.Current.CancellationToken);

        icon.Should().Be(new IconFile(Path.Combine(CustomFolder, "trading.png"), "image/png"));
        _cdn.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_download_a_community_icon_once_then_serve_it_from_the_cache()
    {
        _cdn.Respond("svg/jellyfin.svg", "<svg/>");
        var subject = Subject();

        var first = await subject.GetAsync("jellyfin", TestContext.Current.CancellationToken);
        var second = await subject.GetAsync("jellyfin", TestContext.Current.CancellationToken);

        first!.ContentType.Should().Be("image/svg+xml");
        second.Should().Be(first);
        (await File.ReadAllTextAsync(first.Path, TestContext.Current.CancellationToken)).Should().Be("<svg/>");
        _cdn.Requests.Should().Equal("svg/jellyfin.svg");
    }

    [Fact]
    public async Task should_fall_back_to_the_png_version()
    {
        _cdn.Respond("png/oldapp.png", "png-bytes");

        var icon = await Subject().GetAsync("oldapp", TestContext.Current.CancellationToken);

        icon!.ContentType.Should().Be("image/png");
        _cdn.Requests.Should().Equal("svg/oldapp.svg", "png/oldapp.png");
    }

    [Fact]
    public async Task should_not_ask_again_for_a_missing_icon_before_a_day()
    {
        var subject = Subject();

        (await subject.GetAsync("nothing", TestContext.Current.CancellationToken)).Should().BeNull();
        (await subject.GetAsync("nothing", TestContext.Current.CancellationToken)).Should().BeNull();

        _cdn.Requests.Should().HaveCount(2, "svg then png, only once");
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("Sonarr")]
    [InlineData("a/b")]
    public async Task should_refuse_names_that_are_not_icon_names(string name)
    {
        (await Subject().GetAsync(name, TestContext.Current.CancellationToken)).Should().BeNull();
        _cdn.Requests.Should().BeEmpty();
    }

    private IconService Subject()
    {
        var options = Options.Create(new PortarrOptions
        {
            DataFolder = _folder,
            CatalogFile = Path.Combine(_folder, "catalog.yaml"),
        });
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(IconService.HttpClientName).Returns(_ => new HttpClient(_cdn, disposeHandler: false) { BaseAddress = new Uri("https://icons.example/") });

        return new IconService(options, factory, _clock, NullLogger<IconService>.Instance);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _files = [];

        public List<string> Requests { get; } = [];

        public void Respond(string path, string content) => _files[path] = content;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            Requests.Add(path);

            return Task.FromResult(_files.TryGetValue(path, out var content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
