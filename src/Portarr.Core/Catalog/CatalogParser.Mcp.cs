using System.Text.RegularExpressions;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Core.Catalog;

// The MCP servers of the catalog (D-111): what an assistant installs, and what Portarr checks (D-114). Like the
// knowledge of the catalog (D-84) a definition holds no secret: a header or a variable is a harmless literal or the name
// of a variable on the workstation, and the parser refuses what looks like a secret.
public static partial class CatalogParser
{
    /// <summary>How often a remote MCP server is checked unless the catalog says otherwise: a server is expected to be up, not watched by the second (D-114).</summary>
    public static readonly TimeSpan DefaultMcpCheckInterval = TimeSpan.FromMinutes(5);

    internal const int MaxMcpTextLength = 500;
    internal const int MaxMcpArguments = 50;
    internal const int MaxMcpValues = 50;

    private static readonly TimeSpan MinMcpTimeout = TimeSpan.FromSeconds(1);

    /// <summary>An HTTP header name: letters, digits and dashes.</summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]{0,63}\\z")]
    internal static partial Regex McpHeaderNamePattern();

    /// <summary>An environment variable name.</summary>
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}\\z")]
    internal static partial Regex McpVariableNamePattern();

    /// <summary>A header or variable whose value is a credential: it must name a variable, never hold the value.</summary>
    [GeneratedRegex("authorization|auth|cookie|key|token|secret|pass|pwd|credential|session", RegexOptions.IgnoreCase)]
    internal static partial Regex McpSensitiveNamePattern();

    /// <summary>Control characters, and the invisible ones that make a text read differently from what it is: C1 controls, zero-width and bidirectional marks.</summary>
    [GeneratedRegex(@"[\x00-\x1f\x7f-\x9f\u200b-\u200f\u202a-\u202e\u2060-\u2064\u2066-\u2069\ufeff]")]
    internal static partial Regex McpControlCharacterPattern();

    /// <summary>A command is a program, not a shell line: no operator, no substitution.</summary>
    [GeneratedRegex(@"[;&|<>`$]")]
    internal static partial Regex McpShellCharacterPattern();

    /// <summary>The key of a secret in a Vault-like store: one name.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z")]
    internal static partial Regex McpSecretKeyPattern();

    /// <summary>The name of an Azure Key Vault secret: letters, digits and dashes.</summary>
    [GeneratedRegex("^[A-Za-z0-9-]{1,63}\\z")]
    internal static partial Regex McpAzureSecretNamePattern();

    /// <summary>The path in Vault under which the MCP secrets live: <c>kv/mcp</c>.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*(/[A-Za-z0-9][A-Za-z0-9._-]*)*\z")]
    internal static partial Regex McpVaultPrefixPattern();

    /// <summary>What an Azure Key Vault secret's name starts with: <c>mcp-</c>.</summary>
    [GeneratedRegex("^[A-Za-z0-9-]{0,50}\\z")]
    internal static partial Regex McpAzurePrefixPattern();

    private sealed partial class Builder
    {
        private McpSecretStore? _secretStore;
        private bool _secretStoreDeclared;

        private List<McpServerDefinition> BuildMcpServers(List<McpServerDocument> documents, HashSet<string> appIds, McpSecretStore? secretStore, bool secretStoreDeclared)
        {
            _secretStore = secretStore;
            _secretStoreDeclared = secretStoreDeclared;
            var result = new List<McpServerDefinition>();

            foreach (var (document, index) in documents.Select((d, i) => (d, i)))
            {
                var id = RequireId(document.Id, $"mcpServers[{index}]", result.Select(s => s.Id));
                if (id is not null && BuildMcpServer(id, document, appIds) is { } server)
                {
                    result.Add(server);
                }
            }

            return result;
        }

        private List<WorkstationDefinition> BuildWorkstations(List<WorkstationDocument> documents, Dictionary<string, CatalogEnvironment> environments, List<McpServerDefinition> servers)
        {
            var result = new List<WorkstationDefinition>();

            foreach (var (document, index) in documents.Select((d, i) => (d, i)))
            {
                var id = RequireId(document.Id, $"workstations[{index}]", result.Select(w => w.Id));
                if (id is not null && BuildWorkstation(id, document, environments, servers) is { } workstation)
                {
                    result.Add(workstation);
                }
            }

            return result;
        }

        private WorkstationDefinition? BuildWorkstation(string id, WorkstationDocument document, Dictionary<string, CatalogEnvironment> environments, List<McpServerDefinition> servers)
        {
            var owner = $"Workstation '{id}'";
            var errors = _errors.Count;

            var name = CatalogValues.Blank(document.Name) ?? id;
            RejectSecret(owner, "name", name);

            var os = document.Os?.Trim().ToLowerInvariant();
            if (os is null || !McpOperatingSystems.All.Contains(os))
            {
                _errors.Add($"{owner}: os must be {string.Join(", ", McpOperatingSystems.All)} (paths and commands differ).");
            }

            var environmentId = CatalogValues.Blank(document.Environment);
            if (environmentId is not null && !environments.ContainsKey(environmentId))
            {
                _errors.Add($"{owner}: environment '{environmentId}' is not an environment of the catalog.");
            }

            var clients = BuildWorkstationClients(owner, "clients", document.Clients, McpClients.Ids);
            if (clients.Count == 0 && document.Clients is not { Count: > 0 })
            {
                _errors.Add($"{owner}: clients must list the assistants installed there ({string.Join(", ", McpClients.Ids)}).");
            }

            var wanted = BuildWanted(owner, document.Mcp ?? [], clients, servers);
            var notes = BuildNotes(owner, document.Notes);

            return _errors.Count > errors ? null : new WorkstationDefinition(id, name, os!, environmentId, clients, wanted, notes);
        }

        /// <summary>A list of client ids, each one known (and, when <paramref name="allowed"/> is given, one of them), without repeats.</summary>
        private List<string> BuildWorkstationClients(string owner, string key, List<string>? values, IReadOnlyList<string> allowed)
        {
            var result = new List<string>();
            foreach (var value in values ?? [])
            {
                var client = value?.Trim().ToLowerInvariant();
                if (client is null || !McpClients.Ids.Contains(client))
                {
                    _errors.Add($"{owner}: {key} has an unknown client '{Redact(value ?? string.Empty)}'. Clients: {string.Join(", ", McpClients.Ids)}.");
                }
                else if (!allowed.Contains(client))
                {
                    _errors.Add($"{owner}: {key} lists '{client}', which is not a client of this workstation ({string.Join(", ", allowed)}).");
                }
                else if (result.Contains(client))
                {
                    _errors.Add($"{owner}: {key} lists '{client}' twice.");
                }
                else
                {
                    result.Add(client);
                }
            }

            return result;
        }

        private List<WantedServer> BuildWanted(string owner, List<WantedDocument> documents, List<string> workstationClients, List<McpServerDefinition> servers)
        {
            var result = new List<WantedServer>();
            foreach (var (document, index) in documents.Select((d, i) => (d, i)))
            {
                var key = $"mcp[{index}]";
                var serverId = CatalogValues.Blank(document.Server);
                if (serverId is null || !servers.Any(s => s.Id == serverId))
                {
                    _errors.Add($"{owner}: {key} '{Redact(serverId ?? string.Empty)}' is not an MCP server of the catalog.");
                    continue;
                }

                if (result.Any(w => w.ServerId == serverId))
                {
                    _errors.Add($"{owner}: {key} lists '{serverId}' twice.");
                    continue;
                }

                var scope = CatalogValues.Blank(document.Scope)?.ToLowerInvariant();
                if (scope is not null && !McpScopes.All.Contains(scope))
                {
                    _errors.Add($"{owner}: {key}.scope must be {string.Join(", ", McpScopes.All)}.");
                }

                var clients = document.Clients is null ? workstationClients : BuildWorkstationClients(owner, $"{key}.clients", document.Clients, workstationClients);
                if (document.Clients is { Count: 0 })
                {
                    _errors.Add($"{owner}: {key}.clients must list at least one client, or be left out for all of them.");
                }

                result.Add(new WantedServer(serverId, clients, scope));
            }

            return result;
        }

        private McpServerDefinition? BuildMcpServer(string id, McpServerDocument document, HashSet<string> appIds)
        {
            var owner = $"MCP server '{id}'";
            var errors = _errors.Count;

            var name = CatalogValues.Blank(document.Name) ?? id;
            if (name.Length > 100)
            {
                _errors.Add($"{owner}: name must be at most 100 characters.");
            }

            RejectSecret(owner, "name", name);

            var transport = document.Transport?.Trim();
            if (transport is null || !McpTransports.All.Contains(transport))
            {
                _errors.Add($"{owner}: transport must be {string.Join(" or ", McpTransports.All)}.");
                return null;
            }

            var scope = CatalogValues.Blank(document.Scope) ?? McpScopes.User;
            if (!McpScopes.All.Contains(scope))
            {
                _errors.Add($"{owner}: scope must be {string.Join(", ", McpScopes.All)}.");
            }

            var appId = CatalogValues.Blank(document.App);
            if (appId is not null && !appIds.Contains(appId))
            {
                _errors.Add($"{owner}: app '{appId}' is not an app of the catalog.");
            }

            McpHttpSettings? http = null;
            McpStdioSettings? stdio = null;
            if (transport == McpTransports.Http)
            {
                if (document.Stdio is not null)
                {
                    _errors.Add($"{owner}: an http server declares http, not stdio.");
                }

                http = BuildMcpHttp(owner, document.Http);
            }
            else
            {
                if (document.Http is not null)
                {
                    _errors.Add($"{owner}: a stdio server declares stdio, not http.");
                }

                stdio = BuildMcpStdio(owner, document.Stdio);
            }

            var (interval, timeout) = BuildMcpCheck(owner, transport, document.Check);

            var notes = BuildNotes(owner, document.Notes);

            return _errors.Count > errors
                ? null
                : new McpServerDefinition(id, name, transport, scope, http, stdio, appId, interval, timeout, notes);
        }

        private McpHttpSettings? BuildMcpHttp(string owner, McpHttpDocument? document)
        {
            if (document is null || string.IsNullOrWhiteSpace(document.Url))
            {
                _errors.Add($"{owner}: http.url is required for an http server.");
                return null;
            }

            if (!Uri.TryCreate(document.Url.Trim(), UriKind.Absolute, out var url)
                || url.Scheme is not (HttpScheme or HttpsScheme)
                || url.UserInfo.Length > 0
                || url.Fragment.Length > 0
                || McpControlCharacterPattern().IsMatch(document.Url))
            {
                _errors.Add($"{owner}: http.url '{Redact(document.Url)}' must be an absolute http(s) URL without credentials or fragment.");
                return null;
            }

            var allowInsecure = document.AllowInsecure ?? false;
            if (url.Scheme == HttpScheme && !allowInsecure)
            {
                _errors.Add($"{owner}: http.url is plain http; use https, or set http.allowInsecure: true for a server on a network you trust.");
            }

            if (RejectSecret(owner, "http.url", document.Url))
            {
                return null;
            }

            var headers = BuildMcpValues(owner, "http.headers", document.Headers, McpHeaderNamePattern());
            return new McpHttpSettings(url, headers, allowInsecure);
        }

        private McpStdioSettings? BuildMcpStdio(string owner, McpStdioDocument? document)
        {
            var command = CatalogValues.Blank(document?.Command);
            if (document is null || command is null)
            {
                _errors.Add($"{owner}: stdio.command is required for a stdio server.");
                return null;
            }

            if (command.Length > 260 || McpControlCharacterPattern().IsMatch(command) || McpShellCharacterPattern().IsMatch(command))
            {
                _errors.Add($"{owner}: stdio.command must be a program (npx, uvx, a path), not a shell line: put the arguments in stdio.args.");
                return null;
            }

            var args = new List<string>();
            var arguments = document.Args ?? [];
            if (arguments.Count > MaxMcpArguments)
            {
                _errors.Add($"{owner}: stdio.args may hold at most {MaxMcpArguments} entries.");
            }

            foreach (var (argument, index) in arguments.Select((a, i) => (a, i)))
            {
                var key = $"stdio.args[{index}]";
                if (argument is null || argument.Length > MaxMcpTextLength || McpControlCharacterPattern().IsMatch(argument))
                {
                    _errors.Add($"{owner}: {key} must be a text of at most {MaxMcpTextLength} characters.");
                }
                else if (!RejectSecret(owner, key, argument))
                {
                    args.Add(argument);
                }
            }

            var env = BuildMcpValues(owner, "stdio.env", document.Env, McpVariableNamePattern());
            return new McpStdioSettings(command, args, env);
        }

        /// <summary>
        /// Headers or environment of a server. A value is a text that is not a secret, or <c>{ env: NAME }</c>; a name that
        /// suggests a credential (authorization, key, token…) takes only the second form, so no value ever sits in the catalog.
        /// </summary>
        private SortedDictionary<string, McpValue> BuildMcpValues(string owner, string section, Dictionary<string, McpValueDocument>? documents, Regex namePattern)
        {
            var result = new SortedDictionary<string, McpValue>(StringComparer.Ordinal);
            if (documents is null)
            {
                return result;
            }

            if (documents.Count > MaxMcpValues)
            {
                _errors.Add($"{owner}: {section} may hold at most {MaxMcpValues} entries.");
            }

            foreach (var (name, value) in documents)
            {
                var key = $"{section}.{name}";
                if (!namePattern.IsMatch(name))
                {
                    _errors.Add($"{owner}: {section} has an invalid name '{Redact(name)}'.");
                    continue;
                }

                if (BuildMcpValue(owner, key, name, value) is { } built)
                {
                    result[name] = built;
                }
            }

            return result;
        }

        private McpValue? BuildMcpValue(string owner, string key, string name, McpValueDocument? value)
        {
            if (value is null || value.IsEmpty)
            {
                _errors.Add($"{owner}: {key} needs a value, or {{ env: NAME }} to read it from a variable of the workstation.");
                return null;
            }

            if (value.Env is not null)
            {
                if (McpVariableNamePattern().IsMatch(value.Env))
                {
                    return BuildSecretKey(owner, key, value, out var secretKey) ? McpValue.FromEnv(value.Env, secretKey) : null;
                }

                _errors.Add($"{owner}: {key}.env must be the name of an environment variable (letters, digits and underscores).");
                return null;
            }

            if (value.Secret is not null)
            {
                _errors.Add($"{owner}: {key}.secret needs env: the variable that receives the secret on the workstation.");
                return null;
            }

            var literal = value.Literal!;
            if (string.IsNullOrWhiteSpace(literal) || literal.Length > MaxMcpTextLength || McpControlCharacterPattern().IsMatch(literal))
            {
                _errors.Add($"{owner}: {key} must be a text of at most {MaxMcpTextLength} characters, or {{ env: NAME }}.");
                return null;
            }

            if (McpSensitiveNamePattern().IsMatch(name))
            {
                _errors.Add($"{owner}: {key} looks like a credential: give the name of the variable that holds it ({{ env: NAME }}), never the value.");
                return null;
            }

            return RejectSecret(owner, key, literal) ? null : McpValue.FromLiteral(literal);
        }

        /// <summary>
        /// The key of the secret behind a variable (D-117): a name under the store's prefix, never a value. It needs a <c>secretStore</c>; false (and an
        /// error, unless the store itself was already refused) when it cannot be used.
        /// </summary>
        private bool BuildSecretKey(string owner, string key, McpValueDocument value, out string? secretKey)
        {
            secretKey = null;
            if (value.Secret is null)
            {
                return true;
            }

            if (_secretStore is null)
            {
                if (!_secretStoreDeclared)
                {
                    _errors.Add($"{owner}: {key}.secret names a key of the secret store, but the catalog declares no secretStore.");
                }

                return false;
            }

            var valid = _secretStore.Kind == McpSecretStoreKinds.AzureKeyVault ? McpAzureSecretNamePattern().IsMatch(value.Secret) : McpSecretKeyPattern().IsMatch(value.Secret);
            if (!valid)
            {
                _errors.Add($"{owner}: {key}.secret must be the key of the secret under the store's prefix ({(_secretStore.Kind == McpSecretStoreKinds.AzureKeyVault ? "letters, digits and dashes" : "letters, digits, dots, dashes and underscores")}), never its value.");
                return false;
            }

            secretKey = value.Secret;
            return true;
        }

        /// <summary>
        /// The operator's secret store (D-117): its kind, its address and the prefix under which the MCP secrets live. A name and a place, never a
        /// secret: Portarr holds none and reads none.
        /// </summary>
        private McpSecretStore? BuildSecretStore(SecretStoreDocument? document)
        {
            if (document is null)
            {
                return null;
            }

            const string owner = "secretStore";
            var errors = _errors.Count;

            var kind = document.Kind?.Trim().ToLowerInvariant();
            if (kind is null || !McpSecretStoreKinds.All.Contains(kind))
            {
                _errors.Add($"{owner}: kind must be {string.Join(" or ", McpSecretStoreKinds.All)}.");
            }

            var address = BuildSecretStoreAddress(owner, document);
            var prefix = CatalogValues.Blank(document.Prefix) ?? string.Empty;
            if (kind == McpSecretStoreKinds.Vault && !McpVaultPrefixPattern().IsMatch(prefix))
            {
                _errors.Add($"{owner}: prefix must be the path under which the MCP secrets live, such as kv/mcp (letters, digits, slashes, dots, dashes and underscores; no slash at either end).");
            }
            else if (kind == McpSecretStoreKinds.AzureKeyVault && !McpAzurePrefixPattern().IsMatch(prefix))
            {
                _errors.Add($"{owner}: prefix is put before the key to make the secret's name: at most 50 letters, digits and dashes (mcp-), or left out.");
            }

            return _errors.Count > errors || address is null ? null : new McpSecretStore(kind!, address, prefix);
        }

        private Uri? BuildSecretStoreAddress(string owner, SecretStoreDocument document)
        {
            if (!Uri.TryCreate(document.Address?.Trim(), UriKind.Absolute, out var address)
                || address.Scheme is not (HttpScheme or HttpsScheme)
                || address.UserInfo.Length > 0
                || address.Query.Length > 0
                || address.Fragment.Length > 0
                || address.AbsolutePath.Trim('/').Length > 0
                || address.OriginalString.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':' or '/' or '[' or ']')))
            {
                _errors.Add($"{owner}: address must be the address of the store (https://vault.homelab.example), without credentials, path, query or fragment.");
                return null;
            }

            if (address.Scheme == HttpScheme && document.AllowInsecure != true)
            {
                _errors.Add($"{owner}: address is plain http; use https, or set allowInsecure: true for a store on a network you trust.");
            }

            return RejectSecret(owner, "address", document.Address) ? null : address;
        }

        /// <summary>
        /// The check of a server (D-114): every 5 minutes by default, for a remote server only; <c>check: false</c> turns it off.
        /// Only <c>interval</c> and <c>timeout</c> apply: the probe is Portarr's own MCP request, not an app's HTTP check.
        /// </summary>
        private (TimeSpan? Interval, TimeSpan? Timeout) BuildMcpCheck(string owner, string transport, CheckDocument? document)
        {
            if (document?.InvalidScalar is not null)
            {
                _errors.Add($"{owner}: check must be false, true or a mapping (interval, timeout).");
                return (null, null);
            }

            var remote = transport == McpTransports.Http;
            if (document is null)
            {
                return remote ? (DefaultMcpCheckInterval, CheckDefinition.DefaultTimeout) : (null, null);
            }

            if (!document.Enabled)
            {
                return (null, null);
            }

            if (!remote)
            {
                _errors.Add($"{owner}: a stdio server is a local process Portarr cannot reach: set check: false, or leave check out.");
                return (null, null);
            }

            return document.Body is null ? (DefaultMcpCheckInterval, CheckDefinition.DefaultTimeout) : BuildMcpCheckBody(owner, document.Body);
        }

        private (TimeSpan? Interval, TimeSpan? Timeout) BuildMcpCheckBody(string owner, CheckBodyDocument body)
        {
            var errors = _errors.Count;

            if (body.Type is not null || body.Url is not null || body.Expect is not null || body.Keyword is not null || body.Critical is not null)
            {
                _errors.Add($"{owner}: check takes only interval and timeout (the probe is an MCP request to http.url).");
            }

            var interval = body.Interval is null ? DefaultMcpCheckInterval : CatalogValues.ParseDuration(body.Interval, MinInterval, MaxInterval);
            if (interval is null)
            {
                _errors.Add($"{owner}: check.interval must be between 10s and 24h (30s, 5m, 1h…).");
            }

            var timeout = body.Timeout is null ? CheckDefinition.DefaultTimeout : CatalogValues.ParseDuration(body.Timeout, MinMcpTimeout, MaxTimeout);
            if (timeout is null || timeout > (interval ?? DefaultMcpCheckInterval))
            {
                _errors.Add($"{owner}: check.timeout must be between 1s and 60s, and not longer than the interval.");
            }

            return _errors.Count > errors ? (null, null) : (interval, timeout);
        }
    }
}
