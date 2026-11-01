using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Catalog.Editing;
using Portarr.Core.Configuration;
using Portarr.Core.Portal;

namespace Portarr.Core.Test.Portal;

public sealed class PortalServiceFixture : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly CatalogActor Actor = new("api", "10.0.0.5");

    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-portal-").FullName;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 31, 10, 0, 0, TimeSpan.Zero));
    private readonly PortarrOptions _options;
    private readonly CatalogService _catalogService;
    private readonly CatalogEditor _editor;
    private readonly PortalService _subject;

    public PortalServiceFixture()
    {
        _options = new PortarrOptions { DataFolder = _folder, Catalog = { Writable = true } };
        var options = Options.Create(_options);
        _catalogService = new CatalogService(options, new CatalogParserService([]), _clock, NullLogger<CatalogService>.Instance);
        var audit = new CatalogAuditLog(options);
        _editor = new CatalogEditor(options, _catalogService, new CatalogParserService([]), audit, _clock, NullLogger<CatalogEditor>.Instance);
        _subject = new PortalService(options, _catalogService, _editor, audit, _clock, NullLogger<PortalService>.Instance);
        File.WriteAllText(CatalogFile, "# Mine.\napps:\n  - { id: blog, name: Blog }\n");
    }

    private string CatalogFile => Path.Combine(_folder, "catalog.yaml");

    private CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _editor.Dispose();
        _catalogService.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task should_use_portarrs_own_identity_when_the_catalog_says_nothing()
    {
        await _catalogService.ReloadAsync(Token);

        _subject.Get().Should().Be(new PortalIdentity("Portarr", null, null, null, PortalColors.Default));
    }

    [Fact]
    public async Task should_store_the_logo_set_it_in_the_catalog_and_audit_it()
    {
        var result = await _subject.SaveLogoAsync("My Logo.PNG", Png, Actor, Token);

        result.Edit.Status.Should().Be(CatalogEditStatus.Applied);
        result.Path.Should().Be("branding/my-logo.png");
        File.ReadAllBytes(Path.Combine(_folder, "branding", "my-logo.png")).Should().Equal(Png);
        File.ReadAllText(CatalogFile).Should().StartWith("# Mine.\n").And.Contain("portal:\n  logo: branding/my-logo.png");
        var identity = _subject.Get();
        identity.Logo.Should().Be("branding/my-logo.png");
        identity.LogoFile!.ContentType.Should().Be("image/png");
        identity.LogoFile.Hash.Should().HaveLength(12);
        _subject.ListBranding().Should().Equal("branding/my-logo.png");

        var audit = await new CatalogAuditLog(Options.Create(_options)).ReadAsync(10, Token);
        audit.Select(e => e.Operation).Should().Equal("upload-logo", "set-logo");
    }

    [Fact]
    public async Task should_keep_the_previous_file_and_change_the_hash_when_a_logo_is_replaced()
    {
        await _subject.SaveLogoAsync("logo.png", Png, Actor, Token);
        var before = _subject.Get().LogoFile!.Hash;
        _clock.Advance(TimeSpan.FromSeconds(1));

        byte[] other = [.. Png, 9, 9];
        await _subject.SaveLogoAsync("logo.png", other, Actor, Token);

        Directory.GetFiles(Path.Combine(_folder, "branding"), "logo.png.bak-*").Should().ContainSingle();
        _subject.Get().LogoFile!.Hash.Should().NotBe(before);
        _subject.ListBranding().Should().Equal("branding/logo.png");
    }

    [Fact]
    public async Task should_refuse_a_file_that_is_not_the_image_it_claims_to_be_without_writing_anything()
    {
        var result = await _subject.SaveLogoAsync("logo.png", "<html>"u8.ToArray(), Actor, Token);

        result.Edit.Status.Should().Be(CatalogEditStatus.Invalid);
        result.Path.Should().BeNull();
        Directory.Exists(Path.Combine(_folder, "branding")).Should().BeFalse();
        File.ReadAllText(CatalogFile).Should().NotContain("portal");
    }

    [Fact]
    public async Task should_refuse_an_upload_when_the_catalog_is_read_only()
    {
        _options.Catalog.Writable = false;

        var result = await _subject.SaveLogoAsync("logo.png", Png, Actor, Token);

        result.Edit.Status.Should().Be(CatalogEditStatus.ReadOnly);
        Directory.Exists(Path.Combine(_folder, "branding")).Should().BeFalse();
    }

    [Fact]
    public async Task should_fall_back_and_say_why_when_the_declared_logo_is_not_an_image()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "branding"));
        File.WriteAllText(Path.Combine(_folder, "branding", "fake.png"), "not a png");
        File.WriteAllText(CatalogFile, "portal: { name: Homelab, logo: branding/fake.png }\n");
        await _catalogService.ReloadAsync(Token);

        var identity = _subject.Get();

        identity.Name.Should().Be("Homelab");
        identity.LogoFile.Should().BeNull();
        identity.LogoProblem.Should().Contain("not a valid png");
    }

    [Fact]
    public async Task should_change_the_name_in_place_and_remove_the_section_when_nothing_is_left()
    {
        File.WriteAllText(CatalogFile, "# Mine.\nportal:\n  name: Homelab   # the name\n  logo: branding/logo.png\napps: []\n");
        await _catalogService.ReloadAsync(Token);

        var renamed = await _editor.EditAsync(Edit("set-name", CatalogEdits.SetPortal("Home", null)), Token);

        renamed.Changes.Should().Equal("name: changed");
        File.ReadAllText(CatalogFile).Should().Contain("# Mine.").And.Contain("name: Home   # the name").And.Contain("logo: branding/logo.png");

        await _editor.EditAsync(Edit("clear", CatalogEdits.SetPortal(string.Empty, string.Empty)), Token);

        File.ReadAllText(CatalogFile).Should().NotContain("portal").And.Contain("# Mine.").And.Contain("apps: []");
    }

    [Fact]
    public async Task should_refuse_a_name_or_a_logo_the_catalog_would_not_accept()
    {
        var tooLong = await _editor.EditAsync(Edit("set-name", CatalogEdits.SetPortal(new string('x', 41), null)), Token);
        var outside = await _editor.EditAsync(Edit("set-logo", CatalogEdits.SetPortal(null, "../secret.png")), Token);

        tooLong.Status.Should().Be(CatalogEditStatus.Invalid);
        tooLong.Errors.Should().ContainSingle().Which.Should().StartWith("portal: name");
        outside.Status.Should().Be(CatalogEditStatus.Invalid);
        outside.Errors.Should().ContainSingle().Which.Should().StartWith("portal: logo");
        File.ReadAllText(CatalogFile).Should().NotContain("portal");
    }

    [Fact]
    public async Task should_read_the_colors_of_the_badge_with_the_accent_as_default()
    {
        File.WriteAllText(CatalogFile, "portal: { name: Homelab, colors: { version: \"#abc\" } }\n");
        await _catalogService.ReloadAsync(Token);

        _subject.Get().Colors.Should().Be(new PortalColors("accent", "#abc"));
    }

    [Fact]
    public async Task should_set_and_clear_the_colors_in_place_quoting_a_hex_color()
    {
        File.WriteAllText(CatalogFile, "# Mine.\nportal:\n  name: Homelab   # the name\napps: []\n");
        await _catalogService.ReloadAsync(Token);

        var set = await _editor.EditAsync(Edit("set-colors", CatalogEdits.SetPortal(null, null, "#ffc145", "inkMuted")), Token);
        await _catalogService.ReloadAsync(Token);

        set.Status.Should().Be(CatalogEditStatus.Applied);
        File.ReadAllText(CatalogFile).Should().Contain("name: Homelab   # the name").And.Contain("# Mine.");
        _subject.Get().Colors.Should().Be(new PortalColors("#ffc145", "inkMuted"));

        await _editor.EditAsync(Edit("clear-colors", CatalogEdits.SetPortal(null, null, string.Empty, string.Empty)), Token);
        await _catalogService.ReloadAsync(Token);

        _subject.Get().Colors.Should().Be(PortalColors.Default);
        File.ReadAllText(CatalogFile).Should().NotContain("colors");
    }

    [Theory]
    [InlineData("red; background:url(x)")]
    [InlineData("#12")]
    [InlineData("rgb(0,0,0)")]
    [InlineData("url(http://x)")]
    [InlineData("primary")]
    public async Task should_refuse_a_color_that_is_not_a_hex_color_or_a_known_token(string color)
    {
        var refused = await _editor.EditAsync(Edit("set-colors", CatalogEdits.SetPortal(null, null, color, null)), Token);

        refused.Status.Should().Be(CatalogEditStatus.Invalid);
        refused.Errors.Should().ContainSingle().Which.Should().StartWith("portal: colors.name must be");
        File.ReadAllText(CatalogFile).Should().NotContain("portal");
    }

    private CatalogEdit Edit(string operation, Func<System.Text.Json.Nodes.JsonObject?, System.Text.Json.Nodes.JsonObject?> change)
    {
        return new CatalogEdit(CatalogSections.Portal, CatalogSections.Portal, operation, _editor.GetVersionAsync(Token).GetAwaiter().GetResult(), Actor, change);
    }
}
