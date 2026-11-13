using System.Net;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure.Gpu;
using Portarr.Test.Common;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.GpuExporter.Test;

internal static class GpuEnvironments
{
    public static CatalogEnvironment With(string url = "http://10.0.0.10:9835/metrics")
    {
        return new CatalogEnvironment(
            "server-1",
            "Server 1",
            Connectors: new Dictionary<string, object> { ["docker"] = new object(), [GpuExporterEnvironmentConnector.SectionName] = new GpuEndpoint(new Uri(url)) },
            ConnectorRuntime: RuntimeNames.Docker);
    }

    public static CatalogEnvironment Without() => new("server-2", "Server 2", Connectors: new Dictionary<string, object> { ["docker"] = new object() }, ConnectorRuntime: RuntimeNames.Docker);
}

public sealed class GpuExporterSourceContractFixture : GpuSourceContract
{
    protected override GpuHarness Arrange()
    {
        var text = GpuExporterSourceFixture.OneCard;
        var broken = false;
        var http = new StubHttp(_ => broken ? new HttpResponseMessage(HttpStatusCode.BadGateway) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
        return new GpuHarness(
            new GpuExporterSource(http),
            GpuEnvironments.With(),
            GpuEnvironments.Without(),
            () => text = GpuExporterSourceFixture.OneCard,
            () => text = "# only the exporter's own families\ngo_goroutines 8\n",
            () => broken = true);
    }
}

public sealed class GpuExporterSourceFixture
{
    /// <summary>The families of a real scrape of an idle RTX 2070 (uuid and serial made up), with the exporter's own ones around.</summary>
    internal const string OneCard = """
        # HELP go_goroutines Number of goroutines that currently exist.
        go_goroutines 8
        # HELP nvidia_smi_gpu_info gpu info
        nvidia_smi_gpu_info{compute_cap="7.5",cuda_version="13.4",driver_version="615.78.08",index="0",name="NVIDIA GeForce RTX 2070",pci_bus_id="00000000:01:00.0",serial="[N/A]",uuid="GPU-1111"} 1
        nvidia_smi_utilization_gpu_ratio{uuid="GPU-1111"} 0.42
        nvidia_smi_utilization_encoder_ratio{uuid="GPU-1111"} 0.1
        nvidia_smi_utilization_decoder_ratio{uuid="GPU-1111"} 0
        nvidia_smi_utilization_memory_ratio{uuid="GPU-1111"} 0.05
        nvidia_smi_memory_used_bytes{uuid="GPU-1111"} 1.21831424e+09
        nvidia_smi_memory_total_bytes{uuid="GPU-1111"} 8.589934592e+09
        nvidia_smi_encoder_stats_session_count{uuid="GPU-1111"} 2
        nvidia_smi_temperature_gpu{uuid="GPU-1111"} 41
        nvidia_smi_power_draw_watts{uuid="GPU-1111"} 49.889
        nvidia_smi_power_limit_watts{uuid="GPU-1111"} 225
        nvidia_smi_clocks_current_sm_clock_hz{uuid="GPU-1111"} 1.41e+09
        """;

    private static Task<IReadOnlyList<GpuReading>> Parse(string text) => GpuExporterSource.ParseAsync(new StringReader(text), TestContext.Current.CancellationToken);

    [Fact]
    public async Task should_read_a_card_with_its_loads_as_percent_its_memory_temperature_and_power()
    {
        var card = (await Parse(OneCard)).Should().ContainSingle().Subject;

        card.Should().Be(new GpuReading("GPU-1111", "NVIDIA GeForce RTX 2070", "615.78.08", 42, 1_218_314_240, 8_589_934_592, 10, 0, 2, 41, 49.9, 225));
    }

    [Fact]
    public async Task should_order_several_cards_by_index_and_keep_each_cards_figures_apart()
    {
        var text = """
            nvidia_smi_gpu_info{driver_version="1",index="1",name="Second",uuid="GPU-B"} 1
            nvidia_smi_gpu_info{driver_version="1",index="0",name="First",uuid="GPU-A"} 1
            nvidia_smi_utilization_gpu_ratio{uuid="GPU-A"} 0.1
            nvidia_smi_utilization_gpu_ratio{uuid="GPU-B"} 0.9
            """;

        var cards = await Parse(text);

        cards.Select(c => (c.Name, c.UtilizationPercent)).Should().Equal(("First", 10.0), ("Second", 90.0));
    }

    [Fact]
    public async Task should_report_a_figure_the_exporter_did_not_give_as_missing_and_never_outside_0_to_100()
    {
        var text = """
            nvidia_smi_gpu_info{index="0",name="Card",uuid="GPU-A"} 1
            nvidia_smi_utilization_gpu_ratio{uuid="GPU-A"} 1.7
            nvidia_smi_utilization_encoder_ratio{uuid="GPU-A"} NaN
            nvidia_smi_memory_used_bytes{uuid="GPU-A"} -5
            """;

        var card = (await Parse(text)).Single();

        (card.UtilizationPercent, card.EncoderPercent, card.MemoryUsedBytes, card.TemperatureCelsius, card.Driver).Should().Be((100.0, (double?)null, (long?)null, (double?)null, (string?)null));
    }

    [Fact]
    public async Task should_ignore_a_sample_without_a_card_identifier()
    {
        (await Parse("nvidia_smi_utilization_gpu_ratio 0.5\nnvidia_smi_utilization_gpu_ratio{uuid=\"\"} 0.5")).Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_an_answer_larger_than_the_cap_even_when_it_is_one_line_that_never_ends()
    {
        var http = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', GpuExporterSource.MaxBytes + 1)) });

        var act = () => new GpuExporterSource(http).ReadAsync(GpuEnvironments.With(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task should_ask_the_exporter_url_of_the_environment_with_a_get_and_nothing_else()
    {
        var http = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OneCard) });

        await new GpuExporterSource(http).ReadAsync(GpuEnvironments.With("http://10.0.0.10:9835/metrics"), TestContext.Current.CancellationToken);

        http.Requests.Should().Equal(new Uri("http://10.0.0.10:9835/metrics"));
    }
}

public sealed class GpuExporterEnvironmentConnectorFixture
{
    private static CatalogParseResult Parse(string yaml) => CatalogParser.Parse(yaml, [new GpuExporterEnvironmentConnector(), new StubEnvironmentConnector("docker", isRuntime: true), new StubEnvironmentConnector("cloudflare", isRuntime: true, singleMachine: false)]);

    [Fact]
    public void should_read_the_exporter_of_a_docker_host()
    {
        var parsed = Parse("environments:\n  - { id: server-1, docker: { url: \"http://10.0.0.10:2375\" }, nvidiaGpu: { exporter: \"http://10.0.0.10:9835/metrics\" } }\n");

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments.Single().Gpu().Should().Be(new GpuEndpoint(new Uri("http://10.0.0.10:9835/metrics")));
    }

    [Theory]
    [InlineData("nvidiaGpu: { exporter: \"http://u:p@10.0.0.10:9835/metrics\" }", "nvidiaGpu.exporter must be")]
    [InlineData("nvidiaGpu: { exporter: nope }", "nvidiaGpu.exporter must be")]
    [InlineData("nvidiaGpu: {}", "nvidiaGpu.exporter must be")]
    [InlineData("nvidiaGpu: { exporter: \"http://10.0.0.10:9835/metrics\", tokenEnv: X }", "tokenEnv")]
    public void should_refuse_a_section_that_is_not_an_exporter_url_or_carries_a_credential(string section, string expected)
    {
        var result = Parse($"environments:\n  - {{ id: server-1, docker: {{ url: \"http://10.0.0.10:2375\" }}, {section} }}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain(expected);
    }

    [Fact]
    public void should_only_be_for_an_environment_that_is_one_machine_with_a_runtime()
    {
        var result = Parse("environments:\n  - { id: cdn, cloudflare: { accountId: abc }, nvidiaGpu: { exporter: \"http://10.0.0.10:9835/metrics\" } }\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("nvidiaGpu is for a docker environment");
    }
}
