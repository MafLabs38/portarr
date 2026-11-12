namespace Portarr.Core.Catalog;

/// <summary>
/// An MCP server the operator uses (D-111): described once, installed by an assistant on any workstation. The
/// definition holds no secret: a header or a variable is a literal that is not a secret, or the name of a variable that
/// exists on the workstation (<see cref="McpValue"/>). <c>Scope</c> is where a client installs it by default.
/// <c>AppId</c> links the server to an app of the catalog. <c>CheckInterval</c> is null when the server is not checked
/// (a local process, or <c>check: false</c>, D-114).
/// </summary>
public sealed record McpServerDefinition(
    string Id,
    string Name,
    string Transport,
    string Scope,
    McpHttpSettings? Http = null,
    McpStdioSettings? Stdio = null,
    string? AppId = null,
    TimeSpan? CheckInterval = null,
    TimeSpan? CheckTimeout = null,
    string? Notes = null)
{
    public bool IsHttp => Transport == McpTransports.Http;

    /// <summary>Every header and environment value of the server, whichever its transport.</summary>
    public IEnumerable<McpValue> Values => (Http?.Headers.Values ?? []).Concat(Stdio?.Env.Values ?? []);

    /// <summary>
    /// A package run through <c>npx</c>, <c>uvx</c>, <c>bunx</c> or <c>pnpx</c> without a version: it changes under the operator's feet. The launcher
    /// may be wrapped in <c>cmd /c</c>, which is how a Windows machine is told to run <c>npx</c>.
    /// </summary>
    public bool RunsAnUnpinnedPackage
    {
        get
        {
            if (Stdio is null)
            {
                return false;
            }

            var launcher = Stdio.Command;
            var args = Stdio.Args;
            if (launcher.Equals("cmd", StringComparison.OrdinalIgnoreCase) || launcher.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Count < 2 || !args[0].Equals("/c", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                launcher = args[1];
                args = args.Skip(2).ToList();
            }

            if (launcher is not ("npx" or "npx.cmd" or "uvx" or "bunx" or "pnpx"))
            {
                return false;
            }

            var package = args.FirstOrDefault(a => !a.StartsWith('-'));
            if (package is null)
            {
                return false;
            }

            var at = package.LastIndexOf('@');
            return at <= 0 || package.EndsWith("@latest", StringComparison.Ordinal);
        }
    }

    /// <summary>The variables the workstation must provide for this server, sorted: the names behind its <c>{ env: NAME }</c> values.</summary>
    public IReadOnlyList<string> RequiredVariables() => Values.Where(v => v.IsEnv).Select(v => v.EnvName!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
}

/// <summary>A remote server: its address and the headers a client sends. <c>AllowInsecure</c> accepts a plain <c>http://</c> address (D-103).</summary>
public sealed record McpHttpSettings(Uri Url, IReadOnlyDictionary<string, McpValue> Headers, bool AllowInsecure);

/// <summary>A local process the client starts: a command, its arguments and the environment it gets.</summary>
public sealed record McpStdioSettings(string Command, IReadOnlyList<string> Args, IReadOnlyDictionary<string, McpValue> Env);

/// <summary>
/// A value of a header or of an environment: either a literal (never a secret, the parser refuses those) or the name of a
/// variable that holds it <b>on the workstation</b>. Portarr never reads that variable (D-111).
/// </summary>
public sealed record McpValue(string? Literal, string? EnvName, string? SecretKey = null)
{
    public static McpValue FromLiteral(string value) => new(value, null);

    public static McpValue FromEnv(string name, string? secretKey = null) => new(null, name, secretKey);

    public bool IsEnv => EnvName is not null;

    /// <summary>The key, in the operator's secret store, of the secret whose value the workstation puts in <see cref="EnvName"/> (D-116).</summary>
    public bool HasSecret => SecretKey is not null;
}

/// <summary>
/// The operator's secret store (D-116): where the secrets of the MCP servers live. Portarr holds no secret and reads none: it names the
/// store, and a secret by its key under <c>Prefix</c>; the workstation reads it with the store's own command, under the store's own access
/// control. <c>Address</c> is the store's address (Vault's <c>VAULT_ADDR</c>, an Azure Key Vault's URL).
/// </summary>
public sealed record McpSecretStore(string Kind, Uri Address, string Prefix);

/// <summary>The transports of an MCP server.</summary>
public static class McpTransports
{
    public const string Http = "http";
    public const string Stdio = "stdio";

    public static IReadOnlyList<string> All { get; } = [Http, Stdio];
}

/// <summary>Where a client keeps a server: for the user, for a project, or for this checkout only.</summary>
public static class McpScopes
{
    public const string User = "user";
    public const string Project = "project";
    public const string Local = "local";

    public static IReadOnlyList<string> All { get; } = [User, Project, Local];
}
