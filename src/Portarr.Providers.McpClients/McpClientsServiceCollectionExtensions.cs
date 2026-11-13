using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;

namespace Portarr.Providers.McpClients;

public static class McpClientsServiceCollectionExtensions
{
    /// <summary>Registers the AI clients and the kinds of secret store this library knows, like a connector registers itself (D-36, D-113).</summary>
    public static IServiceCollection AddMcpClientsProvider(this IServiceCollection services)
    {
        foreach (var renderer in McpClientsProvider.Renderers())
        {
            services.AddSingleton(typeof(IMcpClientRenderer), renderer);
        }

        foreach (var provisioner in McpClientsProvider.Provisioners())
        {
            services.AddSingleton(typeof(IMcpSecretStoreProvisioner), provisioner);
        }

        return services;
    }
}
