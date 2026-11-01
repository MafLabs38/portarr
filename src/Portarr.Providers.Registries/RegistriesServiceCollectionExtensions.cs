using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Maintenance;

namespace Portarr.Providers.Registries;

public static class RegistriesServiceCollectionExtensions
{
    /// <summary>Largest registry answer read (a page of 1000 tags, a manifest, an image configuration).</summary>
    private const int MaxResponseBytes = 4 * 1024 * 1024;

    public static IServiceCollection AddRegistriesProvider(this IServiceCollection services)
    {
        services.AddHttpClient(ContainerRegistryClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.MaxResponseContentBufferSize = MaxResponseBytes;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Portarr");
        })
        // Redirects are followed by the client itself (a few, to an https host, without the token); every connection,
        // the token realm's and a redirect's included, goes through the address policy (15-20).
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = RegistryAddressPolicy.ConnectAsync });
        services.AddSingleton<IImageRegistry, ContainerRegistryClient>();

        return services;
    }
}
