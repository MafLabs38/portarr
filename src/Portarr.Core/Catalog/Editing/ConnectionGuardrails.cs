using System.Net;
using System.Text.Json.Nodes;
using Portarr.Core.Net;

namespace Portarr.Core.Catalog.Editing;

/// <summary>
/// What a change to how a host is reached may not contain, whoever makes it (a person, an approval, an assistant): the
/// catalog never holds a secret (the settings name an environment variable), and an address Portarr will call must not be a
/// door to Portarr itself or to a cloud metadata service. Checked on the settings of <see cref="ProtectedSettings"/> only,
/// on every address in them: <c>http</c>, <c>https</c> or <c>unix</c> (a Docker socket), no <c>user:password@</c>, and an
/// address literal that <see cref="OutboundAddressPolicy"/> accepts (a LAN address does; loopback and link-local do not). A host
/// name is not resolved here: the catalog is edited offline, and the connectors confine the credential variables they accept.
/// </summary>
public static class ConnectionGuardrails
{
    private static readonly string[] Schemes = ["http", "https", "unix"];

    /// <summary>The problems with the protected settings of an item, empty when there is none.</summary>
    public static IReadOnlyList<string> Check(JsonObject? item, IReadOnlyList<string> keys)
    {
        var problems = new List<string>();
        foreach (var key in keys)
        {
            if (item?[key] is { } node)
            {
                Walk(node, key, problems);
            }
        }

        return problems;
    }

    private static void Walk(JsonNode node, string path, List<string> problems)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    if (child is not null)
                    {
                        Walk(child, $"{path}.{name}", problems);
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } child)
                    {
                        Walk(child, $"{path}[{i}]", problems);
                    }
                }

                break;
            default:
                if (node is JsonValue value && value.TryGetValue<string>(out var text) && text.Contains("://", StringComparison.Ordinal))
                {
                    CheckAddress(text, path, problems);
                }

                break;
        }
    }

    private static void CheckAddress(string text, string path, List<string> problems)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            problems.Add($"{path}: '{text}' is not a valid address.");
            return;
        }

        if (!Schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"{path}: the scheme '{uri.Scheme}' is not accepted (http, https or unix).");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            problems.Add($"{path}: an address never carries a user or password; name an environment variable for the credential instead.");
        }

        if (!uri.Scheme.Equals("unix", StringComparison.OrdinalIgnoreCase) && IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && !OutboundAddressPolicy.IsAllowed(address))
        {
            problems.Add($"{path}: {uri.Host} is a loopback, link-local or unspecified address: Portarr does not call it.");
        }
    }
}
