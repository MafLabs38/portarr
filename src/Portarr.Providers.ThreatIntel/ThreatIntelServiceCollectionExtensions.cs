using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Maintenance;
using Portarr.Core.Net;

namespace Portarr.Providers.ThreatIntel;

public static class ThreatIntelServiceCollectionExtensions
{
    public static IServiceCollection AddThreatIntelProvider(this IServiceCollection services)
    {
        services.AddHttpClient(ThreatIntelHttp.ClientName, client =>
            {
                client.Timeout = ThreatIntelHttp.ReadTimeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Portarr");
            })
            // Public feeds, read with GET: the connector follows https redirects itself, and every connection goes through the address policy.
            // Nothing is decompressed on the way: the EPSS file is a gzip file the connector reads as one.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.None,
                ConnectCallback = OutboundAddressPolicy.ConnectAsync,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            });
        services.AddSingleton<IExploitIntelligence, KevEpssExploitIntelligence>();

        return services;
    }
}
