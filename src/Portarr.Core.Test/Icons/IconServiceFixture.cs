using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Core.Icons;
using Portarr.Core.Status;

namespace Portarr.Core.Test.Icons;

public sealed class IconServiceFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-icons-").FullName;
    private readonly FakeCdn _cdn = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly IFleetService _fleet = Substitute.For<IFleetService>();
    private readonly IconService _subject;

    public IconServiceFixture()
    {
        UsedIcons("jellyfin", "trading", "oldapp", "nothing");

        var options = Options.Create(new PortarrOptions { DataFolder = _folder, CatalogFile = Path.Combine(_folder, "catalog.yaml") });
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(IconService.HttpClientName).Returns(_ => new HttpClient(_cdn, disposeHandler: false) { BaseAddress = new Uri("https://icons.example/") });
        _subject = new IconService(options, factory, _fleet, _clock, NullLogger<IconService>.Instance);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        _cdn.Dispose();
    }

    [Fact]
    public async Task should_prefer_the_operator_icon_over_the_community_set()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "icons"));
        await File.WriteAllBytesAsync(Path.Combine(_folder, "icons", "trading.png"), [1, 2, 3], TestContext.Current.CancellationToken);
        _cdn.Files["svg/trading.svg"] = "<svg/>";

        var icon = await Get("trading");

        icon.Should().Be(new IconFile(Path.Combine(_folder, "icons", "trading.png"), "image/png"));
        _cdn.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_download_a_community_icon_once_then_serve_it_from_the_cache()
    {
        _cdn.Files["svg/jellyfin.svg"] = "<svg/>";

        var first = await Get("jellyfin");
        var second = await Get("jellyfin");

        second.Should().Be(first);
        first!.ContentType.Should().Be("image/svg+xml");
        _cdn.Requests.Should().Equal("svg/jellyfin.svg");
    }

    [Fact]
    public async Task should_fall_back_to_the_png_version()
    {
        _cdn.Files["png/oldapp.png"] = "png-bytes";

        (await Get("oldapp"))!.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task should_ask_again_for_a_missing_icon_only_after_a_day()
    {
        await Get("nothing");
        await Get("nothing");
        _clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1));
        await Get("nothing");

        _cdn.Requests.Should().HaveCount(4, "svg then png, once at first, once after a day");
    }

    [Fact]
    public async Task should_retry_a_few_minutes_after_the_source_was_unreachable()
    {
        _cdn.Offline = true;
        (await Get("jellyfin")).Should().BeNull();
        (await Get("jellyfin")).Should().BeNull();

        _cdn.Offline = false;
        _cdn.Files["svg/jellyfin.svg"] = "<svg/>";
        _clock.Advance(TimeSpan.FromMinutes(6));

        (await Get("jellyfin")).Should().NotBeNull();
        _cdn.Requests.Should().HaveCount(2, "one failed attempt, then one after the delay");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_not_cache_an_icon_bigger_than_the_limit(bool withoutContentLength)
    {
        _cdn.Files["svg/jellyfin.svg"] = new string('x', (int)IconService.MaxIconBytes + 1);
        _cdn.Chunked = withoutContentLength;

        (await Get("jellyfin")).Should().BeNull();
        var cache = Path.Combine(_folder, "cache", "icons");
        (Directory.Exists(cache) ? Directory.GetFiles(cache) : []).Should().BeEmpty();
    }

    [Theory]
    [InlineData("unused-by-the-catalog")]
    [InlineData("../etc/passwd")]
    [InlineData("a/b")]
    public async Task should_never_download_a_name_the_catalog_does_not_use(string name)
    {
        (await Get(name)).Should().BeNull();
        _cdn.Requests.Should().BeEmpty();
    }

    private Task<IconFile?> Get(string name) => _subject.GetAsync(name, TestContext.Current.CancellationToken);

    private void UsedIcons(params string[] icons)
    {
        _fleet.GetApps().Returns(icons.Select(icon => new AppView(
            new CatalogApp(icon, icon, AppType.Web, icon, null, [], [], []),
            new ResolvedStatus(HealthStatus.Unknown, ""), null, null, [], [], icon)).ToList());
    }

    private sealed class FakeCdn : HttpMessageHandler
    {
        public Dictionary<string, string> Files { get; } = [];

        public List<string> Requests { get; } = [];

        public bool Offline { get; set; }

        /// <summary>Bodies without Content-Length, as a chunked response.</summary>
        public bool Chunked { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            Requests.Add(path);
            if (Offline)
            {
                throw new HttpRequestException("Network unreachable");
            }

            if (!Files.TryGetValue(path, out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            HttpContent body = Chunked ? new ChunkedContent(content) : new StringContent(content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        }
    }

    private sealed class ChunkedContent(string content) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            return stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(content)).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
