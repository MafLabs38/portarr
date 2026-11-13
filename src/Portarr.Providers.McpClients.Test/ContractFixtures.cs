using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.McpClients.Test;

// One class per adapter, each passing the contract of its capability (rule 8, D-113, D-116).
public sealed class ClaudeCodeRendererContractFixture : McpClientRendererContract
{
    protected override IMcpClientRenderer Renderer { get; } = new ClaudeCodeRenderer();
}

public sealed class ClaudeDesktopRendererContractFixture : McpClientRendererContract
{
    protected override IMcpClientRenderer Renderer { get; } = new ClaudeDesktopRenderer();
}

public sealed class CursorRendererContractFixture : McpClientRendererContract
{
    protected override IMcpClientRenderer Renderer { get; } = new CursorRenderer();
}

public sealed class VsCodeRendererContractFixture : McpClientRendererContract
{
    protected override IMcpClientRenderer Renderer { get; } = new VsCodeRenderer();
}

public sealed class WindsurfRendererContractFixture : McpClientRendererContract
{
    protected override IMcpClientRenderer Renderer { get; } = new WindsurfRenderer();
}

public sealed class GeminiCliRendererContractFixture : McpClientRendererContract
{
    protected override IMcpClientRenderer Renderer { get; } = new GeminiCliRenderer();
}

public sealed class CodexCliRendererContractFixture : McpClientRendererContract
{
    protected override IMcpClientRenderer Renderer { get; } = new CodexCliRenderer();
}

public sealed class OpenCodeRendererContractFixture : McpClientRendererContract
{
    protected override IMcpClientRenderer Renderer { get; } = new OpenCodeRenderer();
}

public sealed class ZedRendererContractFixture : McpClientRendererContract
{
    protected override IMcpClientRenderer Renderer { get; } = new ZedRenderer();
}

public sealed class VaultProvisionerContractFixture : McpSecretStoreProvisionerContract
{
    protected override IMcpSecretStoreProvisioner Provisioner { get; } = new VaultProvisioner();

    protected override string ValidPrefix => "kv/mcp";

    protected override string InvalidPrefix => "/kv/mcp/";

    protected override string ValidKey => "memory";

    protected override string InvalidKey => "a b";
}

public sealed class AzureKeyVaultProvisionerContractFixture : McpSecretStoreProvisionerContract
{
    protected override IMcpSecretStoreProvisioner Provisioner { get; } = new AzureKeyVaultProvisioner();

    protected override string ValidPrefix => "mcp-";

    protected override string InvalidPrefix => "mcp/";

    protected override string ValidKey => "memory";

    protected override string InvalidKey => "memory.key";
}
