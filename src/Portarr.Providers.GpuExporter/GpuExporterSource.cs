using System.Globalization;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure.Gpu;
using Portarr.Core.Metrics;

namespace Portarr.Providers.GpuExporter;

/// <summary>
/// The NVIDIA GPU exporter (<c>utkuozdemir/nvidia_gpu_exporter</c>) read over HTTP (D-120): one GET of its <c>/metrics</c> URL, streamed
/// and filtered to the few families the card needs, size capped, no redirect. Every family carries the card's <c>uuid</c>; the model and
/// the driver are labels of <c>nvidia_smi_gpu_info</c>; loads are ratios (0 to 1) of the exporter's base units.
/// </summary>
public sealed class GpuExporterSource : IGpuSource
{
    public const string HttpClientName = "gpu-exporter";

    /// <summary>The largest answer read: a card's families are a few hundred lines, and a runaway answer is not the exporter.</summary>
    public const int MaxCharacters = 1_000_000;

    private const string Prefix = "nvidia_smi_";

    private readonly IHttpClientFactory _httpClientFactory;

    public GpuExporterSource(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public bool Supports(CatalogEnvironment environment) => environment.Gpu() is not null;

    public async Task<IReadOnlyList<GpuReading>> ReadAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        var endpoint = environment.Gpu() ?? throw new InvalidOperationException("The environment declares no GPU exporter.");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Exporter);
        using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        return await ParseAsync(reader, cancellationToken);
    }

    /// <summary>The cards of a scrape, in the order of their <c>index</c>; none when the exporter shows none.</summary>
    internal static async Task<IReadOnlyList<GpuReading>> ParseAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var cards = new Dictionary<string, Card>(StringComparer.Ordinal);
        long read = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            read += line.Length + 1;
            if (read > MaxCharacters)
            {
                throw new InvalidDataException($"The metrics answer is larger than {MaxCharacters} characters.");
            }

            if (line.StartsWith(Prefix, StringComparison.Ordinal) && PrometheusText.TryParseSample(line, IsWanted, out var sample) && sample.Label("uuid") is { Length: > 0 } uuid)
            {
                if (!cards.TryGetValue(uuid, out var card))
                {
                    cards[uuid] = card = new Card(uuid);
                }

                card.Add(sample);
            }
        }

        return cards.Values.OrderBy(c => c.Index).ThenBy(c => c.Id, StringComparer.Ordinal).Select(c => c.ToReading()).ToList();
    }

    private static bool IsWanted(string name) => Families.Contains(name);

    private static readonly HashSet<string> Families = new(StringComparer.Ordinal)
    {
        "nvidia_smi_gpu_info",
        "nvidia_smi_utilization_gpu_ratio",
        "nvidia_smi_utilization_encoder_ratio",
        "nvidia_smi_utilization_decoder_ratio",
        "nvidia_smi_memory_used_bytes",
        "nvidia_smi_memory_total_bytes",
        "nvidia_smi_encoder_stats_session_count",
        "nvidia_smi_temperature_gpu",
        "nvidia_smi_power_draw_watts",
        "nvidia_smi_power_limit_watts",
        "nvidia_smi_enforced_power_limit_watts",
    };

    /// <summary>The families of one card, collected while the scrape is read.</summary>
    private sealed class Card(string id)
    {
        private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);
        private string? _name;
        private string? _driver;

        public string Id { get; } = id;

        public int Index { get; private set; } = int.MaxValue;

        public void Add(Sample sample)
        {
            if (sample.Name == "nvidia_smi_gpu_info")
            {
                _name = Blank(sample.Label("name"));
                _driver = Blank(sample.Label("driver_version"));
                Index = int.TryParse(sample.Label("index"), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : int.MaxValue;
            }
            else if (double.IsFinite(sample.Value))
            {
                _values[sample.Name] = sample.Value;
            }
        }

        public GpuReading ToReading()
        {
            return new GpuReading(
                Id,
                _name ?? "GPU",
                _driver,
                Percent("nvidia_smi_utilization_gpu_ratio"),
                Bytes("nvidia_smi_memory_used_bytes"),
                Bytes("nvidia_smi_memory_total_bytes"),
                Percent("nvidia_smi_utilization_encoder_ratio"),
                Percent("nvidia_smi_utilization_decoder_ratio"),
                _values.TryGetValue("nvidia_smi_encoder_stats_session_count", out var sessions) && sessions >= 0 ? (int)sessions : null,
                Number("nvidia_smi_temperature_gpu"),
                Number("nvidia_smi_power_draw_watts"),
                Number("nvidia_smi_power_limit_watts") ?? Number("nvidia_smi_enforced_power_limit_watts"));
        }

        private double? Number(string family) => _values.TryGetValue(family, out var value) ? Math.Round(value, 1) : null;

        /// <summary>A ratio of the exporter (0 to 1) as the percent it is shown as, kept in 0 to 100.</summary>
        private double? Percent(string family) => _values.TryGetValue(family, out var value) ? Math.Round(Math.Clamp(value * 100, 0, 100), 1) : null;

        private long? Bytes(string family) => _values.TryGetValue(family, out var value) && value >= 0 ? (long)value : null;

        private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
