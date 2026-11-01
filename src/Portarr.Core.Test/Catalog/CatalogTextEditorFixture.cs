using System.Text.Json.Nodes;
using Portarr.Core.Catalog;
using Portarr.Core.Catalog.Editing;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Catalog;

public sealed class CatalogTextEditorFixture
{
    // A catalog written by hand: comments everywhere, flow-style apps, block-style data, two-space lists.
    private const string Catalog = """
        # My homelab (private).
        environments:
          - id: server-1
            name: Server 1            # the hypervisor
            address: 10.0.0.10
            docker: { url: "http://10.0.0.10:2375" }
          # The cluster: two nodes.
          - id: cluster
            name: Cluster
            runtime: kubernetes
            kubernetes:
              server: "https://10.0.0.20:6443"
              tokenEnv: K8S_TOKEN
              nodeHosts: { node-1: server-1 }   # node-2 is a VPS

        stacks:
          - id: media
            name: Media
            members:
              - { app: jellyfin, role: critical }
              - { app: sonarr, role: optional }   # nice to have

        apps:
          # --- Media
          - { id: jellyfin, name: Jellyfin, urls: [{ label: Public, href: "https://jellyfin.homelab.example" }] }   # main
          - { id: sonarr, name: Sonarr, type: service }
          # --- Tools
          - id: grafana
            name: Grafana   # dashboards
            urls:
              - { href: "https://grafana.homelab.example" }

        # Followed data.
        data:
          - id: jellyfin-config
            app: jellyfin
            kind: volume
            where: { environment: server-1, path: /mnt/appdata/jellyfin }   # appdata
            importance: comfort
            targets:
              local: { manual: true }
        """;

    private static string Apply(string text, string section, string id, JsonObject? item) => CatalogTextEditor.Apply(text, section, id, item).Text;

    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    /// <summary>The lines of <paramref name="after"/> that are not in <paramref name="before"/>, and the reverse.</summary>
    private static (List<string> Added, List<string> Removed) Diff(string before, string after)
    {
        var old = before.Split('\n').ToList();
        var next = after.Split('\n').ToList();
        return (next.Except(old).ToList(), old.Except(next).ToList());
    }

    private static void ShouldBeValid(string text)
    {
        var result = TestCatalog.Parse(text);
        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public void should_add_a_key_to_a_block_item_and_keep_every_comment()
    {
        var after = Apply(Catalog, "apps", "grafana", Json("""{ "id": "grafana", "name": "Grafana", "urls": [{ "href": "https://grafana.homelab.example" }], "notes": "Behind the SSO." }"""));

        var (added, removed) = Diff(Catalog, after);
        removed.Should().BeEmpty();
        added.Should().Equal("    notes: Behind the SSO.");
        after.Should().Contain("    urls:\n      - { href: \"https://grafana.homelab.example\" }\n    notes: Behind the SSO.\n");
        ShouldBeValid(after);
    }

    [Fact]
    public void should_change_one_value_in_place_and_keep_its_trailing_comment()
    {
        var after = Apply(Catalog, "environments", "server-1", Json("""{ "id": "server-1", "name": "Server One", "address": "10.0.0.10", "docker": { "url": "http://10.0.0.10:2375" } }"""));

        var (added, removed) = Diff(Catalog, after);
        removed.Should().Equal("    name: Server 1            # the hypervisor");
        added.Should().Equal("    name: Server One            # the hypervisor");
    }

    [Fact]
    public void should_re_emit_a_flow_item_on_its_line_and_keep_the_comment_after_it()
    {
        var after = Apply(Catalog, "apps", "jellyfin", Json("""{ "id": "jellyfin", "name": "Jellyfin", "icon": "jellyfin", "urls": [{ "label": "Public", "href": "https://jellyfin.homelab.example" }] }"""));

        var (added, removed) = Diff(Catalog, after);
        removed.Should().ContainSingle();
        added.Should().Equal("  - { id: jellyfin, name: Jellyfin, icon: jellyfin, urls: [{ label: Public, href: \"https://jellyfin.homelab.example\" }] }   # main");
        ShouldBeValid(after);
    }

    [Fact]
    public void should_write_a_flow_item_in_block_style_when_it_gains_multi_line_notes()
    {
        var after = Apply(Catalog, "apps", "sonarr", Json("""{ "id": "sonarr", "name": "Sonarr", "type": "service", "notes": "Runs nightly.\nAsk before upgrading." }"""));

        after.Should().Contain("""
              - id: sonarr
                name: Sonarr
                type: service
                notes: |-
                  Runs nightly.
                  Ask before upgrading.
              # --- Tools
            """);
        TestCatalog.Parse(after).Catalog.Apps.Single(a => a.Id == "sonarr").Notes.Should().Be("Runs nightly.\nAsk before upgrading.");
    }

    [Fact]
    public void should_move_the_trailing_comment_of_a_flow_item_off_a_literal_block()
    {
        var after = Apply(Catalog, "apps", "jellyfin", Json("""{ "id": "jellyfin", "name": "Jellyfin", "notes": "One.\nTwo." }"""));

        after.Should().Contain("  - id: jellyfin\n    name: Jellyfin\n    notes: |-\n      One.\n      Two.\n    # main\n");
        TestCatalog.Parse(after).Catalog.Apps.Single(a => a.Id == "jellyfin").Notes.Should().Be("One.\nTwo.");
    }

    [Fact]
    public void should_append_a_new_item_in_the_style_of_most_items()
    {
        var after = Apply(Catalog, "apps", "radarr", Json("""{ "id": "radarr", "name": "Radarr", "type": "service" }"""));

        Diff(Catalog, after).Added.Should().Equal("  - { id: radarr, name: Radarr, type: service }");
        after.Should().Contain("      - { href: \"https://grafana.homelab.example\" }\n  - { id: radarr, name: Radarr, type: service }\n\n# Followed data.");
        ShouldBeValid(after);
    }

    [Fact]
    public void should_append_a_block_item_after_block_items()
    {
        var after = Apply(Catalog, "data", "sonarr-config", Json("""{ "id": "sonarr-config", "app": "sonarr", "kind": "volume", "where": { "environment": "server-1" }, "importance": "comfort", "targets": { "local": { "manual": true } } }"""));

        after.Should().EndWith("""
                  local: { manual: true }
              - id: sonarr-config
                app: sonarr
                kind: volume
                where: { environment: server-1 }
                importance: comfort
                targets: { local: { manual: true } }
            """.TrimEnd());
        ShouldBeValid(after);
    }

    [Fact]
    public void should_create_a_missing_section_at_the_end_with_the_file_indentation()
    {
        var after = Apply(Catalog, "providers", "dns-host", Json("""{ "id": "dns-host", "name": "DNS host", "kind": "dns", "console": "https://dns.example/manager" }"""));

        after.Should().StartWith(Catalog);
        after[Catalog.Length..].Should().Be("\n\nproviders:\n  - id: dns-host\n    name: DNS host\n    kind: dns\n    console: \"https://dns.example/manager\"\n");
        ShouldBeValid(after);
    }

    [Fact]
    public void should_remove_an_item_with_its_lines_only()
    {
        var after = Apply(Catalog, "apps", "sonarr", null);

        var (added, removed) = Diff(Catalog, after);
        added.Should().BeEmpty();
        removed.Should().Equal("  - { id: sonarr, name: Sonarr, type: service }");
    }

    [Fact]
    public void should_remove_a_block_item_spread_over_several_lines()
    {
        var after = Apply(Catalog, "apps", "grafana", null);

        after.Should().Contain("  - { id: sonarr, name: Sonarr, type: service }\n  # --- Tools\n\n# Followed data.");
    }

    [Fact]
    public void should_leave_an_empty_list_when_the_last_item_goes()
    {
        var after = Apply(Catalog, "data", "jellyfin-config", null);

        after.Should().EndWith("# Followed data.\ndata: []");
        ShouldBeValid(after);
    }

    [Fact]
    public void should_append_a_member_and_keep_the_comments_of_the_others()
    {
        var after = Apply(Catalog, "stacks", "media", Json("""{ "id": "media", "name": "Media", "members": [{ "app": "jellyfin", "role": "critical" }, { "app": "sonarr", "role": "optional" }, { "app": "grafana", "role": "optional" }] }"""));

        after.Should().Contain("      - { app: sonarr, role: optional }   # nice to have\n      - { app: grafana, role: optional }\n");
        ShouldBeValid(after);
    }

    [Fact]
    public void should_change_one_member_in_place()
    {
        var after = Apply(Catalog, "stacks", "media", Json("""{ "id": "media", "name": "Media", "members": [{ "app": "jellyfin", "role": "critical" }, { "app": "sonarr", "role": "critical" }] }"""));

        Diff(Catalog, after).Added.Should().Equal("      - { app: sonarr, role: critical }   # nice to have");
    }

    [Fact]
    public void should_remove_a_key_and_keep_its_neighbours()
    {
        var after = Apply(Catalog, "environments", "server-1", Json("""{ "id": "server-1", "name": "Server 1", "docker": { "url": "http://10.0.0.10:2375" } }"""));

        Diff(Catalog, after).Removed.Should().Equal("    address: 10.0.0.10");
        Diff(Catalog, after).Added.Should().BeEmpty();
    }

    [Fact]
    public void should_patch_a_nested_mapping_and_keep_the_comment_on_its_last_line()
    {
        var after = Apply(Catalog, "environments", "cluster", Json("""
            { "id": "cluster", "name": "Cluster", "runtime": "kubernetes",
              "kubernetes": { "server": "https://10.0.0.20:6443", "tokenEnv": "K8S_TOKEN", "nodeHosts": { "node-1": "server-1" } },
              "machines": [{ "name": "node-2", "notes": "A VPS.", "access": { "ssh": { "alias": "node-2", "user": "ubuntu", "key": "~/.ssh/node-2" } } }] }
            """));

        after.Should().Contain("""
                  nodeHosts: { node-1: server-1 }   # node-2 is a VPS
                machines:
                  - { name: node-2, notes: A VPS., access: { ssh: { alias: node-2, user: ubuntu, key: ~/.ssh/node-2 } } }
            """);
        ShouldBeValid(after);
    }

    [Fact]
    public void should_quote_what_yaml_would_read_differently()
    {
        var after = Apply(Catalog, "apps", "sonarr", Json("""{ "id": "sonarr", "name": "yes", "description": "a: b # c", "tags": ["1.0", "null", "- x"], "type": "service" }"""));

        Diff(Catalog, after).Added.Should().Equal("  - { id: sonarr, name: \"yes\", description: \"a: b # c\", tags: [\"1.0\", \"null\", \"- x\"], type: service }");
        var sonarr = TestCatalog.Parse(after).Catalog.Apps.Single(a => a.Id == "sonarr");
        sonarr.Name.Should().Be("yes");
        sonarr.Tags.Should().Equal("1.0", "null", "- x");
    }

    [Fact]
    public void should_keep_windows_line_endings()
    {
        var crlf = Catalog.ReplaceLineEndings("\r\n");

        var after = Apply(crlf, "apps", "grafana", Json("""{ "id": "grafana", "name": "Grafana", "urls": [{ "href": "https://grafana.homelab.example" }], "notes": "Two\nlines" }"""));

        after.Replace("\r\n", string.Empty, StringComparison.Ordinal).Should().NotContain("\n");
        TestCatalog.Parse(after).Catalog.Apps.Single(a => a.Id == "grafana").Notes.Should().Be("Two\nlines");
    }

    [Fact]
    public void should_start_an_empty_file()
    {
        var after = Apply(string.Empty, "apps", "sonarr", Json("""{ "id": "sonarr", "name": "Sonarr" }"""));

        after.Should().Be("apps:\n  - id: sonarr\n    name: Sonarr\n");
    }

    [Fact]
    public void should_fill_an_empty_section()
    {
        var after = Apply("apps: []\nlinks:\n", "links", "tunnel", Json("""{ "id": "tunnel", "from": "a", "to": "b", "kind": "vpn" }"""));

        after.Should().Be("apps: []\nlinks:\n  - id: tunnel\n    from: a\n    to: b\n    kind: vpn\n");
    }

    [Fact]
    public void should_refuse_a_file_whose_root_is_not_a_mapping()
    {
        var act = () => Apply("- a\n- b\n", "apps", "x", Json("""{ "id": "x" }"""));

        act.Should().Throw<CatalogEditException>().Which.Message.Should().Contain("root must be a mapping");
    }

    [Fact]
    public void should_say_when_the_item_to_remove_is_not_there()
    {
        var act = () => Apply(Catalog, "apps", "nope", null);

        act.Should().Throw<CatalogEditException>().Which.Status.Should().Be(CatalogEditStatus.NotFound);
    }
}
