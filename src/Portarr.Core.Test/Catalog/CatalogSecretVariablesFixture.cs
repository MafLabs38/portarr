using Portarr.Core.Catalog;

namespace Portarr.Core.Test.Catalog;

/// <summary>
/// The secret variables a catalog may name (issue #45): a connector's own namespace, its default name, or a name the operator allows
/// outside the catalog, and never an unrelated secret of the process.
/// </summary>
public sealed class CatalogSecretVariablesFixture
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("PORTARR_K8S_TOKEN")]
    [InlineData("PORTARR_K8S_CLUSTER_2")]
    [InlineData("K8S_TOKEN")]
    public void should_accept_the_namespace_the_default_and_an_absent_name(string? value)
    {
        CatalogValues.SecretVariableError("kubernetes.tokenEnv", value, "PORTARR_K8S_", "K8S_TOKEN", "token", None, out _).Should().BeNull();
    }

    [Theory]
    [InlineData("GITHUB_TOKEN")]
    [InlineData("PORTARR_K8S_")]
    [InlineData("portarr_k8s_token")]
    [InlineData("CLOUDFLARE_API_TOKEN")]
    public void should_refuse_a_variable_outside_the_namespace_naming_the_way_out(string value)
    {
        var error = CatalogValues.SecretVariableError("kubernetes.tokenEnv", value, "PORTARR_K8S_", "K8S_TOKEN", "token", None, out var variable);

        error.Should().Contain("PORTARR_K8S_").And.Contain("PORTARR__CATALOG__SECRETVARIABLES");
        variable.Should().Be(value);
    }

    [Fact]
    public void should_accept_a_variable_the_operator_allows_and_only_that_one()
    {
        var allowed = CatalogValues.ParseVariableList("OLD_TOKEN, ANOTHER;THIRD name! 1BAD");

        allowed.Should().BeEquivalentTo(["OLD_TOKEN", "ANOTHER", "THIRD"]);
        CatalogValues.SecretVariableError("kubernetes.tokenEnv", "OLD_TOKEN", "PORTARR_K8S_", "K8S_TOKEN", "token", allowed, out _).Should().BeNull();
        CatalogValues.SecretVariableError("kubernetes.tokenEnv", "GITHUB_TOKEN", "PORTARR_K8S_", "K8S_TOKEN", "token", allowed, out _).Should().NotBeNull();
    }

    [Fact]
    public void should_refuse_a_value_that_is_not_a_variable_name_as_the_secret_itself()
    {
        CatalogValues.SecretVariableError("kubernetes.tokenEnv", "eyJhbGciOi.token", "PORTARR_K8S_", "K8S_TOKEN", "token", None, out _)
            .Should().Contain("never the token itself");
    }

    [Fact]
    public void should_confine_the_github_token_of_a_code_source_and_let_the_operator_allow_a_name()
    {
        const string Yaml = "sources:\n  github: { owners: [example], tokenEnv: MY_GH_TOKEN }\n";

        CatalogParser.Parse(Yaml, []).Errors.Should().ContainSingle().Which.Should().Contain("PORTARR_GITHUB_");
        CatalogParser.Parse(Yaml, [], CatalogValues.ParseVariableList("MY_GH_TOKEN")).Errors.Should().BeEmpty();
        CatalogParser.Parse("sources:\n  github: { owners: [example], tokenEnv: GITHUB_TOKEN }\n", []).Errors.Should().BeEmpty("the default name stays valid");
    }
}
