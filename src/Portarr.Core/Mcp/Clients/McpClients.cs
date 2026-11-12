namespace Portarr.Core.Mcp.Clients;

/// <summary>The clients Portarr can render a server for (D-113), by their id in the catalog.</summary>
public static class McpClients
{
    public static IReadOnlyList<IMcpClientRenderer> All { get; } =
    [
        new ClaudeCodeRenderer(),
        new ClaudeDesktopRenderer(),
        new CursorRenderer(),
        new VsCodeRenderer(),
        new WindsurfRenderer(),
        new GeminiCliRenderer(),
        new CodexCliRenderer(),
        new OpenCodeRenderer(),
        new ZedRenderer(),
    ];

    public static IReadOnlyList<string> Ids { get; } = All.Select(c => c.Id).ToList();

    public static IMcpClientRenderer? Find(string? id) => All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
}
