using System.Net;
using System.Text.RegularExpressions;

namespace Portarr.Core.Catalog;

// The knowledge of the catalog (D-84): notes, access, machines, network links and external providers. Descriptive
// only, for people and AI assistants; nothing here is a secret, and the parser refuses what looks like one.
public static partial class CatalogParser
{
    /// <summary>Longest note: a page of Markdown, not a wiki (a memory product holds the long form).</summary>
    public const int MaxNotesLength = 8000;

    /// <summary>A machine or node name: a host name, dots allowed.</summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,252}$")]
    private static partial Regex MachineNamePattern();

    /// <summary>A host name or an IP address.</summary>
    [GeneratedRegex("^[A-Za-z0-9.:_-]{1,253}$")]
    private static partial Regex HostPattern();

    /// <summary>A jump host: an SSH alias or <c>user@host</c>.</summary>
    [GeneratedRegex("^([A-Za-z0-9._-]{1,64}@)?[A-Za-z0-9.:_-]{1,253}$")]
    private static partial Regex JumpPattern();

    /// <summary>An SSH user or alias.</summary>
    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SshNamePattern();

    /// <summary>A key file name or path (<c>id_ed25519</c>, <c>~/.ssh/server-1</c>, <c>C:\keys\server-1</c>).</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_~./\\:-]{1,255}$")]
    private static partial Regex KeyFilePattern();

    private sealed partial class Builder
    {
        private string? BuildNotes(string owner, string? notes, string key = "notes")
        {
            if (string.IsNullOrWhiteSpace(notes))
            {
                return null;
            }

            if (notes.Length > MaxNotesLength)
            {
                _errors.Add($"{owner}: {key} must be at most {MaxNotesLength} characters.");
                return null;
            }

            return RejectSecret(owner, key, notes) ? null : notes.Trim();
        }

        /// <summary>True (and an error) when the value looks like a secret; the value itself is never echoed.</summary>
        private bool RejectSecret(string owner, string key, string? value)
        {
            if (value is null || SecretScanner.Find(value) is not { } reason)
            {
                return false;
            }

            _errors.Add($"{owner}: {key} looks like it contains a secret ({reason}); the catalog describes, it never holds credentials: name the key file or the variable instead.");
            return true;
        }

        private MachineAccess? BuildAccess(string owner, string key, AccessDocument? document)
        {
            if (document is null)
            {
                return null;
            }

            var ssh = BuildSsh(owner, $"{key}.ssh", document.Ssh);
            var consoles = new List<AccessConsole>();
            foreach (var console in document.Consoles ?? [])
            {
                if (!Uri.TryCreate(console.Href, UriKind.Absolute, out var href) || href.Scheme is not ("http" or "https") || href.UserInfo.Length > 0)
                {
                    _errors.Add($"{owner}: {key}.consoles '{Redact(console.Href)}' must be an absolute http(s) URL without credentials.");
                    continue;
                }

                if (!RejectSecret(owner, $"{key}.consoles", console.Href))
                {
                    consoles.Add(new AccessConsole(CatalogValues.Blank(console.Label) ?? href.Host, href));
                }
            }

            var notes = BuildNotes(owner, document.Notes, $"{key}.notes");
            if (document.Ssh is null && (document.Consoles ?? []).Count == 0 && string.IsNullOrWhiteSpace(document.Notes))
            {
                _errors.Add($"{owner}: {key} must say how to reach the machine: ssh, consoles or notes.");
                return null;
            }

            return new MachineAccess(ssh, consoles, notes);
        }

        private SshAccess? BuildSsh(string owner, string key, SshDocument? document)
        {
            if (document is null)
            {
                return null;
            }

            // Every field is checked, so one pass reports every mistake.
            var aliasValid = Check(document.Alias, SshNamePattern(), owner, $"{key}.alias", "an alias of your SSH config");
            var hostValid = Check(document.Host, HostPattern(), owner, $"{key}.host", "a host name or an IP address");
            var userValid = Check(document.User, SshNamePattern(), owner, $"{key}.user", "a user name");
            var jumpValid = Check(document.Jump, JumpPattern(), owner, $"{key}.jump", "a jump host (an alias or user@host)");
            var keyValid = Check(document.Key, KeyFilePattern(), owner, $"{key}.key", "the name or path of the key file (e.g. ~/.ssh/id_ed25519), never the key itself");
            var valid = aliasValid && hostValid && userValid && jumpValid && keyValid;
            if (document.Port is < 1 or > 65535)
            {
                _errors.Add($"{owner}: {key}.port must be between 1 and 65535.");
                valid = false;
            }

            if (document.Alias is null && document.Host is null)
            {
                _errors.Add($"{owner}: {key} needs a host or an alias.");
                valid = false;
            }

            if (!valid || RejectSecret(owner, $"{key}.key", document.Key))
            {
                return null;
            }

            return new SshAccess(CatalogValues.Blank(document.Host), document.Port, CatalogValues.Blank(document.User), CatalogValues.Blank(document.Key), CatalogValues.Blank(document.Jump), CatalogValues.Blank(document.Alias));
        }

        private bool Check(string? value, Regex pattern, string owner, string key, string expected)
        {
            if (value is null || pattern.IsMatch(value.Trim()))
            {
                return true;
            }

            _errors.Add($"{owner}: {key} must be {expected}.");
            return false;
        }

        private List<MachineKnowledge>? BuildMachines(string environmentId, List<MachineDocument>? documents)
        {
            if (documents is null || documents.Count == 0)
            {
                return null;
            }

            var result = new List<MachineKnowledge>();
            foreach (var document in documents)
            {
                if (document.Name is null || !MachineNamePattern().IsMatch(document.Name))
                {
                    _errors.Add($"Environment '{environmentId}': machines need a name (the host or node name, e.g. node-1).");
                    continue;
                }

                if (result.Exists(m => m.Name == document.Name))
                {
                    _errors.Add($"Environment '{environmentId}': machine '{document.Name}' is listed twice.");
                    continue;
                }

                var owner = $"Environment '{environmentId}': machine '{document.Name}'";
                result.Add(new MachineKnowledge(document.Name, BuildNotes(owner, document.Notes), BuildAccess(owner, "access", document.Access)));
            }

            return result;
        }

        private List<CatalogLink> BuildLinks(List<LinkDocument> documents, List<CatalogEnvironment> environments)
        {
            var result = new List<CatalogLink>();
            var machines = KnownMachines(environments);

            foreach (var (document, index) in documents.Select((d, i) => (d, i)))
            {
                var id = RequireId(document.Id, $"links[{index}]", result.Select(l => l.Id));
                if (id is null)
                {
                    continue;
                }

                var owner = $"Link '{id}'";
                var from = ResolveEnd(owner, "from", document.From, environments, machines);
                var to = ResolveEnd(owner, "to", document.To, environments, machines);
                if (from is not null && from == to)
                {
                    _errors.Add($"{owner}: from and to must be two different environments or machines.");
                }

                var kind = document.Kind?.Trim();
                if (kind is null || !LinkKinds.All.Contains(kind))
                {
                    _errors.Add($"{owner}: kind must be one of {string.Join(", ", LinkKinds.All)}.");
                }

                var network = CatalogValues.Blank(document.Network);
                if (network is not null && !IPNetwork.TryParse(network, out _))
                {
                    _errors.Add($"{owner}: network must be an address range in CIDR notation (e.g. 10.0.0.0/24).");
                }

                var name = CatalogValues.Blank(document.Name);
                RejectSecret(owner, "name", name);
                result.Add(new CatalogLink(id, from ?? string.Empty, to ?? string.Empty, kind ?? string.Empty, name, network, BuildNotes(owner, document.Notes)));
            }

            return result;
        }

        /// <summary>Machine name → the environments that know it (their <c>machines</c> and <c>kubernetes.nodeHosts</c>).</summary>
        private static Dictionary<string, List<string>> KnownMachines(List<CatalogEnvironment> environments)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var environment in environments)
            {
                var names = (environment.Machines ?? []).Select(m => m.Name).Concat(environment.Kubernetes?.NodeHosts?.Keys ?? []).Distinct(StringComparer.Ordinal);
                foreach (var name in names)
                {
                    if (!result.TryGetValue(name, out var owners))
                    {
                        result[name] = owners = [];
                    }

                    owners.Add(environment.Id);
                }
            }

            return result;
        }

        private string? ResolveEnd(string owner, string key, string? value, List<CatalogEnvironment> environments, Dictionary<string, List<string>> machines)
        {
            var reference = CatalogValues.Blank(value);
            if (reference is null)
            {
                _errors.Add($"{owner}: {key} is required: an environment id, a machine name or environment/machine.");
                return null;
            }

            var slash = reference.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0)
            {
                var environment = reference[..slash];
                var machine = reference[(slash + 1)..];
                if (machines.TryGetValue(machine, out var owners) && owners.Contains(environment))
                {
                    return reference;
                }

                _errors.Add($"{owner}: {key} '{reference}' is not a machine of an environment of the catalog (declare it in that environment's machines).");
                return null;
            }

            if (environments.Exists(e => e.Id == reference))
            {
                return reference;
            }

            if (!machines.TryGetValue(reference, out var known))
            {
                _errors.Add($"{owner}: {key} '{reference}' is neither an environment nor a machine of the catalog (declare machines under their environment).");
                return null;
            }

            if (known.Count > 1)
            {
                _errors.Add($"{owner}: {key} '{reference}' is a machine of several environments ({string.Join(", ", known)}); write environment/machine.");
                return null;
            }

            return reference;
        }

        private List<CatalogProvider> BuildProviders(List<ProviderDocument> documents, Dictionary<string, CatalogEnvironment> environments, HashSet<string> appIds)
        {
            var result = new List<CatalogProvider>();

            foreach (var (document, index) in documents.Select((d, i) => (d, i)))
            {
                var id = RequireId(document.Id, $"providers[{index}]", result.Select(p => p.Id));
                if (id is null)
                {
                    continue;
                }

                var owner = $"Provider '{id}'";
                var kind = document.Kind?.Trim();
                if (kind is null || !ProviderKinds.All.Contains(kind))
                {
                    _errors.Add($"{owner}: kind must be one of {string.Join(", ", ProviderKinds.All)}.");
                }

                Uri? console = null;
                if (document.Console is not null
                    && (!Uri.TryCreate(document.Console, UriKind.Absolute, out console) || console.Scheme is not ("http" or "https") || console.UserInfo.Length > 0))
                {
                    _errors.Add($"{owner}: console '{Redact(document.Console)}' must be an absolute http(s) URL without credentials.");
                    console = null;
                }

                foreach (var user in (document.UsedBy ?? []).Where(u => !appIds.Contains(u) && !environments.ContainsKey(u)))
                {
                    _errors.Add($"{owner}: usedBy '{user}' is neither an app nor an environment of the catalog.");
                }

                var account = CatalogValues.Blank(document.Account);
                if (account is { Length: > 200 })
                {
                    _errors.Add($"{owner}: account must be at most 200 characters (a name or an e-mail hint).");
                }

                RejectSecret(owner, "account", account);
                RejectSecret(owner, "console", document.Console);
                RejectSecret(owner, "name", document.Name);
                result.Add(new CatalogProvider(id, CatalogValues.Blank(document.Name) ?? id, kind ?? string.Empty, console, account, document.UsedBy ?? [], BuildNotes(owner, document.Notes)));
            }

            return result;
        }
    }
}

/// <summary>
/// Spots values that look like credentials (D-84): private keys and certificates, key material, well-known token
/// formats, <c>password=…</c> assignments, credentials in URLs and long random strings. A heuristic that errs on
/// the side of refusing: the operator names the key file or the variable instead.
/// </summary>
public static partial class SecretScanner
{
    private const int RandomWordLength = 40;
    private const int HexWordLength = 56;

    [GeneratedRegex("-----BEGIN [A-Z ]+-----|PRIVATE KEY")]
    private static partial Regex PemPattern();

    [GeneratedRegex(@"\b(ssh-(rsa|ed25519|dss)|ecdsa-sha2-[a-z0-9]+|sk-ssh-[a-z0-9@.-]+) AAAA")]
    private static partial Regex KeyMaterialPattern();

    [GeneratedRegex(@"\b(gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|glpat-[A-Za-z0-9_-]{20,}|xox[abprs]-[A-Za-z0-9-]{10,}|AKIA[0-9A-Z]{16}|sk-[A-Za-z0-9_-]{20,}|AIza[0-9A-Za-z_-]{30,}|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,})")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"\b(password|passwd|pwd|secret|api[_-]?key|access[_-]?key|token)\s*[:=]\s*\S", RegexOptions.IgnoreCase)]
    private static partial Regex AssignmentPattern();

    [GeneratedRegex(@"://[^/\s:@]+:[^/\s@]+@")]
    private static partial Regex UrlCredentialsPattern();

    [GeneratedRegex(@"[A-Za-z0-9+=_-]+")]
    private static partial Regex WordPattern();

    /// <summary>Why the value looks like a secret, or null. Never includes the value.</summary>
    public static string? Find(string value)
    {
        if (PemPattern().IsMatch(value))
        {
            return "a PEM block or a private key";
        }

        if (KeyMaterialPattern().IsMatch(value))
        {
            return "SSH key material";
        }

        if (TokenPattern().IsMatch(value))
        {
            return "a token format";
        }

        if (AssignmentPattern().IsMatch(value))
        {
            return "a password, secret or token assignment";
        }

        if (UrlCredentialsPattern().IsMatch(value))
        {
            return "credentials in a URL";
        }

        return WordPattern().Matches(value).Any(m => LooksRandom(m.Value)) ? "a long random string" : null;
    }

    private static bool LooksRandom(string word)
    {
        if (word.Length >= HexWordLength && word.All(Uri.IsHexDigit))
        {
            return true;
        }

        return word.Length >= RandomWordLength && word.Any(char.IsUpper) && word.Any(char.IsLower) && word.Any(char.IsDigit);
    }
}
