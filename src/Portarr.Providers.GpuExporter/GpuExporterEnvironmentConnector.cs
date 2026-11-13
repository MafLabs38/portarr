using Portarr.Core.Catalog;

namespace Portarr.Providers.GpuExporter;

/// <summary>The exporter of the graphics card of a single machine (D-120): its <c>/metrics</c> URL.</summary>
public sealed record GpuEndpoint(Uri Exporter);

/// <summary>The <c>nvidiaGpu:</c> section of an environment (D-120).</summary>
internal sealed class GpuDocument
{
    /// <summary>The metrics URL of the card's exporter, e.g. http://10.0.0.10:9835/metrics.</summary>
    public string? Exporter { get; set; }
}

/// <summary>
/// <c>nvidiaGpu:</c> names the NVIDIA GPU exporter of the card of a Docker host (D-120, D-105): an auxiliary section, for an environment that is
/// one machine with a runtime. The exporter has no credential: it is restricted by network, like node_exporter.
/// </summary>
public sealed class GpuExporterEnvironmentConnector : IEnvironmentConnector
{
    public const string SectionName = "nvidiaGpu";

    public string Section => SectionName;

    public EnvironmentConnectorResult? Parse(EnvironmentSectionContext context)
    {
        if (context.Read<GpuDocument>() is not { } document)
        {
            return null;
        }

        if (!context.SingleMachine || !context.HasRuntime)
        {
            context.Error("nvidiaGpu is for a docker environment: the card belongs to one machine.");
            return null;
        }

        if (CatalogValues.HttpUrl(document.Exporter) is not { } url)
        {
            context.Error("nvidiaGpu.exporter must be the exporter's metrics URL (e.g. http://10.0.0.10:9835/metrics), without credentials.");
            return null;
        }

        return new EnvironmentConnectorResult(new GpuEndpoint(url));
    }
}

public static class GpuExporterEnvironmentExtensions
{
    /// <summary>The GPU exporter this environment declares for its single machine, or null.</summary>
    public static GpuEndpoint? Gpu(this CatalogEnvironment environment)
    {
        return environment.Settings<GpuEndpoint>(GpuExporterEnvironmentConnector.SectionName);
    }
}
