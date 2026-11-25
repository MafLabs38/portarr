using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Maintenance;
using Portarr.Core.Net;

namespace Portarr.Providers.Trivy;

public static class TrivyServiceCollectionExtensions
{
    /// <summary>Largest answer read: 200 images, each with its 50 worst findings.</summary>
    private const int MaxResponseBytes = 16 * 1024 * 1024;

    public static IServiceCollection AddTrivyProvider(this IServiceCollection services)
    {
        services.AddHttpClient(TrivyImageScanner.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(20);
                client.MaxResponseContentBufferSize = MaxResponseBytes;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Portarr");
            })
            // The secret goes to the address the catalog names, never to wherever a redirect points; a loopback or link-local address is no scanner.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = OutboundAddressPolicy.ConnectAsync, PooledConnectionLifetime = TimeSpan.FromMinutes(10) });
        services.AddSingleton<IImageScanner, TrivyImageScanner>();

        return services;
    }
}
