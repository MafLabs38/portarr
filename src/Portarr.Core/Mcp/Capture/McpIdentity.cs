using Portarr.Core.Catalog;

namespace Portarr.Core.Mcp.Capture;

/// <summary>
/// What makes two descriptions the same MCP server whatever they are called and whichever machine they were seen on (D-116): for a remote
/// server its address (scheme and host in lower case, no trailing slash, the query kept); for a local one run through <c>npx</c>, <c>uvx</c>
/// or alike, the package without its version; for any other program, the program and its arguments.
/// </summary>
public static class McpIdentity
{
    private static readonly string[] PackageRunners = ["npx", "uvx", "bunx", "pnpx"];

    public static string Of(CapturedServer server) => server.Transport == McpTransports.Http ? Remote(server.Url) : Local(server.Command, server.Args);

    public static string Of(McpServerDefinition server)
    {
        return server.IsHttp ? Remote(server.Http?.Url.OriginalString) : Local(server.Stdio?.Command, server.Stdio?.Args ?? []);
    }

    /// <summary>A short, stable key for a group of captured servers: the first 12 hex digits of the identity's SHA-256.</summary>
    public static string KeyOf(string identity) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..12];

    private static string Remote(string? address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var url))
        {
            return "http:" + (address ?? string.Empty).Trim().ToLowerInvariant();
        }

        var path = url.AbsolutePath.TrimEnd('/');
        return $"http:{url.Scheme.ToLowerInvariant()}://{url.Authority.ToLowerInvariant()}{path}{url.Query}";
    }

    private static string Local(string? command, IReadOnlyList<string> args)
    {
        var program = ProgramName(command);
        if (PackageRunners.Contains(program, StringComparer.OrdinalIgnoreCase)
            && args.FirstOrDefault(a => !a.StartsWith('-')) is { } package)
        {
            return "stdio:package:" + WithoutVersion(package);
        }

        return "stdio:" + string.Join(' ', new[] { (command ?? string.Empty).Trim() }.Concat(args));
    }

    /// <summary>The program without its folder and its Windows extension: <c>/usr/bin/npx</c> and <c>npx.cmd</c> are <c>npx</c>.</summary>
    private static string ProgramName(string? command)
    {
        var name = (command ?? string.Empty).Trim().Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var extension = new[] { ".cmd", ".exe", ".bat" }.FirstOrDefault(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        return extension is null ? name : name[..^extension.Length];
    }

    /// <summary><c>@scope/pkg@1.2.3</c> becomes <c>@scope/pkg</c>; <c>pkg@latest</c> becomes <c>pkg</c>; a leading <c>@</c> is a scope, not a version.</summary>
    private static string WithoutVersion(string package)
    {
        var at = package.LastIndexOf('@');
        return at > 0 ? package[..at] : package;
    }
}
