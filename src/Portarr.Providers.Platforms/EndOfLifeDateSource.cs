using System.Globalization;
using System.Net;
using System.Text.Json;
using Portarr.Core.Platforms;

namespace Portarr.Providers.Platforms;

/// <summary>
/// The support cycles of a product (D-119), from <c>endoflife.date</c>: one GET of <c>/api/v1/products/&lt;product&gt;/</c>, whose
/// <c>releases</c> carry the day active support ends (<c>eoasFrom</c>) and the day security fixes stop (<c>eolFrom</c>). A product
/// the site does not list answers 404, which is "unknown", not an error. The product is an identifier a machine reported
/// (<c>/etc/os-release</c>'s <c>ID</c>): it is refused unless it has the shape of one, so it can never reshape the URL.
/// </summary>
public sealed class EndOfLifeDateSource : ISupportLifecycleSource
{
    public static readonly Uri BaseUrl = new("https://endoflife.date/api/v1/products/");

    /// <summary>
    /// <c>/etc/os-release</c>'s <c>ID</c> of the distributions that endoflife.date names otherwise: this connector speaks its site's names,
    /// so Core only ever passes the identifier a machine reported.
    /// </summary>
    private static readonly Dictionary<string, string> Products = new(StringComparer.Ordinal)
    {
        ["rocky"] = "rocky-linux",
        ["amzn"] = "amazon-linux",
        ["ol"] = "oracle-linux",
        ["opensuse-leap"] = "opensuse",
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public EndOfLifeDateSource(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<SupportCycle>?> ReadAsync(string product, CancellationToken cancellationToken)
    {
        if (!PlatformReport.ProductPattern().IsMatch(product))
        {
            return null;
        }

        var site = Products.GetValueOrDefault(product, product);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PlatformsHttp.ReadTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUrl, site + Path.AltDirectorySeparatorChar));
        using var response = await _httpClientFactory.CreateClient(PlatformsHttp.ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = PlatformsHttp.Capped(await response.Content.ReadAsStreamAsync(timeout.Token));
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);

        if (!document.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The support dates answer has no list of releases.");
        }

        var cycles = new List<SupportCycle>();
        foreach (var release in releases.EnumerateArray())
        {
            if (PlatformsHttp.Text(release, "name") is { Length: > 0 } name)
            {
                cycles.Add(new SupportCycle(name, Date(release, "eoasFrom"), Date(release, "eolFrom")));
            }
        }

        return cycles;
    }

    /// <summary>A day written <c>yyyy-MM-dd</c>; null when there is none (null, a boolean, anything else).</summary>
    private static DateOnly? Date(JsonElement element, string name)
    {
        return PlatformsHttp.Text(element, name) is { } text && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }
}
