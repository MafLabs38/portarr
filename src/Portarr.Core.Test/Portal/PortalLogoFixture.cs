using Portarr.Core.Portal;

namespace Portarr.Core.Test.Portal;

public sealed class PortalLogoFixture : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-logo-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("branding/logo.png")]
    [InlineData("branding/Logo.SVG")]
    [InlineData("img/my-logo.webp")]
    [InlineData("logo.png")]
    public void should_accept_a_relative_path_to_an_image_inside_the_folder(string path)
    {
        PortalLogo.ValidatePath(path).Should().BeNull();
    }

    [Theory]
    [InlineData("../logo.png")]
    [InlineData("branding/../../logo.png")]
    [InlineData("/etc/logo.png")]
    [InlineData("~/logo.png")]
    [InlineData("C:/logo.png")]
    [InlineData("branding\\logo.png")]
    [InlineData("branding//logo.png")]
    [InlineData("branding/.hidden.png")]
    [InlineData("branding/logo.gif")]
    [InlineData("catalog.yaml")]
    public void should_refuse_a_path_that_leaves_the_folder_or_is_not_a_supported_image(string path)
    {
        PortalLogo.ValidatePath(path).Should().NotBeNull();
    }

    [Fact]
    public void should_check_the_signature_of_a_png_and_a_webp()
    {
        PortalLogo.Inspect(Png, ".png").Format.Should().Be(new LogoFormat("png", "image/png"));
        PortalLogo.Inspect("RIFF\0\0\0\0WEBPVP8 "u8, "webp").Format!.ContentType.Should().Be("image/webp");
        PortalLogo.Inspect(Png, ".webp").Error.Should().NotBeNull("a png renamed to webp is not a webp");
        PortalLogo.Inspect("<html></html>"u8, ".png").Error.Should().NotBeNull();
        PortalLogo.Inspect(Png, ".gif").Error.Should().NotBeNull();
    }

    [Fact]
    public void should_refuse_an_empty_or_too_large_image()
    {
        PortalLogo.Inspect([], ".png").Error.Should().NotBeNull();
        PortalLogo.Inspect(new byte[PortalLogo.MaxBytes + 1], ".png").Error.Should().NotBeNull();
    }

    [Fact]
    public void should_accept_a_plain_svg_and_refuse_one_that_runs_code()
    {
        const string Plain = """<?xml version="1.0"?><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><circle cx="5" cy="5" r="4"/></svg>""";
        PortalLogo.Inspect(System.Text.Encoding.UTF8.GetBytes(Plain), "svg").Format!.ContentType.Should().Be("image/svg+xml");

        foreach (var hostile in new[]
        {
            """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""",
            """<svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)"/>""",
            """<svg xmlns="http://www.w3.org/2000/svg"><a href="javascript:alert(1)"><circle r="1"/></a></svg>""",
            """<svg xmlns="http://www.w3.org/2000/svg"><foreignObject><div/></foreignObject></svg>""",
            """<!DOCTYPE svg [<!ENTITY x "y">]><svg xmlns="http://www.w3.org/2000/svg"/>""",
            """<html xmlns="http://www.w3.org/1999/xhtml"/>""",
            "not xml at all",
        })
        {
            PortalLogo.Inspect(System.Text.Encoding.UTF8.GetBytes(hostile), "svg").Error.Should().NotBeNull(hostile);
        }
    }

    [Fact]
    public void should_resolve_only_files_inside_the_folder()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "branding"));
        var inside = Path.Combine(_folder, "branding", "logo.png");
        File.WriteAllBytes(inside, Png);
        var outside = Path.Combine(Directory.CreateTempSubdirectory("portarr-outside-").FullName, "secret.png");
        File.WriteAllBytes(outside, Png);
        File.CreateSymbolicLink(Path.Combine(_folder, "branding", "link.png"), outside);

        PortalLogo.Resolve(_folder, "branding/logo.png").Should().Be(inside);
        PortalLogo.Resolve(_folder, "branding/missing.png").Should().BeNull();
        PortalLogo.Resolve(_folder, "branding/link.png").Should().BeNull("a link out of the folder is not followed");
        PortalLogo.Resolve(_folder, "../secret.png").Should().BeNull();
        Directory.Delete(Path.GetDirectoryName(outside)!, recursive: true);
    }
}
