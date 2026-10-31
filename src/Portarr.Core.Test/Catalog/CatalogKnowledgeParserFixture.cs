using Portarr.Core.Catalog;

namespace Portarr.Core.Test.Catalog;

public sealed class CatalogKnowledgeParserFixture
{
    private const string Knowledge = """
        environments:
          - id: server-1
            name: Server 1
            notes: The hypervisor. Reboots need the array to be stopped first.
            access:
              ssh: { alias: server-1, host: 10.0.0.10, port: 2222, user: root, key: ~/.ssh/server-1 }
              consoles: [{ label: Web UI, href: "https://server-1.homelab.example" }]
          - id: cluster
            name: Cluster
            runtime: kubernetes
            kubernetes: { server: "https://10.0.0.20:6443", nodeHosts: { node-1: server-1 } }
            machines:
              - name: node-2
                notes: A VPS at a hosting provider.
                access: { ssh: { host: vps.homelab.example, user: ubuntu, key: id_ed25519, jump: server-1 } }
        apps:
          - { id: blog, name: Blog, notes: "Static site." }
        stacks:
          - { id: web, name: Web, members: [{ app: blog }], notes: Public sites. }
        links:
          - { id: tunnel, from: node-1, to: node-2, kind: wireguard-tunnel, network: 10.99.0.0/24, notes: node-1 initiates. }
          - { id: lan, from: server-1, to: cluster/node-1, kind: lan }
        providers:
          - { id: cdn, name: Example CDN, kind: cdn, console: "https://dash.cdn.example", account: ops@homelab.example, usedBy: [blog, cluster], notes: Pages and DNS. }
        """;

    private static IReadOnlyList<string> Errors(string yaml) => CatalogParser.Parse(yaml).Errors;

    [Fact]
    public void should_read_notes_access_machines_links_and_providers()
    {
        var result = CatalogParser.Parse(Knowledge);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        var server = result.Catalog.Environments[0];
        server.Notes.Should().StartWith("The hypervisor.");
        server.Access!.Ssh.Should().Be(new SshAccess("10.0.0.10", 2222, "root", "~/.ssh/server-1", null, "server-1"));
        server.Access.Consoles.Should().ContainSingle().Which.Label.Should().Be("Web UI");
        result.Catalog.Environments[1].Machines.Should().ContainSingle().Which.Access!.Ssh!.Jump.Should().Be("server-1");
        result.Catalog.Apps.Single().Notes.Should().Be("Static site.");
        result.Catalog.Stacks.Single().Notes.Should().Be("Public sites.");
        result.Catalog.NetworkLinks.Should().HaveCount(2);
        result.Catalog.NetworkLinks[0].Should().Be(new CatalogLink("tunnel", "node-1", "node-2", "wireguard-tunnel", null, "10.99.0.0/24", "node-1 initiates."));
        result.Catalog.ExternalProviders.Single().UsedBy.Should().Equal("blog", "cluster");
    }

    [Theory]
    // The PEM markers are split so that the repository itself holds no private-key pattern for secret scanners.
    [InlineData("-----BEGIN OPENSSH PRIVATE " + "KEY-----\nb3BlbnNzaC1rZXktdjEAAAAA\n-----END OPENSSH PRIVATE " + "KEY-----")]
    [InlineData("key: ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIExample")]
    [InlineData("token ghp_" + "abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("root password: hunter2")]
    [InlineData("see https://admin:hunter2@router.homelab.example")]
    [InlineData("api key 9fQ2xT7mB4kR8vN1pL6sW3eY5uI0oA2dZ7cX9bV4")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void should_refuse_notes_that_look_like_a_secret_without_echoing_them(string notes)
    {
        var errors = Errors($"apps:\n  - id: blog\n    notes: \"{notes.Replace("\n", "\\n", StringComparison.Ordinal)}\"");

        errors.Should().ContainSingle().Which.Should().Contain("App 'blog': notes looks like it contains a secret").And.NotContain("hunter2");
    }

    [Theory]
    [InlineData("Keys are in ~/.ssh, the token in the variable K8S_TOKEN.")]
    [InlineData("Commit 4f2a9c1 fixed the tunnel; see https://docs.homelab.example/network/wireguard-tunnel-between-nodes.")]
    public void should_accept_notes_that_only_name_where_secrets_are(string notes)
    {
        Errors($"apps:\n  - id: blog\n    notes: \"{notes}\"").Should().BeEmpty();
    }

    [Fact]
    public void should_refuse_a_key_that_is_not_a_file_name()
    {
        Errors("environments:\n  - id: server-1\n    access: { ssh: { host: 10.0.0.10, key: \"ssh-ed25519 AAAAC3Nza\" } }")
            .Should().ContainSingle().Which.Should().Contain("access.ssh.key must be the name or path of the key file");
    }

    [Fact]
    public void should_refuse_links_to_unknown_or_ambiguous_ends()
    {
        var errors = Errors("""
            environments:
              - { id: a, machines: [{ name: node-1 }] }
              - { id: b, machines: [{ name: node-1 }] }
            links:
              - { id: one, from: nowhere, to: a, kind: vpn }
              - { id: two, from: node-1, to: a, kind: vpn }
              - { id: three, from: a, to: a, kind: lan }
              - { id: four, from: a/node-1, to: b/node-1, kind: teleport, network: not-a-range }
            """);

        errors.Should().BeEquivalentTo(
            "Link 'one': from 'nowhere' is neither an environment nor a machine of the catalog (declare machines under their environment).",
            "Link 'two': from 'node-1' is a machine of several environments (a, b); write environment/machine.",
            "Link 'three': from and to must be two different environments or machines.",
            "Link 'four': kind must be one of wireguard-tunnel, vpn, lan, peering, ssh-tunnel, other.",
            "Link 'four': network must be an address range in CIDR notation (e.g. 10.0.0.0/24).");
    }

    [Fact]
    public void should_refuse_providers_used_by_unknown_ids_and_unknown_kinds()
    {
        var errors = Errors("""
            providers:
              - { id: host, kind: hosting, usedBy: [ghost], console: "ftp://host.example" }
            """);

        errors.Should().BeEquivalentTo(
            "Provider 'host': kind must be one of cdn, paas, dns, registrar, cloud, vps, storage, backup, email, git, registry, monitoring, other.",
            "Provider 'host': console 'ftp://host.example' must be an absolute http(s) URL without credentials.",
            "Provider 'host': usedBy 'ghost' is neither an app nor an environment of the catalog.");
    }

    [Fact]
    public void should_refuse_an_access_that_says_nothing()
    {
        Errors("environments:\n  - id: server-1\n    access: { consoles: [] }")
            .Should().ContainSingle().Which.Should().Contain("access must say how to reach the machine");
    }
}
