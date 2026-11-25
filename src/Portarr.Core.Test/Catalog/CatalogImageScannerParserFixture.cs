using Portarr.Core.Catalog;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Catalog;

/// <summary>The image scanner companion in the catalog (<c>imageScanner:</c>, D-131): an address and the name of a secret variable, never the secret.</summary>
public sealed class CatalogImageScannerParserFixture
{
    [Fact]
    public void should_read_the_scanner_with_its_default_secret_variable_and_a_trailing_slash()
    {
        var result = TestCatalog.Parse("imageScanner: { url: \"https://scanner.homelab.example/trivy\" }\n");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Catalog.ImageScanner.Should().Be(new ImageScannerSource(new Uri("https://scanner.homelab.example/trivy/"), "PORTARR_SCANNER_SECRET"));
    }

    [Fact]
    public void should_accept_plain_http_only_when_the_operator_says_so_and_a_variable_of_its_namespace()
    {
        var result = TestCatalog.Parse("imageScanner: { url: \"http://scanner:8489\", allowInsecure: true, secretEnv: PORTARR_SCANNER_STAGING }\n");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Catalog.ImageScanner.Should().Be(new ImageScannerSource(new Uri("http://scanner:8489/"), "PORTARR_SCANNER_STAGING", true));
    }

    [Fact]
    public void should_refuse_plain_http_unless_the_operator_accepts_the_secret_in_clear()
    {
        var result = TestCatalog.Parse("imageScanner: { url: \"http://scanner:8489\" }\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("allowInsecure");
        result.Catalog.ImageScanner.Should().BeNull();
    }

    [Theory]
    [InlineData("imageScanner: { }")]
    [InlineData("imageScanner: { url: \"scanner\" }")]
    [InlineData("imageScanner: { url: \"ftp://scanner\" }")]
    [InlineData("imageScanner: { url: \"https://user:pass@scanner\" }")]
    [InlineData("imageScanner: { url: \"https://scanner/?token=x\" }")]
    [InlineData("imageScanner: { url: \"https://scanner/#frag\" }")]
    public void should_refuse_an_address_that_is_not_the_scanners(string yaml)
    {
        var result = TestCatalog.Parse(yaml + "\n");

        result.Errors.Should().ContainSingle().Which.Should().StartWith("imageScanner: url must be");
    }

    [Fact]
    public void should_refuse_a_secret_variable_outside_the_scanners_namespace_so_a_catalog_cannot_send_another_secret_there()
    {
        var result = TestCatalog.Parse("imageScanner: { url: \"https://scanner.homelab.example\", secretEnv: GITHUB_TOKEN }\n");

        result.Errors.Should().ContainSingle().Which.Should().StartWith("imageScanner: secretEnv must").And.Contain("PORTARR_SCANNER_");
    }

    [Fact]
    public void should_leave_the_scanner_out_of_a_catalog_that_does_not_declare_one()
    {
        TestCatalog.Parse("environments: []\n").Catalog.ImageScanner.Should().BeNull();
    }
}
