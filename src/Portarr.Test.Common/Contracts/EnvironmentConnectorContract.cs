using AwesomeAssertions;
using Portarr.Core.Catalog;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>
/// The contract of an <see cref="IEnvironmentConnector"/> (ADR 0013): the catalog section it owns is read strictly, its errors
/// name the environment, and a catalog cannot make it accept a credential in a URL or a secret in place of a variable name.
/// A connector's test project inherits it, supplies a valid section and, where its section has them, the invalid ones.
/// </summary>
public abstract class EnvironmentConnectorContract
{
    protected abstract IEnvironmentConnector Connector { get; }

    /// <summary>A valid value of the section, as a YAML flow mapping: <c>{ url: "http://10.0.0.10:9898" }</c>.</summary>
    protected abstract string ValidSection { get; }

    /// <summary>The section with credentials in its URL, or null when the section has no URL.</summary>
    protected virtual string? SectionWithCredentialsInUrl => null;

    /// <summary>The section with a literal secret where a variable name is expected, or null when it has no such key.</summary>
    protected virtual string? SectionWithLiteralSecret => null;

    /// <summary>The section with a secret variable outside the <c>PORTARR_READER_</c> namespace, or null when it names none.</summary>
    protected virtual string? SectionWithSecretOutsideNamespace => null;

    /// <summary>
    /// True for a connector that does not confine its secret variables yet (issue #45): the section above is then accepted, and
    /// the test says so, so that the day it is confined this flag goes away with the test that pins the gap.
    /// </summary>
    protected virtual bool SecretVariablesAreUnconfined => false;

    [Fact]
    public void should_accept_its_valid_section()
    {
        var result = Parse(ValidSection);

        result.Errors.Should().BeEmpty();
        result.Catalog.Environments.Single().Declares(Connector.Section).Should().BeTrue();
    }

    [Fact]
    public void should_refuse_an_unknown_key_naming_its_line()
    {
        var result = Parse(ValidSection.TrimEnd().TrimEnd('}').TrimEnd() + ", unexpectedKey: 1 }");

        result.Errors.Should().ContainSingle().Which.Should().StartWith("Invalid YAML at line 3").And.Contain("unexpectedKey");
    }

    [Fact]
    public void should_not_fail_on_an_empty_section()
    {
        var read = () => CatalogParser.Parse($"environments:\n  - id: server-1\n    {Connector.Section}:\n", [Connector]);

        read.Should().NotThrow();
    }

    [Fact]
    public void should_refuse_credentials_in_a_url_and_name_the_environment()
    {
        if (SectionWithCredentialsInUrl is null)
        {
            return; // this section has no URL
        }

        var result = Parse(SectionWithCredentialsInUrl);

        result.Errors.Should().ContainSingle().Which.Should().Contain("Environment 'server-1'");
    }

    [Fact]
    public void should_refuse_a_literal_secret_in_place_of_a_variable_name()
    {
        if (SectionWithLiteralSecret is null)
        {
            return; // this section names no variable
        }

        var result = Parse(SectionWithLiteralSecret);

        result.Errors.Should().ContainSingle().Which.Should().Contain("Environment 'server-1'");
    }

    [Fact]
    public void should_confine_its_secret_variables_to_the_portarr_namespace_or_say_it_does_not()
    {
        if (SectionWithSecretOutsideNamespace is null)
        {
            return; // this section names no secret variable
        }

        var result = Parse(SectionWithSecretOutsideNamespace);

        if (SecretVariablesAreUnconfined)
        {
            result.Errors.Should().BeEmpty("a known gap, issue #45: when the section is confined, remove SecretVariablesAreUnconfined");
        }
        else
        {
            result.Errors.Should().NotBeEmpty("a secret variable outside the namespace must be refused");
        }
    }

    private CatalogParseResult Parse(string section)
    {
        return CatalogParser.Parse($"environments:\n  - id: server-1\n    {Connector.Section}: {section}\n", [Connector]);
    }
}
