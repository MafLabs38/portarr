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
    }

    [Theory]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)"/>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><a href="javascript:alert(1)"><circle r="1"/></a></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><foreignObject><div/></foreignObject></svg>""")]
    [InlineData("""<!DOCTYPE svg [<!ENTITY x "y">]><svg xmlns="http://www.w3.org/2000/svg"/>""")]
    [InlineData("""<html xmlns="http://www.w3.org/1999/xhtml"/>""")]
    [InlineData("not xml at all")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"><image xlink:href="https://attacker.example/pixel.png"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><use href="https://attacker.example/other.svg#x"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><image href="data:text/html;base64,PHNjcmlwdD4="/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><style>@import url(https://attacker.example/x.css);</style></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><style>.a { background: url("https://attacker.example/x.png") }</style></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><rect style="fill: url(https://attacker.example/x.svg#p)"/></svg>""")]
    public void should_refuse_an_svg_that_runs_code_or_loads_from_outside(string hostile)
    {
        PortalLogo.Inspect(System.Text.Encoding.UTF8.GetBytes(hostile), "svg").Error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><defs><linearGradient id="g"/></defs><rect fill="url(#g)"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"><defs><circle id="c" r="1"/></defs><use xlink:href="#c"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><style>.a { fill: url(#g) }</style><rect class="a"/></svg>""")]
    public void should_accept_an_svg_that_only_refers_to_itself(string svg)
    {
        PortalLogo.Inspect(System.Text.Encoding.UTF8.GetBytes(svg), "svg").Error.Should().BeNull();
    }

    [Fact]
    public void should_resolve_only_files_inside_the_folder()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "branding"));
        var inside = Path.Combine(_folder, "branding", "logo.png");
        File.WriteAllBytes(inside, Png);

        PortalLogo.Resolve(_folder, "branding/logo.png").Should().Be(inside);
        PortalLogo.Resolve(_folder, "branding/missing.png").Should().BeNull();
        PortalLogo.Resolve(_folder, "../secret.png").Should().BeNull();
    }

    [Fact]
    public void should_not_follow_a_link_to_a_file_or_a_folder_outside()
    {
        var outsideFolder = Directory.CreateTempSubdirectory("portarr-outside-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(_folder, "branding"));
            File.WriteAllBytes(Path.Combine(outsideFolder, "secret.png"), Png);
            try
            {
                File.CreateSymbolicLink(Path.Combine(_folder, "branding", "link.png"), Path.Combine(outsideFolder, "secret.png"));
                Directory.CreateSymbolicLink(Path.Combine(_folder, "branding", "linked-folder"), outsideFolder);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return; // links cannot be made here (Windows without the privilege)
            }

            PortalLogo.Resolve(_folder, "branding/link.png").Should().BeNull("a link out of the folder is not followed");
            PortalLogo.Resolve(_folder, "branding/linked-folder/secret.png").Should().BeNull("nor is a linked folder, whose files look inside it");
        }
        finally
        {
            Directory.Delete(outsideFolder, recursive: true);
        }
    }

    [Fact]
    public void should_follow_a_link_that_stays_inside_the_folder()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "branding"));
        var real = Path.Combine(_folder, "branding", "real.png");
        File.WriteAllBytes(real, Png);
        try
        {
            File.CreateSymbolicLink(Path.Combine(_folder, "branding", "alias.png"), real);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        PortalLogo.Resolve(_folder, "branding/alias.png").Should().NotBeNull();
    }
}
