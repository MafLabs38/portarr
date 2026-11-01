using Portarr.Core.Catalog;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Catalog;

/// <summary>The code sources of the Maintenance page (<c>sources: github:</c>, D-64) and an app's repository (D-65).</summary>
public sealed class CatalogSourcesParserFixture
{
    [Fact]
    public void should_read_the_github_source_with_its_defaults()
    {
        var result = TestCatalog.Parse("""
            sources:
              github:
                owners: [example, example-org]
                exclude: ["example/test-*"]
            apps:
              - { id: shop, repository: example/shop }
            """);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Catalog.GitHub.Should().BeEquivalentTo(new GitHubSource(["example", "example-org"], "GITHUB_TOKEN", 6, [], ["example/test-*"], false, false));
        result.Catalog.Apps[0].Repository.Should().Be("example/shop");
    }

    [Theory]
    [InlineData("github: { owners: [] }", "list the owners")]
    [InlineData("github: { owners: [\"not a login\"] }", "owner 'not a login'")]
    [InlineData("github: { owners: [example], include: [shop] }", "include entry 'shop' must be owner/name")]
    [InlineData("github: { owners: [example], tokenEnv: \"ghp_abc-def\" }", "never the token itself")]
    [InlineData("github: { owners: [example], pushedWithinMonths: -1 }", "pushedWithinMonths")]
    public void should_reject_an_invalid_github_source(string source, string error)
    {
        var result = TestCatalog.Parse($"sources:\n  {source}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain(error);
    }

    [Fact]
    public void should_reject_a_repository_url()
    {
        TestCatalog.Parse("apps:\n  - { id: shop, repository: \"https://github.com/example/shop\" }\n")
            .Errors.Should().ContainSingle().Which.Should().Contain("repository must be owner/name");
    }

    [Theory]
    [InlineData("example/test-*", "Example/test-app", true)]
    [InlineData("example/*-old", "example/site-old", true)]
    [InlineData("example/shop", "example/shop-2", false)]
    [InlineData("*/sandbox", "other/sandbox", true)]
    public void should_match_exclusions_with_wildcards(string pattern, string repository, bool excluded)
    {
        new GitHubSource([], "T", 6, [], [pattern], false, false).IsExcluded(repository).Should().Be(excluded);
    }
}
