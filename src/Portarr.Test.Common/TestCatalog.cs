using Portarr.Core.Catalog;

namespace Portarr.Test.Common;

/// <summary>
/// Reads a section into a plain dictionary and hands it back as the settings: a connector for the tests of Core, which never look
/// inside a connector's settings (D-105) but may want to see what the section held.
/// </summary>
public sealed class RawSectionConnector : IEnvironmentConnector
{
    public RawSectionConnector(string section, bool isRuntime = false, bool singleMachine = true)
    {
        Section = section;
        IsRuntime = isRuntime;
        SingleMachine = singleMachine;
    }

    public string Section { get; }

    public bool IsRuntime { get; }

    public bool SingleMachine { get; }

    public EnvironmentConnectorResult? Parse(EnvironmentSectionContext context)
    {
        return context.Read<Dictionary<string, string>>() is { } values ? new EnvironmentConnectorResult(values) : null;
    }
}

/// <summary>
/// A cluster for the tests of Core: a runtime that is many machines, which gives the hosts of its nodes (<c>nodeHosts</c>) like the real
/// connector does (D-59, D-105) and accepts the rest of its section without looking at it.
/// </summary>
public sealed class StandInKubernetesConnector : IEnvironmentConnector
{
    private sealed class Document
    {
        public string? Server { get; set; }

        public string? TokenEnv { get; set; }

        public string? CaFile { get; set; }

        public bool AllowInsecure { get; set; }

        public Dictionary<string, string>? NodeHosts { get; set; }

        public Dictionary<string, object>? NodeExporter { get; set; }
    }

    public string Section => "kubernetes";

    public bool IsRuntime => true;

    public bool SingleMachine => false;

    public WorkloadMatchFields MatchFields => WorkloadMatchFields.All;

    public EnvironmentConnectorResult? Parse(EnvironmentSectionContext context)
    {
        return context.Read<Document>() is { } document ? new EnvironmentConnectorResult(new object(), document.NodeHosts is { Count: > 0 } ? document.NodeHosts : null) : null;
    }
}

/// <summary>A catalog read with stand-ins for the connector libraries Core does not reference (D-105).</summary>
public static class TestCatalog
{
    /// <summary>Docker (a runtime, one machine), Cloudflare and Kubernetes (runtimes, not machines), Backrest and the host agent (auxiliary).</summary>
    public static IReadOnlyList<IEnvironmentConnector> Connectors { get; } =
    [
        new RawSectionConnector("docker", isRuntime: true),
        new StubEnvironmentConnector("cloudflare", isRuntime: true, singleMachine: false),
        new StubEnvironmentConnector("backrest"),
        new RawSectionConnector("hostMetrics"),
        new StandInKubernetesConnector(),
    ];

    public static CatalogParseResult Parse(string yaml, params IEnvironmentConnector[] more)
    {
        return CatalogParser.Parse(yaml, [.. Connectors, .. more]);
    }
}
