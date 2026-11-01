using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Portarr.Core.Maintenance;

namespace Portarr.Providers.Registries;

/// <summary>
/// Reads image tags anonymously (D-66): Docker Hub through its web API (tags sorted by last update, with their
/// date and digest, and outside the pull rate limit), every other registry (GHCR, lscr.io, Quay, gcr.io…)
/// through the OCI distribution API and its anonymous token flow. GET and HEAD only; nothing is pulled.
/// </summary>
public sealed partial class ContainerRegistryClient : IImageRegistry
{
    public const string HttpClientName = "registries";

    public static readonly Uri DockerHubApi = new("https://hub.docker.com/v2/");

    /// <summary>Docker Hub pages of 100 tags, newest first: the newest version is always in the first ones.</summary>
    private const int MaxHubPages = 3;

    /// <summary>OCI lists are not sorted: linuxserver images have thousands of tags.</summary>
    private const int MaxOciPages = 10;

    /// <summary>A blob is served by one redirect to a CDN; a chain longer than this is not followed.</summary>
    private const int MaxRedirects = 3;

    private static readonly string[] ManifestTypes =
    [
        "application/vnd.oci.image.index.v1+json",
        "application/vnd.docker.distribution.manifest.list.v2+json",
        "application/vnd.oci.image.manifest.v1+json",
        "application/vnd.docker.distribution.manifest.v2+json",
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> _tokens = new(StringComparer.Ordinal);

    public ContainerRegistryClient(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<RegistryTag>> ListTagsAsync(ImageReference image, CancellationToken cancellationToken)
    {
        return image.Registry == ImageReference.DockerHub
            ? await ListHubTagsAsync(image, cancellationToken)
            : await ListOciTagsAsync(image, cancellationToken);
    }

    public async Task<RegistryTag?> DescribeAsync(ImageReference image, string tag, CancellationToken cancellationToken)
    {
        if (image.Registry == ImageReference.DockerHub)
        {
            using var response = await SendAsync(HttpMethod.Get, new Uri(DockerHubApi, $"{HubPath(image)}/tags/{Uri.EscapeDataString(tag)}"), null, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            var hubTag = await ReadAsync(response, RegistryJsonContext.Default.HubTag, cancellationToken);
            return new RegistryTag(hubTag.Name, hubTag.LastUpdated, hubTag.Digest);
        }

        using var manifestResponse = await OciGetAsync(image, $"manifests/{Uri.EscapeDataString(tag)}", ManifestTypes, cancellationToken);
        if (manifestResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var digest = manifestResponse.Headers.TryGetValues("Docker-Content-Digest", out var values) ? values.FirstOrDefault() : null;
        var manifest = await ReadAsync(manifestResponse, RegistryJsonContext.Default.OciManifest, cancellationToken);
        return new RegistryTag(tag, await CreatedAsync(image, manifest, cancellationToken), digest);
    }

    private async Task<List<RegistryTag>> ListHubTagsAsync(ImageReference image, CancellationToken cancellationToken)
    {
        var tags = new List<RegistryTag>();
        Uri? next = new(DockerHubApi, $"{HubPath(image)}/tags?page_size=100&ordering=last_updated");
        for (var page = 0; next is not null && page < MaxHubPages; page++)
        {
            using var response = await SendAsync(HttpMethod.Get, next, null, cancellationToken);
            var body = await ReadAsync(response, RegistryJsonContext.Default.HubTagPage, cancellationToken);
            tags.AddRange((body.Results ?? []).Select(t => new RegistryTag(t.Name, t.LastUpdated, t.Digest)));
            next = Uri.TryCreate(body.Next, UriKind.Absolute, out var link) && link.Host == DockerHubApi.Host && link.Scheme == Uri.UriSchemeHttps ? link : null;
        }

        return tags;
    }

    private async Task<List<RegistryTag>> ListOciTagsAsync(ImageReference image, CancellationToken cancellationToken)
    {
        var tags = new List<RegistryTag>();
        string? next = "tags/list?n=1000";
        for (var page = 0; next is not null && page < MaxOciPages; page++)
        {
            using var response = await OciGetAsync(image, next, null, cancellationToken);
            var body = await ReadAsync(response, RegistryJsonContext.Default.OciTagList, cancellationToken);
            tags.AddRange((body.Tags ?? []).Select(t => new RegistryTag(t)));
            next = NextOciPage(response, image);
        }

        return tags;
    }

    /// <summary>The creation date of an image, from its configuration (linux/amd64 for an index): two more reads.</summary>
    private async Task<DateTimeOffset?> CreatedAsync(ImageReference image, OciManifest manifest, CancellationToken cancellationToken)
    {
        if (manifest.Manifests is { Count: > 0 } manifests)
        {
            var chosen = manifests.FirstOrDefault(m => m.Platform is { Os: "linux", Architecture: "amd64" }) ?? manifests[0];
            using var platformResponse = await OciGetAsync(image, $"manifests/{chosen.Digest}", ManifestTypes, cancellationToken);
            manifest = await ReadAsync(platformResponse, RegistryJsonContext.Default.OciManifest, cancellationToken);
        }

        if (manifest.Config?.Digest is not { } configDigest)
        {
            return null;
        }

        using var configResponse = await OciGetAsync(image, $"blobs/{configDigest}", null, cancellationToken);
        return (await ReadAsync(configResponse, RegistryJsonContext.Default.OciConfig, cancellationToken)).Created;
    }

    /// <summary>
    /// A GET on the registry's API with an anonymous pull token: the first call without one, and on a 401
    /// the token the challenge points at (<c>WWW-Authenticate: Bearer realm=…,service=…,scope=…</c>).
    /// </summary>
    private async Task<HttpResponseMessage> OciGetAsync(ImageReference image, string path, string[]? accept, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://{image.Registry}/v2/{image.Repository}/{path}");
        var key = image.RepositoryKey;
        var token = _tokens.TryGetValue(key, out var cached) && cached.Expires > _timeProvider.GetUtcNow() ? cached.Token : null;

        var response = await SendAsync(HttpMethod.Get, uri, request => Prepare(request, token, accept), cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // A 401 asks for a token: the first time, or again when the cached one was rejected (it expired early). Once.
        var challenge = response.Headers.WwwAuthenticate.FirstOrDefault(h => h.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase));
        response.Dispose();
        _tokens.TryRemove(key, out _);
        token = await TokenAsync(image, challenge?.Parameter, cancellationToken);
        _tokens[key] = (token, _timeProvider.GetUtcNow().AddMinutes(4));

        return await SendAsync(HttpMethod.Get, uri, request => Prepare(request, token, accept), cancellationToken);
    }

    private static void Prepare(HttpRequestMessage request, string? token, string[]? accept)
    {
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        foreach (var type in accept ?? [])
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(type));
        }
    }

    /// <summary>An anonymous pull token. The realm must be HTTPS; only the pull scope of this repository is asked.</summary>
    private async Task<string> TokenAsync(ImageReference image, string? challenge, CancellationToken cancellationToken)
    {
        var parameters = ChallengePattern().Matches(challenge ?? string.Empty)
            .ToDictionary(m => m.Groups["key"].Value.ToLowerInvariant(), m => m.Groups["value"].Value, StringComparer.Ordinal);
        if (!parameters.TryGetValue("realm", out var realmText) || !Uri.TryCreate(realmText, UriKind.Absolute, out var realm) || realm.Scheme != Uri.UriSchemeHttps)
        {
            throw new RegistryUnavailableException(RegistryUnavailableException.Refused);
        }

        var query = $"scope={Uri.EscapeDataString($"repository:{image.Repository}:pull")}"
            + (parameters.TryGetValue("service", out var service) ? $"&service={Uri.EscapeDataString(service)}" : string.Empty);
        var separator = string.IsNullOrEmpty(realm.Query) ? "?" : "&";
        using var response = await SendAsync(HttpMethod.Get, new Uri($"{realm}{separator}{query}"), null, cancellationToken);
        var body = await ReadAsync(response, RegistryJsonContext.Default.RegistryToken, cancellationToken);
        // Registries name the token `token` (Docker) or `access_token` (OAuth 2); an empty one is no token.
        var token = string.IsNullOrEmpty(body.Token) ? body.AccessToken : body.Token;
        return string.IsNullOrEmpty(token) ? throw new RegistryUnavailableException(RegistryUnavailableException.Refused) : token;
    }

    /// <summary>
    /// Sends the request. A redirect (a blob served from a CDN) is followed up to <see cref="MaxRedirects"/> times, only to
    /// an https address and without the token: it is not Portarr's to hand to another host.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, Action<HttpRequestMessage>? prepare, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var redirects = 0;
        while (true)
        {
            using var request = new HttpRequestMessage(method, uri);
            if (redirects == 0)
            {
                prepare?.Invoke(request);
            }

            var response = await client.SendAsync(request, cancellationToken);
            if (!IsRedirect(response.StatusCode) || redirects >= MaxRedirects)
            {
                return response;
            }

            var target = Absolute(uri, response.Headers.Location);
            response.Dispose();
            if (target is not { Scheme: "https" })
            {
                throw new RegistryUnavailableException(RegistryUnavailableException.Refused);
            }

            uri = target;
            redirects++;
        }
    }

    private static Uri? Absolute(Uri from, Uri? location) =>
        location is null || location.IsAbsoluteUri ? location : new Uri(from, location);

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>The body of a successful answer; a refused, missing or throttled repository becomes a reason code.</summary>
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                throw new RegistryUnavailableException(RegistryUnavailableException.NotFound);
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new RegistryUnavailableException(RegistryUnavailableException.Refused);
            case HttpStatusCode.TooManyRequests:
                throw new RegistryUnavailableException(RegistryUnavailableException.RateLimited);
        }

        response.EnsureSuccessStatusCode();
        try
        {
            return await response.Content.ReadFromJsonAsync(type, cancellationToken) ?? throw new InvalidDataException("Empty registry answer.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Unexpected registry answer.", exception);
        }
    }

    private static string? NextOciPage(HttpResponseMessage response, ImageReference image)
    {
        if (!response.Headers.TryGetValues("Link", out var links))
        {
            return null;
        }

        var match = NextLinkPattern().Match(string.Join(',', links));
        var prefix = $"/v2/{image.Repository}/";
        if (!match.Success)
        {
            return null;
        }

        var link = match.Groups["url"].Value;
        string? path = link;
        if (Uri.TryCreate(link, UriKind.Absolute, out var absolute))
        {
            // An absolute link is followed only on the registry itself.
            path = absolute.Host == image.Registry ? absolute.PathAndQuery : null;
        }

        return path is not null && path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : null;
    }

    private static string HubPath(ImageReference image)
    {
        var slash = image.Repository.IndexOf('/', StringComparison.Ordinal);
        return $"namespaces/{Uri.EscapeDataString(image.Repository[..slash])}/repositories/{Uri.EscapeDataString(image.Repository[(slash + 1)..])}";
    }

    [GeneratedRegex("(?<key>[A-Za-z]+)=\"(?<value>[^\"]*)\"")]
    private static partial Regex ChallengePattern();

    [GeneratedRegex("<(?<url>[^>]+)>;\\s*rel=\"next\"")]
    private static partial Regex NextLinkPattern();
}
