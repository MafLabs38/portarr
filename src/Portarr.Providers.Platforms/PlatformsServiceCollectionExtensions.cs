using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Net;
using Portarr.Core.Platforms;

namespace Portarr.Providers.Platforms;

public static class PlatformsServiceCollectionExtensions
{
    /// <summary>Largest answer read: the channels of K3s and the releases of a product are a few hundred kilobytes at most.</summary>
    private const int MaxResponseBytes = 2 * 1024 * 1024;

    public static IServiceCollection AddPlatformsProvider(this IServiceCollection services)
    {
        services.AddHttpClient(PlatformsHttp.ClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.MaxResponseContentBufferSize = MaxResponseBytes;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Portarr");
        })
        // Public services, read with GET: a redirect is not followed, and every connection goes through the address policy.
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = OutboundAddressPolicy.ConnectAsync });
        services.AddSingleton<IReleaseLineSource, K3sReleaseLineSource>();
        services.AddSingleton<ISupportLifecycleSource, EndOfLifeDateSource>();

        return services;
    }
}
