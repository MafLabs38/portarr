using Portarr.Core.Catalog;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Catalog;

/// <summary>The <c>check:</c> key of apps (D-50) and Cloudflare environments (D-53).</summary>
public sealed class CatalogCheckParserFixture
{

    [Fact]
    public void should_check_web_apps_by_default_and_not_services()
    {
        var catalog = Parse("""
            apps:
              - { id: web, urls: [{ href: "https://web.homelab.example" }] }
              - { id: db, type: service }
            """);

        catalog.Apps[0].Check.Should().Be(CheckDefinition.Default);
        catalog.Apps[1].Check.Should().BeNull();
    }

    [Fact]
    public void should_turn_the_check_off_with_false()
    {
        Parse("apps: [{ id: web, check: false }]").Apps[0].Check.Should().BeNull();
    }

    [Fact]
    public void should_read_a_custom_check()
    {
        var check = Parse("""
            apps:
              - id: grafana
                check:
                  url: "http://10.0.0.10:3000/api/health"
                  expect: [200, 401, 500-504]
                  keyword: database
                  interval: 5m
                  timeout: 15
                  critical: false
            """).Apps[0].Check!;

        check.Url.Should().Be(new Uri("http://10.0.0.10:3000/api/health"));
        check.Expect.Should().Equal(new StatusCodeRange(200, 200), new StatusCodeRange(401, 401), new StatusCodeRange(500, 504));
        check.Keyword.Should().Be("database");
        check.Interval.Should().Be(TimeSpan.FromMinutes(5));
        check.Timeout.Should().Be(TimeSpan.FromSeconds(15));
        check.Critical.Should().BeFalse();
    }

    [Theory]
    [InlineData("check: sometimes", "check must be false, true or a mapping")]
    [InlineData("check: { expect: [700] }", "check.expect '700'")]
    [InlineData("check: { expect: [299-200] }", "check.expect '299-200'")]
    [InlineData("check: { interval: 2s, timeout: 1s }", "check.interval must be between 10s and 24h")]
    [InlineData("check: { interval: 30s, timeout: 45s }", "check.timeout must be between 1s and 60s")]
    [InlineData("check: { url: \"ftp://files.homelab.example\" }", "check.url")]
    [InlineData("check: { url: \"https://admin:secret@app.homelab.example\" }", "https://***@app.homelab.example")]
    [InlineData("check: { method: POST }", "Invalid YAML")]
    public void should_reject_an_invalid_check_naming_the_app(string check, string error)
    {
        var result = TestCatalog.Parse($"apps:\n  - id: web\n    {check}\n");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain(error);
    }

    [Fact]
    public void should_read_a_cloudflare_environment_and_its_workloads()
    {
        var catalog = Parse("""
            environments:
              - id: cdn
                runtime: cloudflare
                cloudflare: { accountId: TODO }
            apps:
              - id: site
                workloads: [{ runtime: cloudflare@cdn, match: { name: site } }]
            """);

        catalog.Environments[0].Declares("cloudflare").Should().BeTrue();
        catalog.Environments[0].Runtime.Should().Be("cloudflare");
        catalog.Apps[0].Workloads.Should().ContainSingle().Which.EnvironmentId.Should().Be("cdn");
    }

    [Fact]
    public void should_read_the_type_of_a_check_and_default_it_to_http()
    {
        var typed = Parse("apps: [{ id: web, check: { type: http, url: \"https://web.homelab.example/health\" } }]").Apps[0].Check!;
        var plain = Parse("apps: [{ id: web, check: { url: \"https://web.homelab.example/health\" } }]").Apps[0].Check!;

        typed.Type.Should().Be("http");
        plain.Type.Should().Be("http");
        typed.Should().Be(plain);
    }

    [Fact]
    public void should_refuse_a_check_type_nobody_runs()
    {
        var result = TestCatalog.Parse("apps: [{ id: web, check: { type: ping, url: \"https://web.homelab.example/\" } }]");

        result.Errors.Should().ContainSingle().Which.Should().Contain("check.type must be http");
    }

    [Fact]
    public void should_name_the_runtimes_that_are_known_when_one_is_not()
    {
        var result = TestCatalog.Parse("environments:\n  - id: cdn\n    runtime: nomad\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("runtime must be cloudflare, docker or kubernetes");
    }

    [Fact]
    public void should_reject_a_selector_whose_runtime_is_not_the_environment_one()
    {
        var result = TestCatalog.Parse("""
            environments:
              - { id: cdn, cloudflare: { accountId: abc } }
            apps:
              - { id: site, workloads: [{ runtime: docker@cdn, match: { name: site } }] }
            """);

        result.Errors.Should().ContainSingle().Which.Should().Contain("use cloudflare@cdn");
    }

    private static Portarr.Core.Catalog.Catalog Parse(string yaml)
    {
        var result = TestCatalog.Parse(yaml);
        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        return result.Catalog;
    }
}
