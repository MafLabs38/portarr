using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;

namespace Portarr.Providers.NodeExporter.Test;

/// <summary>The Portarr reader (D-100) asks for a bearer token on its node_exporter-format endpoint; node_exporter itself asks for none.</summary>
public sealed class NodeExporterBearerFixture
{
    private const string Text = "node_memory_MemTotal_bytes 1000\nnode_memory_MemAvailable_bytes 400\n";

    private static readonly Uri ReaderUrl = new("http://10.0.0.10:8485/v1/host");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task should_send_the_token_of_the_environments_own_endpoint_as_a_bearer()
    {
        var handler = new RecordingHandler();
        var source = Source(handler, "secret-token");

        await source.ReadAsync(Target(ReaderUrl), Token);

        handler.Authorization.Should().Be("Bearer secret-token");
    }

    [Fact]
    public async Task should_send_no_token_to_an_endpoint_that_is_not_the_environments_own()
    {
        var handler = new RecordingHandler();
        var source = Source(handler, "secret-token");

        await source.ReadAsync(Target(new Uri("http://10.0.0.11:9100/metrics")), Token);

        handler.Authorization.Should().BeNull("a node's exporter reached another way must never receive the reader's token");
    }

    [Fact]
    public async Task should_fail_as_unauthorized_rather_than_read_anonymously_when_the_token_variable_is_empty()
    {
        var handler = new RecordingHandler();
        var source = Source(handler, null);

        var read = () => source.ReadAsync(Target(ReaderUrl), Token);

        (await read.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        handler.Requests.Should().Be(0);
    }

    [Theory]
    [InlineData(null, "token-missing")]
    [InlineData("secret-token", null)]
    public void should_report_a_missing_token_as_a_reason_code_before_reading(string? token, string? expected)
    {
        var source = Source(new RecordingHandler(), token);

        source.GetConfigurationError(Target(ReaderUrl)).Should().Be(expected);
    }

    [Fact]
    public void should_report_no_configuration_error_for_an_exporter_that_is_not_the_environments_own()
    {
        var source = Source(new RecordingHandler(), null);

        source.GetConfigurationError(Target(new Uri("http://10.0.0.11:9100/metrics"))).Should().BeNull();
    }

    private static NodeExporterHostMetricsSource Source(RecordingHandler handler, string? token)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(NodeExporterHostMetricsSource.HttpClientName).Returns(_ => new HttpClient(handler));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(token is null ? [] : new Dictionary<string, string?> { ["PORTARR_READER_TEST"] = token }).Build();
        return new NodeExporterHostMetricsSource(factory, configuration);
    }

    private static HostMetricsTarget Target(Uri url)
    {
        var environment = NodeExporterEnvironments.With(new HostMetricsEndpoint(ReaderUrl, "PORTARR_READER_TEST"));
        return new HostMetricsTarget(environment, "server-1", url);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Text, Encoding.UTF8, "text/plain") });
        }
    }
}
