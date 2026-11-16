using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Portarr.Core.Net;

namespace Portarr.Core.Catalog.Editing;

/// <summary>
/// What a change to how a host is reached may not contain, whoever makes it (a person, an approval, an assistant): the
/// catalog never holds a secret (the settings name an environment variable), and an address Portarr will call must not be a
/// door to Portarr itself or to a cloud metadata service. Checked on the settings of <see cref="ProtectedSettings"/> and on the
/// links and the address that a check fetches (<see cref="ProtectedSettings.GuardedKeysOf"/>), on every address a change sets or changes (what an item already holds is not judged again): <c>http</c>, <c>https</c>,
/// <c>tcp</c> (a Docker proxy) or <c>unix</c> (a Docker socket), no <c>user:password@</c>, and an address literal that
/// <see cref="OutboundAddressPolicy"/> accepts (a LAN address does; loopback and link-local do not), nor the names that always mean
/// them (<c>localhost</c>, a cloud metadata name). Other host names are not resolved here: the catalog is edited offline, the
/// connectors confine the credential variables they accept, and for the protected settings an administrator's approval is the
/// control. The links and the address of an environment are not approved, so a name that resolves to a loopback or metadata address
/// is a known limit there (a connect-time check is the control, and is not added: R-144-103).
/// </summary>
public static partial class ConnectionGuardrails
{
    private static readonly string[] Schemes = ["http", "https", "tcp", "unix"];

    private static readonly string[] ForbiddenNames = ["localhost", "metadata", "metadata.google.internal", "instance-data", "instance-data.ec2.internal"];

    /// <summary>The problems with the protected settings of an item, empty when there is none.</summary>
    public static IReadOnlyList<string> Check(JsonObject? item, IReadOnlyList<string> keys)
    {
        var problems = new List<string>();
        foreach (var key in keys)
        {
            if (item?[key] is { } node)
            {
                if (key == ProtectedSettings.AddressKey)
                {
                    // An environment's address is a bare host (an app with no link of its own is probed there).
                    if (node is JsonValue host && host.TryGetValue<string>(out var text))
                    {
                        CheckHost(text.Trim(), key, problems);
                    }
                }
                else
                {
                    Walk(node, key, problems);
                }
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
                if (node is JsonValue value && value.TryGetValue<string>(out var text) && LooksLikeAddress(text.Trim()))
                {
                    CheckAddress(text.Trim(), path, problems);
                }

                break;
        }
    }

    /// <summary>A scheme followed by two slashes or two backslashes (.NET reads <c>http:\\host</c> as an address too); a Windows path is not one.</summary>
    private static bool LooksLikeAddress(string text) => AddressStart().IsMatch(text);

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:[/\\]{2}")]
    private static partial Regex AddressStart();

    private static void CheckAddress(string text, string path, List<string> problems)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            problems.Add($"{path}: '{text}' is not a valid address.");
            return;
        }

        if (!Schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"{path}: the scheme '{uri.Scheme}' is not accepted (http, https, tcp or unix).");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            problems.Add($"{path}: an address never carries a user or password; name an environment variable for the credential instead.");
        }

        if (!uri.Scheme.Equals("unix", StringComparison.OrdinalIgnoreCase))
        {
            CheckHost(uri.Host, path, problems);
        }
    }

    /// <summary>A host Portarr will call: not a name that always means this host or a metadata service, nor an address literal the outbound policy refuses.</summary>
    private static void CheckHost(string host, string path, List<string> problems)
    {
        if (IsForbiddenName(host))
        {
            problems.Add($"{path}: {host} always means this host or a metadata service: Portarr does not call it.");
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out var address) && !OutboundAddressPolicy.IsAllowed(address))
        {
            problems.Add($"{path}: {host} is a loopback, link-local or unspecified address: Portarr does not call it.");
        }
    }

    private static bool IsForbiddenName(string host)
    {
        var name = host.Trim('[', ']').TrimEnd('.');
        return ForbiddenNames.Contains(name, StringComparer.OrdinalIgnoreCase) || name.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }
}
