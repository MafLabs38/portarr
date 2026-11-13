using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;

namespace Portarr.Providers.McpClients;

/// <summary>
/// The adapters of this library, in the order the pages show them. This is the one list there is: the dependency injection registration and the
/// tests both read it, and the Core holds none (D-113).
/// </summary>
public static class McpClientsProvider
{
    public static IReadOnlyList<IMcpClientRenderer> Renderers() =>
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

    public static IReadOnlyList<IMcpSecretStoreProvisioner> Provisioners() => [new VaultProvisioner(), new AzureKeyVaultProvisioner()];
}
