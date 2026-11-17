using System.Text.RegularExpressions;
using Portarr.Core.Catalog;

namespace Portarr.Providers.Proxmox;

/// <summary>
/// A Proxmox VE node read through its API (D-128). <c>TokenId</c> is the API token's id (<c>user@realm!name</c>, not a secret);
/// <c>TokenVariable</c> names the environment variable that holds the token's secret, never the secret itself. <c>Fingerprint</c>
/// is the SHA-256 of the node's certificate: Proxmox ships a self-signed one, which this pins instead of trusting anything.
/// </summary>
public sealed record ProxmoxEndpoint(Uri Url, string Node, string TokenId, string TokenVariable, string? Fingerprint);

/// <summary>The <c>proxmox:</c> section of an environment (D-128).</summary>
internal sealed class ProxmoxDocument
{
    public string? Url { get; set; }

    /// <summary>The node's name in Proxmox; the environment's id when omitted.</summary>
    public string? Node { get; set; }

    public string? TokenId { get; set; }

    /// <summary>Name of the environment variable that holds the token's secret (never the secret itself).</summary>
    public string? TokenEnv { get; set; }

    public string? Fingerprint { get; set; }
}

/// <summary>
/// <c>runtime: proxmox</c> declares a Proxmox VE node as one machine whose LXC containers are the workloads (D-128): they share the
/// host's kernel, so, like Docker containers on a Docker host, they are consumers of the machine and not machines of their own. The
/// VMs stay environments that name this one as their <c>host</c> (D-59). Read-only: the token is meant to hold the <c>PVEAuditor</c> role.
/// </summary>
public sealed partial class ProxmoxEnvironmentConnector : IEnvironmentConnector
{
    public const string SectionName = "proxmox";

    /// <summary>The namespace of the variables a catalog may name as an API token's secret.</summary>
    public const string VariablePrefix = "PORTARR_PROXMOX_";

    public string Section => SectionName;

    public bool IsRuntime => true;

    public bool SingleMachine => true;

    public EnvironmentConnectorResult? Parse(EnvironmentSectionContext context)
    {
        if (context.Read<ProxmoxDocument>() is not { } document)
        {
            return null;
        }

        if (!Uri.TryCreate(document.Url?.Trim(), UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length > 0)
        {
            context.Error("proxmox.url must be the node's https address (e.g. https://10.0.0.30:8006), without credentials: the API token travels in it.");
            return null;
        }

        var node = CatalogValues.Blank(document.Node) ?? context.EnvironmentId;
        if (!NodePattern().IsMatch(node))
        {
            context.Error("proxmox.node must be the node's name in Proxmox: letters, digits and dashes.");
            return null;
        }

        if (CatalogValues.Blank(document.TokenId) is not { } tokenId || !TokenIdPattern().IsMatch(tokenId))
        {
            context.Error("proxmox.tokenId must be the API token's id, user@realm!name (e.g. portarr@pve!readonly): it is not a secret.");
            return null;
        }

        var variableError = CatalogValues.SecretVariableError("proxmox.tokenEnv", document.TokenEnv, VariablePrefix, string.Empty, "token", context.AllowedSecretVariables, out var variable);
        if (variableError is not null || variable is null)
        {
            context.Error(variableError ?? $"proxmox.tokenEnv must name the environment variable that holds the token's secret (e.g. {VariablePrefix}TOKEN), never the secret itself.");
            return null;
        }

        var fingerprint = CatalogValues.Blank(document.Fingerprint);
        if (fingerprint is not null && !FingerprintPattern().IsMatch(fingerprint))
        {
            context.Error("proxmox.fingerprint must be the certificate's SHA-256, 32 hexadecimal pairs separated by colons (Proxmox shows it in Datacenter, Nodes, Certificates).");
            return null;
        }

        // The requests are built from this address: the API root, with its trailing slash, so that the last segment is never dropped.
        var root = new UriBuilder(url) { Path = "/api2/json/", Query = string.Empty, Fragment = string.Empty }.Uri;
        return new EnvironmentConnectorResult(new ProxmoxEndpoint(root, node, tokenId, variable, fingerprint?.ToUpperInvariant()));
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,62}$")]
    private static partial Regex NodePattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+@[A-Za-z0-9_.-]+![A-Za-z0-9_.-]+$")]
    private static partial Regex TokenIdPattern();

    [GeneratedRegex("^([0-9A-Fa-f]{2}:){31}[0-9A-Fa-f]{2}$")]
    private static partial Regex FingerprintPattern();
}

public static class ProxmoxEnvironmentExtensions
{
    /// <summary>The Proxmox node this environment declares, or null.</summary>
    public static ProxmoxEndpoint? Proxmox(this CatalogEnvironment environment)
    {
        return environment.Settings<ProxmoxEndpoint>(ProxmoxEnvironmentConnector.SectionName);
    }
}
