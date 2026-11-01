using Portarr.Core.Catalog;

namespace Portarr.Providers.NodeExporter;

/// <summary>
/// The node_exporter of a single machine (D-72): its <c>/metrics</c> URL, read every second. <c>TokenVariable</c> names the
/// environment variable holding a bearer token when something in front of it asks for one (the Portarr reader does, D-100).
/// <c>Interval</c> reads it less often than every second (D-107).
/// </summary>
public sealed record HostMetricsEndpoint(Uri NodeExporter, string? TokenVariable = null, TimeSpan? Interval = null);

/// <summary>The <c>hostMetrics:</c> section of an environment, which the <c>reader:</c> shortcut also stands for (D-102).</summary>
internal sealed class HostMetricsDocument
{
    /// <summary>node_exporter's metrics URL, e.g. http://10.0.0.10:9100/metrics.</summary>
    public string? NodeExporter { get; set; }

    /// <summary>The environment variable holding a bearer token, when something in front of node_exporter asks for one.</summary>
    public string? TokenEnv { get; set; }

    /// <summary>How often the agent is read when not every second: <c>5s</c>, <c>1m</c> (1 second to 5 minutes).</summary>
    public string? Interval { get; set; }
}

/// <summary>
/// <c>hostMetrics:</c> names the node_exporter of a Docker host (D-72, D-105): an auxiliary section, for an environment that is one
/// machine with a runtime. A cluster declares its nodes' exporters in its own section. A token variable is sent to the address
/// the catalog names, so it must be in the reader's namespace (D-102).
/// </summary>
public sealed class NodeExporterEnvironmentConnector : IEnvironmentConnector
{
    public const string SectionName = "hostMetrics";

    public string Section => SectionName;

    public EnvironmentConnectorResult? Parse(EnvironmentSectionContext context)
    {
        if (context.Read<HostMetricsDocument>() is not { } document)
        {
            return null;
        }

        if (!context.SingleMachine || !context.HasRuntime)
        {
            context.Error("hostMetrics is for a docker environment; a kubernetes environment declares kubernetes.nodeExporter.");
            return null;
        }

        if (CatalogValues.HttpUrl(document.NodeExporter) is not { } url)
        {
            context.Error("hostMetrics.nodeExporter must be node_exporter's metrics URL (e.g. http://10.0.0.10:9100/metrics), without credentials.");
            return null;
        }

        if (CatalogValues.ReaderTokenError("hostMetrics.tokenEnv", document.TokenEnv, out var variable) is { } error)
        {
            context.Error(error);
            return null;
        }

        TimeSpan? interval = null;
        if (document.Interval is not null && (interval = CatalogValues.ParseDuration(document.Interval, HostMetricsIntervals.Min, HostMetricsIntervals.Max)) is null)
        {
            context.Error("hostMetrics.interval must be a duration of 1 second to 5 minutes (e.g. 5s, 1m), or be omitted to read every second.");
            return null;
        }

        return new EnvironmentConnectorResult(new HostMetricsEndpoint(url, variable, interval));
    }
}

/// <summary>How often a host agent can be read: every second by default, slower when the catalog says so (D-107).</summary>
public static class HostMetricsIntervals
{
    public static readonly TimeSpan Min = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan Max = TimeSpan.FromMinutes(5);
}

public static class NodeExporterEnvironmentExtensions
{
    /// <summary>The node_exporter this environment declares for its single machine, or null.</summary>
    public static HostMetricsEndpoint? HostMetrics(this CatalogEnvironment environment)
    {
        return environment.Settings<HostMetricsEndpoint>(NodeExporterEnvironmentConnector.SectionName);
    }
}
