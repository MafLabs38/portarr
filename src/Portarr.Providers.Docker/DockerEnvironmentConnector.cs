using Portarr.Core.Catalog;

namespace Portarr.Providers.Docker;

/// <summary>
/// Docker Engine API endpoint: <c>unix:///var/run/docker.sock</c> or <c>http://host:2375</c> (socket proxy).
/// </summary>
public sealed record DockerEndpoint(Uri Url);

/// <summary>The <c>docker:</c> section of an environment.</summary>
internal sealed class DockerDocument
{
    public string? Url { get; set; }
}

/// <summary>
/// <c>docker:</c> declares a Docker host, one machine whose containers are the workloads (D-105).
/// </summary>
public sealed class DockerEnvironmentConnector : IEnvironmentConnector
{
    public const string SectionName = "docker";

    public string Section => SectionName;

    public bool IsRuntime => true;

    public bool SingleMachine => true;

    public EnvironmentConnectorResult? Parse(EnvironmentSectionContext context)
    {
        if (context.Read<DockerDocument>() is not { } document)
        {
            return null;
        }

        if (!Uri.TryCreate(document.Url, UriKind.Absolute, out var url) || url.Scheme is not ("unix" or "http" or "https" or "tcp"))
        {
            context.Error("docker.url must be unix:///path/to/docker.sock or http(s)://host:port.");
            return null;
        }

        return new EnvironmentConnectorResult(new DockerEndpoint(url));
    }
}

public static class DockerEnvironmentExtensions
{
    /// <summary>The Docker endpoint this environment declares, or null.</summary>
    public static DockerEndpoint? Docker(this CatalogEnvironment environment)
    {
        return environment.Settings<DockerEndpoint>(DockerEnvironmentConnector.SectionName);
    }
}
