using Portarr.Core.Catalog;

namespace Portarr.Core.Test.Catalog;

public sealed class CatalogParserFixture
{
    private const string ValidCatalog = """
        environments:
          - id: server-1
            name: Server 1
            docker: { url: "http://socket-proxy:2375" }
        stacks:
          - id: media
            name: Media
            members:
              - { app: recyclarr, role: optional }
        apps:
          - id: sonarr
            name: Sonarr
            icon: sonarr
            stack: media
            tags: [arr]
            urls:
              - { label: Public, href: "https://sonarr.homelab.example" }
            workloads:
              - { runtime: docker@server-1, match: { name: sonarr } }
          - id: recyclarr
            name: Recyclarr
            type: service
        """;

    [Fact]
    public void should_read_environments_stacks_and_apps()
    {
        var result = CatalogParser.Parse(ValidCatalog);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Catalog.Environments.Should().ContainSingle().Which.Docker!.Url.Should().Be(new Uri("http://socket-proxy:2375"));
        result.Catalog.Apps.Should().HaveCount(2);

        var sonarr = result.Catalog.Apps[0];
        sonarr.Type.Should().Be(AppType.Web);
        sonarr.Urls.Should().ContainSingle().Which.Href.Should().Be(new Uri("https://sonarr.homelab.example"));
        sonarr.Workloads.Should().ContainSingle().Which.Should().Be(new WorkloadSelector("server-1", "sonarr"));
        result.Catalog.Apps[1].Type.Should().Be(AppType.Service);
    }

    [Fact]
    public void should_make_the_stack_shorthand_a_critical_member()
    {
        var media = CatalogParser.Parse(ValidCatalog).Catalog.Stacks.Single();

        media.Members.Should().BeEquivalentTo(new[]
        {
            new StackMember("recyclarr", MemberRole.Optional),
            new StackMember("sonarr", MemberRole.Critical),
        });
    }

    [Fact]
    public void should_read_the_lan_address_url_scopes_and_workload_roles()
    {
        const string yaml = """
            environments:
              - { id: server-1, address: 10.0.0.10, docker: { url: "http://socket-proxy:2375" } }
            apps:
              - id: trading
                urls:
                  - { href: "https://trading.homelab.example" }
                  - { href: "http://10.0.0.10:3000" }
                  - { label: Tunnel, href: "https://tunnel.homelab.example", scope: lan }
                workloads:
                  - { runtime: docker@server-1, match: { name: trading-front } }
                  - { runtime: docker@server-1, match: { name: trading-scanner }, role: optional }
            """;

        var result = CatalogParser.Parse(yaml);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Catalog.Environments.Single().Address.Should().Be("10.0.0.10");
        result.Catalog.Apps.Single().Urls.Select(u => u.Scope).Should().Equal(UrlScope.Public, UrlScope.Lan, UrlScope.Lan);
        result.Catalog.Apps.Single().Workloads.Select(w => w.Role).Should().Equal(MemberRole.Critical, MemberRole.Optional);
    }

    [Fact]
    public void should_accept_an_empty_file()
    {
        var result = CatalogParser.Parse(string.Empty);

        result.IsValid.Should().BeTrue();
        result.Catalog.Apps.Should().BeEmpty();
    }

    [Theory]
    [InlineData("apps:\n  - id: Sonarr", "id 'Sonarr' must be lowercase")]
    [InlineData("apps:\n  - id: a\n  - id: a", "id 'a' is used twice")]
    [InlineData("apps:\n  - id: a\n    stack: nope", "stack 'nope' is not declared")]
    [InlineData("apps:\n  - id: a\n    type: game", "type must be web, desktop or service")]
    [InlineData("apps:\n  - id: a\n    urls: [{ href: \"ftp://x\" }]", "must be an absolute http(s) URL")]
    [InlineData("apps:\n  - id: a\n    workloads: [{ runtime: docker@ghost, match: { name: a } }]", "environment 'ghost' is not declared")]
    [InlineData("apps:\n  - id: a\n    workloads: [{ runtime: podman@x }]", "must be docker@<environment id>")]
    [InlineData("stacks:\n  - id: s\n    members: [{ app: ghost }]", "member 'ghost' is not a declared app")]
    [InlineData("environments:\n  - id: e\n    docker: { url: \"ssh://host\" }", "docker.url must be")]
    [InlineData("apps:\n  - id: a\n    colour: red", "Invalid YAML")]
    [InlineData("apps: [", "Invalid YAML")]
    [InlineData("apps:\n  - id: a\n    urls: [{ href: \"http://x.example\", scope: intranet }]", "must be public or lan")]
    [InlineData("environments:\n  - { id: e, docker: { url: \"http://p:2375\" } }\napps:\n  - id: a\n    workloads: [{ runtime: docker@e, match: { name: a }, role: maybe }]", "must be critical or optional")]
    public void should_report_a_clear_error_and_return_an_empty_catalog(string yaml, string expected)
    {
        var result = CatalogParser.Parse(yaml);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains(expected, StringComparison.Ordinal));
        result.Catalog.Should().Be(Portarr.Core.Catalog.Catalog.Empty);
    }
}
