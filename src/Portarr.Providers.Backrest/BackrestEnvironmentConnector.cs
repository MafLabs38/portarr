using Portarr.Core.Catalog;

namespace Portarr.Providers.Backrest;

/// <summary>
/// A Backrest instance (D-80). The credentials are read from the environment variables the catalog names, never
/// written in it; a Backrest without authentication needs neither.
/// </summary>
public sealed record BackrestEndpoint(Uri Url, string? UserVariable, string? PasswordVariable);

/// <summary>The <c>backrest:</c> section of an environment (D-80); the credentials are variable names.</summary>
internal sealed class BackrestDocument
{
    public string? Url { get; set; }

    public string? UserEnv { get; set; }

    public string? PasswordEnv { get; set; }
}

/// <summary>
/// <c>backrest:</c> names the Backrest of a single machine (a Docker host or a launcher-only environment): its URL and the
/// variables holding the basic-auth credentials, never the credentials (D-80, D-105).
/// </summary>
public sealed class BackrestEnvironmentConnector : IEnvironmentConnector
{
    public const string SectionName = "backrest";

    public string Section => SectionName;

    public EnvironmentConnectorResult? Parse(EnvironmentSectionContext context)
    {
        if (context.Read<BackrestDocument>() is not { } document)
        {
            return null;
        }

        if (!context.SingleMachine)
        {
            context.Error($"Environment '{context.EnvironmentId}': backrest runs on a docker host (or an environment without connector), not on a kubernetes or cloudflare environment.");
            return null;
        }

        if (CatalogValues.HttpUrl(document.Url) is not { } url)
        {
            context.Error($"Environment '{context.EnvironmentId}': backrest.url must be Backrest's address (e.g. http://10.0.0.10:9898), without credentials.");
            return null;
        }

        var user = CatalogValues.Blank(document.UserEnv);
        var password = CatalogValues.Blank(document.PasswordEnv);
        if ((user is null) != (password is null) || (user is not null && !CatalogValues.IsVariableName(user)) || (password is not null && !CatalogValues.IsVariableName(password)))
        {
            context.Error($"Environment '{context.EnvironmentId}': backrest.userEnv and passwordEnv must both name an environment variable (never the credentials themselves), or both be omitted when Backrest has no authentication.");
            return null;
        }

        return new EnvironmentConnectorResult(new BackrestEndpoint(url, user, password));
    }
}

public static class BackrestEnvironmentExtensions
{
    /// <summary>The Backrest this environment declares, or null.</summary>
    public static BackrestEndpoint? Backrest(this CatalogEnvironment environment)
    {
        return environment.Settings<BackrestEndpoint>(BackrestEnvironmentConnector.SectionName);
    }
}
