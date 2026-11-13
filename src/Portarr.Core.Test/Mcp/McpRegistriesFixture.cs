using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;
using Portarr.Test.Common.Mcp;

namespace Portarr.Core.Test.Mcp;

/// <summary>The registries and the vocabulary: what registered is what the catalog may name, in the order of registration, with no list of the Core's own.</summary>
public sealed class McpRegistriesFixture
{
    [Fact]
    public void should_keep_the_clients_in_the_order_they_registered()
    {
        var registry = new McpClientRegistry([new StandInMcpClient("zed"), new StandInMcpClient("alpha"), new StandInMcpClient("mid")]);

        registry.Ids.Should().Equal("zed", "alpha", "mid");
        registry.All.Select(c => c.Id).Should().Equal(registry.Ids);
    }

    [Fact]
    public void should_find_a_client_by_its_exact_id_and_nothing_else()
    {
        var registry = new McpClientRegistry([new StandInMcpClient("cursor")]);

        registry.Find("cursor").Should().NotBeNull();
        registry.Find("Cursor").Should().BeNull();
        registry.Find("nope").Should().BeNull();
        registry.Find(null).Should().BeNull();
    }

    [Fact]
    public void should_refuse_two_clients_with_the_same_id_at_startup()
    {
        var act = () => new McpClientRegistry([new StandInMcpClient("cursor"), new StandInMcpClient("cursor")]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'cursor'*");
    }

    [Fact]
    public void should_refuse_two_stores_of_the_same_kind_at_startup()
    {
        var act = () => new McpSecretStoreRegistry([new StandInSecretStore("vault"), new StandInSecretStore("vault")]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'vault'*");
    }

    [Fact]
    public void should_know_nothing_when_nothing_registered()
    {
        McpVocabulary.None.Clients.Should().BeEmpty();
        McpVocabulary.None.SecretStoreKinds.Should().BeEmpty();
        McpVocabulary.None.CheckSecretPrefix("vault", "any").Should().BeNull();
        TestCatalogWithNothingRegistered("workstations:\n  - { id: pc, os: linux, clients: [cursor] }").Errors.Should().Contain(e => e.Contains("unknown client 'cursor'", StringComparison.Ordinal));
    }

    [Fact]
    public void should_relay_the_verdict_of_the_store_that_owns_the_kind_and_say_nothing_of_an_unknown_kind()
    {
        var vocabulary = TestMcp.Vocabulary;

        vocabulary.CheckSecretPrefix("vault", "kv mcp").Should().Be("must not contain spaces.");
        vocabulary.CheckSecretPrefix("vault", "kv/mcp").Should().BeNull();
        vocabulary.CheckSecretKey("azure-key-vault", "a/b").Should().NotBeNull();
        vocabulary.CheckSecretKey("keychain", "a/b").Should().BeNull();
    }

    private static CatalogParseResult TestCatalogWithNothingRegistered(string yaml) => CatalogParser.Parse(yaml, [], null, McpVocabulary.None);
}
