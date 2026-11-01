using Portarr.Core.Catalog;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Backrest.Test;

/// <summary>The <c>backrest:</c> section of an environment (D-80, D-105): what the connector accepts and refuses.</summary>
public sealed class BackrestEnvironmentConnectorFixture : EnvironmentConnectorContract
{
    protected override IEnvironmentConnector Connector { get; } = new BackrestEnvironmentConnector();

    protected override string ValidSection => "{ url: \"http://10.0.0.10:9898\", userEnv: BACKREST_USER, passwordEnv: BACKREST_PASSWORD }";

    protected override string? SectionWithCredentialsInUrl => "{ url: \"http://user:pass@10.0.0.10:9898\" }";

    protected override string? SectionWithLiteralSecret => "{ url: \"http://10.0.0.10:9898\", userEnv: \"admin\", passwordEnv: \"pa ss\" }";

    private static CatalogParseResult Parse(string yaml) => CatalogParser.Parse(yaml, [new BackrestEnvironmentConnector()]);

    [Fact]
    public void should_read_the_url_and_the_variables_that_hold_the_credentials()
    {
        var parsed = Parse("""
            environments:
              - { id: server-1, docker: { url: "http://10.0.0.10:2375" }, backrest: { url: "http://10.0.0.10:9898", userEnv: BACKREST_USER, passwordEnv: BACKREST_PASSWORD } }
            """);

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments[0].Backrest().Should().Be(new BackrestEndpoint(new Uri("http://10.0.0.10:9898"), "BACKREST_USER", "BACKREST_PASSWORD"));
    }

    [Fact]
    public void should_accept_a_backrest_without_authentication()
    {
        var parsed = Parse("environments:\n  - { id: server-1, backrest: { url: \"http://10.0.0.10:9898\" } }\n");

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments[0].Backrest().Should().Be(new BackrestEndpoint(new Uri("http://10.0.0.10:9898"), null, null));
    }

    [Fact]
    public void should_leave_an_environment_that_declares_no_backrest_without_one()
    {
        var parsed = Parse("environments:\n  - { id: server-1, docker: { url: \"http://10.0.0.10:2375\" } }\n");

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments[0].Backrest().Should().BeNull();
        parsed.Catalog.Environments[0].Declares("backrest").Should().BeFalse();
    }

    [Theory]
    [InlineData("backrest: { url: \"http://10.0.0.10:9898\", userEnv: BACKREST_USER }", "must both name an environment variable")]
    [InlineData("backrest: { url: \"http://user:pass@10.0.0.10:9898\" }", "without credentials")]
    [InlineData("backrest: { url: \"http://10.0.0.10:9898\", userEnv: \"admin\", passwordEnv: \"pa ss\" }", "must both name an environment variable")]
    public void should_reject_a_backrest_with_credentials_in_the_catalog(string backrest, string error)
    {
        var result = Parse($"environments:\n  - {{ id: server-1, docker: {{ url: \"http://10.0.0.10:2375\" }}, {backrest} }}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain(error);
    }

    [Fact]
    public void should_reject_a_backrest_on_a_cluster()
    {
        var result = Parse("environments:\n  - { id: cluster, kubernetes: { server: \"https://10.0.0.20:6443\" }, backrest: { url: \"http://10.0.0.10:9898\" } }\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("not on a kubernetes or cloudflare environment");
    }

    [Fact]
    public void should_refuse_the_section_when_no_connector_owns_it()
    {
        var result = CatalogParser.Parse("environments:\n  - { id: server-1, backrest: { url: \"http://10.0.0.10:9898\" } }\n");

        result.IsValid.Should().BeFalse("an unknown key is an error, a section nobody registered is one");
    }
}
