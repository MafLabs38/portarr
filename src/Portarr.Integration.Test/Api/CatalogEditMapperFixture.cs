using Portarr.Api.V1.Catalog;
using Portarr.Core.Catalog.Editing;

namespace Portarr.Integration.Test.Api;

/// <summary>How the parser's messages become field paths in a validation problem (pure, no host).</summary>
public sealed class CatalogEditMapperFixture
{
    [Theory]
    [InlineData("portal: logo must be a png, webp or svg file.", "portal.logo")]
    [InlineData("portal: colors.name must be a hex color.", "portal.colors.name")]
    [InlineData("portal: name must be at most 40 characters.", "portal.name")]
    [InlineData("portal: something is wrong", "portal.something")]
    [InlineData("portal: Not a field name", "portal")]
    [InlineData("the portal is odd", "portal")]
    [InlineData("", "portal")]
    public void should_name_the_field_of_a_portal_error(string error, string expected)
    {
        CatalogEditMapper.PathOf(error, CatalogSections.Portal, CatalogSections.Portal).Should().Be(expected);
    }

    [Theory]
    [InlineData("App 'blog': check.url must be an http(s) URL.", "apps", "blog", "apps[blog]")]
    [InlineData("Environment 'cluster': machine 'node-2': access.ssh.key must be a file name.", "environments", "cluster", "environments[cluster].machines[node-2]")]
    [InlineData("id: the item says 'x' but the request targets 'y'.", "apps", "y", "item.id")]
    [InlineData("something unexpected", "stacks", "media", "stacks[media]")]
    public void should_name_the_item_an_error_is_about(string error, string section, string id, string startsWith)
    {
        CatalogEditMapper.PathOf(error, section, id).Should().StartWith(startsWith);
    }

    [Fact]
    public void should_group_the_errors_of_a_result_by_path()
    {
        var result = new CatalogEditResult(CatalogEditStatus.Invalid, "v1", [], ["portal: logo must be a png.", "portal: logo cannot be empty.", "portal: name must be shorter."]);

        var errors = result.ToErrors(CatalogSections.Portal, CatalogSections.Portal);

        errors.Keys.Should().BeEquivalentTo("portal.logo", "portal.name");
        errors["portal.logo"].Should().HaveCount(2);
    }
}
