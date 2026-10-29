using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;

namespace Portarr.Core.Test.Catalog;

public sealed class CatalogServiceFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-catalog-").FullName;
    private readonly CatalogService _subject;

    public CatalogServiceFixture()
    {
        var options = Options.Create(new PortarrOptions { DataFolder = _folder });
        _subject = new CatalogService(options, new FakeTimeProvider(), NullLogger<CatalogService>.Instance);
    }

    private string File => Path.Combine(_folder, "catalog.yaml");

    public void Dispose()
    {
        _subject.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task should_keep_the_previous_apps_and_report_the_error_when_an_edit_breaks_the_catalog()
    {
        await System.IO.File.WriteAllTextAsync(File, "apps:\n  - { id: sonarr, name: Sonarr }", TestContext.Current.CancellationToken);
        await _subject.ReloadAsync(TestContext.Current.CancellationToken);

        await System.IO.File.WriteAllTextAsync(File, "apps:\n  - { id: sonarr, stack: nope }", TestContext.Current.CancellationToken);
        await _subject.ReloadAsync(TestContext.Current.CancellationToken);

        _subject.Current.Catalog.Apps.Should().ContainSingle().Which.Name.Should().Be("Sonarr");
        _subject.Current.Errors.Should().ContainSingle().Which.Should().Contain("stack 'nope' is not declared");
    }

    [Fact]
    public async Task should_start_empty_without_a_catalog_file()
    {
        await _subject.ReloadAsync(TestContext.Current.CancellationToken);

        _subject.Current.Should().Match<CatalogState>(s => !s.FileExists && s.Catalog.Apps.Count == 0 && s.Errors.Count == 0);
    }
}
