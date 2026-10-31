using Portarr.Core.Catalog;

namespace Portarr.Core.Test.Catalog;

/// <summary>The <c>reader:</c> key (D-101): one address and one token variable, read as the Docker window and the host metrics.</summary>
public sealed class CatalogReaderParserFixture
{
    [Fact]
    public void should_read_a_reader_as_the_docker_window_and_the_host_metrics_with_its_token_variable()
    {
        var result = CatalogParser.Parse("""
            environments:
              - id: server-1
                name: Server 1
                reader: { url: "http://10.0.0.10:8485", tokenEnv: PORTARR_READER_SERVER_1 }
            """);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        var environment = result.Catalog.Environments.Should().ContainSingle().Subject;
        environment.Docker.Should().Be(new DockerEndpoint(new Uri("http://10.0.0.10:8485/v1/docker/"), "PORTARR_READER_SERVER_1"));
        environment.HostMetrics.Should().Be(new HostMetricsEndpoint(new Uri("http://10.0.0.10:8485/v1/host"), "PORTARR_READER_SERVER_1"));
        environment.Runtime.Should().Be(RuntimeNames.Docker);
    }

    [Theory]
    [InlineData("docker: { url: \"unix:///var/run/docker.sock\" }", "reader replaces docker and hostMetrics")]
    [InlineData("hostMetrics: { nodeExporter: \"http://10.0.0.10:9100/metrics\" }", "reader replaces docker and hostMetrics")]
    [InlineData("runtime: kubernetes", "cannot declare a reader")]
    public void should_refuse_a_reader_next_to_what_it_replaces(string other, string message)
    {
        var result = CatalogParser.Parse($$"""
            environments:
              - id: server-1
                {{other}}
                reader: { url: "http://10.0.0.10:8485", tokenEnv: PORTARR_READER_SERVER_1 }
            """);

        result.Errors.Should().ContainSingle().Which.Should().Contain("Environment 'server-1'").And.Contain(message);
    }

    [Theory]
    [InlineData("url: \"http://10.0.0.10:8485/v1/docker\", tokenEnv: A_TOKEN", "reader.url")]
    [InlineData("url: \"http://user:pass@10.0.0.10:8485\", tokenEnv: A_TOKEN", "reader.url")]
    [InlineData("url: \"ftp://10.0.0.10:8485\", tokenEnv: A_TOKEN", "reader.url")]
    [InlineData("url: \"http://10.0.0.10:8485\"", "reader.tokenEnv")]
    [InlineData("url: \"http://10.0.0.10:8485\", tokenEnv: \"0123456789abcdef0123456789abcdef-secret\"", "reader.tokenEnv")]
    public void should_refuse_a_reader_without_a_plain_address_and_a_variable_name(string reader, string key)
    {
        var result = CatalogParser.Parse($$"""
            environments:
              - id: server-1
                reader: { {{reader}} }
            """);

        result.Errors.Should().ContainSingle().Which.Should().Contain(key);
    }

    [Fact]
    public void should_accept_an_optional_token_variable_on_docker_and_host_metrics_and_refuse_a_token()
    {
        var valid = CatalogParser.Parse("""
            environments:
              - id: server-1
                docker: { url: "http://proxy.homelab.example:2375", tokenEnv: PROXY_TOKEN }
                hostMetrics: { nodeExporter: "http://10.0.0.10:9100/metrics", tokenEnv: EXPORTER_TOKEN }
            """);
        var invalid = CatalogParser.Parse("""
            environments:
              - id: server-1
                docker: { url: "http://proxy.homelab.example:2375", tokenEnv: "not a variable name!" }
            """);

        valid.IsValid.Should().BeTrue(string.Join("; ", valid.Errors));
        valid.Catalog.Environments.Single().Docker!.TokenVariable.Should().Be("PROXY_TOKEN");
        valid.Catalog.Environments.Single().HostMetrics!.TokenVariable.Should().Be("EXPORTER_TOKEN");
        invalid.Errors.Should().ContainSingle().Which.Should().Contain("docker.tokenEnv");
    }
}
