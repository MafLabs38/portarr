using Portarr.Core.Catalog;

namespace Portarr.Providers.Docker;

/// <summary>
/// Docker Engine API endpoint: <c>unix:///var/run/docker.sock</c> or <c>http://host:2375</c> (socket proxy). <c>TokenVariable</c>
/// names the environment variable holding a bearer token when something in front of Docker asks for one (the Portarr reader
/// does, D-100): never the token itself.
/// </summary>
public sealed record DockerEndpoint(Uri Url, string? TokenVariable = null);

/// <summary>The <c>docker:</c> section of an environment, which the <c>reader:</c> shortcut also stands for (D-102).</summary>
internal sealed class DockerDocument
{
    public string? Url { get; set; }

    /// <summary>Name of the environment variable holding a bearer token (never the token itself).</summary>
    public string? TokenEnv { get; set; }
}

/// <summary>
/// <c>docker:</c> declares a Docker host, one machine whose containers are the workloads (D-105). A token variable is sent to the
/// address the catalog names, so it must be in the reader's namespace (D-102).
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

        if (CatalogValues.ReaderTokenError("docker.tokenEnv", document.TokenEnv, out var variable) is { } error)
        {
            context.Error(error);
            return null;
        }

        return new EnvironmentConnectorResult(new DockerEndpoint(url, variable));
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
