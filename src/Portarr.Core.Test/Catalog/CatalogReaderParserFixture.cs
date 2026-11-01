using Portarr.Core.Catalog;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Catalog;

/// <summary>The <c>reader:</c> key (D-101): one address and one token variable, read as the Docker window and the host metrics.</summary>
public sealed class CatalogReaderParserFixture
{
    [Fact]
    public void should_read_a_reader_as_the_docker_window_and_the_host_metrics_with_its_token_variable()
    {
        var result = TestCatalog.Parse("""
            environments:
              - id: server-1
                name: Server 1
                reader: { url: "http://10.0.0.10:8485", tokenEnv: PORTARR_READER_SERVER_1 }
            """);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        var environment = result.Catalog.Environments.Should().ContainSingle().Subject;
        // The reader is read as the docker section it stands for: the Docker library reads that section (its own tests).
        environment.Settings<Dictionary<string, string>>("docker").Should().BeEquivalentTo(new Dictionary<string, string> { ["url"] = "http://10.0.0.10:8485/v1/docker/", ["tokenEnv"] = "PORTARR_READER_SERVER_1" });
        environment.Settings<Dictionary<string, string>>("hostMetrics").Should().BeEquivalentTo(new Dictionary<string, string> { ["nodeExporter"] = "http://10.0.0.10:8485/v1/host", ["tokenEnv"] = "PORTARR_READER_SERVER_1" });
        environment.Runtime.Should().Be(RuntimeNames.Docker);
    }

    [Theory]
    [InlineData("https://proxy.homelab.example/reader-1", "https://proxy.homelab.example/reader-1/v1/docker/", "https://proxy.homelab.example/reader-1/v1/host")]
    [InlineData("https://proxy.homelab.example/reader-1/", "https://proxy.homelab.example/reader-1/v1/docker/", "https://proxy.homelab.example/reader-1/v1/host")]
    [InlineData("https://reader.homelab.example/", "https://reader.homelab.example/v1/docker/", "https://reader.homelab.example/v1/host")]
    public void should_resolve_the_routes_under_the_readers_own_path_behind_a_reverse_proxy(string url, string docker, string host)
    {
        var result = TestCatalog.Parse($$"""
            environments:
              - id: server-1
                reader: { url: "{{url}}", tokenEnv: PORTARR_READER_SERVER_1 }
            """);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        var environment = result.Catalog.Environments.Single();
        environment.Settings<Dictionary<string, string>>("docker")!["url"].Should().Be(docker);
        environment.Settings<Dictionary<string, string>>("hostMetrics")!["nodeExporter"].Should().Be(host);
    }

    [Theory]
    [InlineData("docker: { url: \"unix:///var/run/docker.sock\" }", "reader replaces docker and hostMetrics")]
    [InlineData("hostMetrics: { nodeExporter: \"http://10.0.0.10:9100/metrics\" }", "reader replaces docker and hostMetrics")]
    [InlineData("runtime: kubernetes", "cannot declare a reader")]
    public void should_refuse_a_reader_next_to_what_it_replaces(string other, string message)
    {
        var result = TestCatalog.Parse($$"""
            environments:
              - id: server-1
                {{other}}
                reader: { url: "http://10.0.0.10:8485", tokenEnv: PORTARR_READER_SERVER_1 }
            """);

        result.Errors.Should().ContainSingle().Which.Should().Contain("Environment 'server-1'").And.Contain(message);
    }

    [Theory]
    [InlineData("url: \"http://10.0.0.10:8485/v1?x=1\", tokenEnv: PORTARR_READER_A", "reader.url")]
    [InlineData("url: \"http://10.0.0.10:8485/#frag\", tokenEnv: PORTARR_READER_A", "reader.url")]
    [InlineData("url: \"http://user:pass@10.0.0.10:8485\", tokenEnv: PORTARR_READER_A", "reader.url")]
    [InlineData("url: \"ftp://10.0.0.10:8485\", tokenEnv: PORTARR_READER_A", "reader.url")]
    [InlineData("url: \"\", tokenEnv: PORTARR_READER_A", "reader.url")]
    [InlineData("tokenEnv: PORTARR_READER_A", "reader.url")]
    [InlineData("url: \"http://10.0.0.10:8485\"", "reader.tokenEnv")]
    [InlineData("url: \"http://10.0.0.10:8485\", tokenEnv: \"0123456789abcdef0123456789abcdef-secret\"", "reader.tokenEnv")]
    public void should_refuse_a_reader_without_an_address_and_a_variable_name(string reader, string key)
    {
        var result = TestCatalog.Parse($$"""
            environments:
              - id: server-1
                reader: { {{reader}} }
            """);

        result.Errors.Should().ContainSingle().Which.Should().Contain(key);
    }

    [Theory]
    [InlineData("GITHUB_TOKEN")]
    [InlineData("CLOUDFLARE_API_TOKEN")]
    [InlineData("PORTARR__APIKEY")]
    [InlineData("PORTARR_READER_")]
    [InlineData("portarr_reader_lower")]
    public void should_refuse_to_send_any_other_variable_of_the_process_to_a_url(string variable)
    {
        var reader = TestCatalog.Parse($$"""
            environments:
              - id: server-1
                reader: { url: "http://attacker.example:8485", tokenEnv: {{variable}} }
            """);
        reader.Errors.Should().ContainSingle().Which.Should().Contain("reader.tokenEnv").And.Contain("PORTARR_READER_");
    }

    [Fact]
    public void should_accept_a_token_variable_of_the_reader_namespace_on_docker_and_host_metrics()
    {
        var result = TestCatalog.Parse("""
            environments:
              - id: server-1
                docker: { url: "http://proxy.homelab.example:2375", tokenEnv: PORTARR_READER_PROXY }
                hostMetrics: { nodeExporter: "http://10.0.0.10:9100/metrics", tokenEnv: PORTARR_READER_EXPORTER }
            """);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Catalog.Environments.Single().Settings<Dictionary<string, string>>("docker")!["tokenEnv"].Should().Be("PORTARR_READER_PROXY");
        result.Catalog.Environments.Single().Settings<Dictionary<string, string>>("hostMetrics")!["tokenEnv"].Should().Be("PORTARR_READER_EXPORTER");
    }
}
