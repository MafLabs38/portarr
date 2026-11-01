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
        result.Catalog.CodeSources.Should().ContainSingle().Which.Should().BeEquivalentTo(new GitHubSource(["example", "example-org"], "GITHUB_TOKEN", 6, [], ["example/test-*"], false, false));
        result.Catalog.Apps[0].Repository.Should().Be("example/shop");
    }

    [Fact]
    public void should_read_a_custom_token_variable_includes_and_an_unlimited_activity_window()
    {
        var result = TestCatalog.Parse("""
            sources:
              github:
                owners: [example]
                tokenEnv: MY_GH_TOKEN
                include: [other/lib, Other/Lib]
                pushedWithinMonths: 0
                forks: true
                archived: true
            """);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Catalog.CodeSources.Should().ContainSingle().Which.Should().BeEquivalentTo(new GitHubSource(["example"], "MY_GH_TOKEN", 0, ["other/lib"], [], true, true), "an include listed twice, in any case, is kept once");
    }

    [Fact]
    public void should_read_a_repository_only_source_without_an_owner()
    {
        var result = TestCatalog.Parse("sources:\n  github: { include: [other/lib] }\n");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Catalog.CodeSources.Single().Logins.Should().BeEmpty();
    }

    [Fact]
    public void should_keep_an_owner_listed_twice_once()
    {
        TestCatalog.Parse("sources:\n  github: { owners: [example, Example] }\n").Catalog.CodeSources.Single().Logins.Should().Equal("example");
    }

    [Theory]
    [InlineData("example/a.b", "example/a.b", true)]
    [InlineData("example/a.b", "example/aXb", false)]
    [InlineData("example/a+b*", "example/a+bc", true)]
    [InlineData("example/(x)", "example/(x)", true)]
    public void should_match_a_pattern_literally_apart_from_its_wildcard(string pattern, string repository, bool excluded)
    {
        new GitHubSource([], "T", 6, [], [pattern], false, false).IsExcluded(repository).Should().Be(excluded);
    }

    [Fact]
    public void should_read_several_accounts_each_with_its_own_token()
    {
        var result = TestCatalog.Parse("""
            sources:
              github:
                - { owners: [example], tokenEnv: GITHUB_TOKEN_EXAMPLE }
                - { owners: [example-org], tokenEnv: GITHUB_TOKEN_ORG, exclude: ["example-org/test-*"] }
            """);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Catalog.CodeSources.Cast<GitHubSource>().Select(s => (s.Owners.Single(), s.TokenVariable)).Should().Equal(("example", "GITHUB_TOKEN_EXAMPLE"), ("example-org", "GITHUB_TOKEN_ORG"));
    }

    [Fact]
    public void should_name_the_entry_of_a_list_in_its_errors_and_refuse_an_owner_given_to_two_accounts()
    {
        var result = TestCatalog.Parse("sources:\n  github:\n    - { owners: [example] }\n    - { owners: [\"not a login\", example] }\n");

        result.Errors.Should().Contain(e => e.StartsWith("sources.github[1]: owner 'not a login'", StringComparison.Ordinal));
        result.Errors.Should().Contain(e => e.Contains("owner 'example' is listed in more than one account", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://github.example/api/v3", "https://github.example/api/v3/")]
    [InlineData("https://github.example/api/v3/", "https://github.example/api/v3/")]
    public void should_read_the_api_address_of_a_github_enterprise_server(string apiUrl, string expected)
    {
        var result = TestCatalog.Parse($"sources:\n  github: {{ owners: [example], apiUrl: \"{apiUrl}\" }}\n");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        ((GitHubSource)result.Catalog.CodeSources.Single()).ApiUrl.Should().Be(new Uri(expected));
    }

    [Theory]
    [InlineData("http://github.example/api/v3/")]
    [InlineData("https://user:pass@github.example/api/v3/")]
    [InlineData("https://github.example/api/v3/?x=1")]
    [InlineData("github.example")]
    public void should_refuse_an_api_address_that_is_not_plain_https(string apiUrl)
    {
        var result = TestCatalog.Parse($"sources:\n  github: {{ owners: [example], apiUrl: \"{apiUrl}\" }}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("apiUrl must be the https address");
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
