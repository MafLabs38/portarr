using AwesomeAssertions;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>
/// The contract of an <see cref="IMcpSecretStoreProvisioner"/> (rule 8, D-116): a kind the catalog can name, a verdict on a prefix and on a key that the
/// catalog parser relays, and a provisioning command that names the variables and the keys and nothing else. The test project of an adapter
/// inherits it and supplies the provisioner and a few valid and invalid names.
/// </summary>
public abstract class McpSecretStoreProvisionerContract
{
    protected abstract IMcpSecretStoreProvisioner Provisioner { get; }

    protected abstract string ValidPrefix { get; }

    protected abstract string InvalidPrefix { get; }

    protected abstract string ValidKey { get; }

    protected abstract string InvalidKey { get; }

    private McpSecretStore Store => new(Provisioner.Kind, new Uri("https://store.homelab.example"), ValidPrefix);

    public static TheoryData<string> Systems() => new(McpOperatingSystems.All);

    [Fact]
    public void should_have_a_kind_the_catalog_can_name()
    {
        Provisioner.Kind.Should().MatchRegex("^[a-z][a-z0-9-]*$");
    }

    [Fact]
    public void should_accept_a_valid_prefix_and_key_and_say_why_it_refuses_the_others()
    {
        Provisioner.CheckPrefix(ValidPrefix).Should().BeNull();
        Provisioner.CheckKey(ValidKey).Should().BeNull();
        Provisioner.CheckPrefix(InvalidPrefix).Should().NotBeNullOrWhiteSpace();
        Provisioner.CheckKey(InvalidKey).Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void should_provision_the_variables_from_the_keys_and_name_what_to_grant(string os)
    {
        var secrets = new List<McpSecretRef> { new("MEMORY_MCP_KEY", ValidKey) };

        var operation = Provisioner.Provision(Store, secrets, os);

        operation.Kind.Should().Be(McpOperationKinds.Provision);
        operation.Text.Should().Contain("MEMORY_MCP_KEY");
        Provisioner.Identifier(Store, ValidKey).Should().Contain(ValidKey);
        Provisioner.Notes(Store, os).Should().NotBeEmpty();
    }
}
