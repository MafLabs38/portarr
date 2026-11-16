using Portarr.Core.Maintenance;

namespace Portarr.Core.Test.Maintenance;

public sealed class ImageReferenceFixture
{
    [Theory]
    [InlineData("postgres:16", "docker.io", "library/postgres", "16", "postgres:16")]
    [InlineData("grafana/grafana", "docker.io", "grafana/grafana", "latest", "grafana/grafana:latest")]
    [InlineData("index.docker.io/library/nginx:alpine", "docker.io", "library/nginx", "alpine", "nginx:alpine")]
    [InlineData("lscr.io/linuxserver/sonarr:4.0.20", "lscr.io", "linuxserver/sonarr", "4.0.20", "lscr.io/linuxserver/sonarr:4.0.20")]
    [InlineData("ghcr.io/Example/App:1.2", "ghcr.io", "example/app", "1.2", "ghcr.io/example/app:1.2")]
    [InlineData("registry.example:5000/team/app:2", "registry.example:5000", "team/app", "2", "registry.example:5000/team/app:2")]
    public void should_normalize_like_the_docker_cli(string value, string registry, string repository, string tag, string display)
    {
        var image = ImageReference.Parse(value)!;

        image.Registry.Should().Be(registry);
        image.Repository.Should().Be(repository);
        image.Tag.Should().Be(tag);
        image.Display.Should().Be(display);
    }

    [Theory]
    [InlineData("Proxmox LXC")]
    [InlineData("Cloudflare Pages")]
    [InlineData("Cloudflare Worker")]
    public void should_not_read_the_kind_a_connector_without_images_puts_in_the_image_field(string kind)
    {
        ImageReference.Parse(kind).Should().BeNull("such a workload runs no image: the maintenance pass must not look it up");
    }

    [Fact]
    public void should_keep_a_digest_and_recognize_an_image_id()
    {
        var digest = "sha256:" + new string('a', 64);

        ImageReference.Parse($"example/quake@{digest}")!.Should().Match<ImageReference>(i => i.Tag == null && i.Digest == digest && i.Display == "example/quake@sha256:aaaaaaaaaaaa");
        ImageReference.Parse(digest)!.IsImageId.Should().BeTrue();
        ImageReference.Parse("Cloudflare Pages").Should().BeNull();
    }
}
