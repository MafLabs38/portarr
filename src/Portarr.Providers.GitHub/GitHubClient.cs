using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Portarr.Providers.GitHub;

/// <summary>An answer of the GitHub API: its status, body, the next page (a relative path) and the error message.</summary>
internal sealed record GitHubResponse(HttpStatusCode Status, byte[] Body, string? Next, string? Message)
{
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    public T? Read<T>(JsonTypeInfo<T> type) => IsSuccess ? JsonSerializer.Deserialize(Body, type) : default;
}

/// <summary>
/// GET requests to the GitHub REST API with conditional requests: every answer is kept with its ETag and asked
/// again with <c>If-None-Match</c>; GitHub answers 304, which does not count against the rate limit, when nothing
/// changed. Below <see cref="RateLimitReserve"/> remaining requests, calls stop until the window resets.
/// </summary>
internal sealed partial class GitHubClient
{
    public const string ApiVersion = "2022-11-28";

    /// <summary>Requests left untouched in each rate-limit window, for the operator's own tools.</summary>
    public const int RateLimitReserve = 250;

    /// <summary>Cached answers kept; a homelab scan needs a few hundred.</summary>
    private const int MaxCachedAnswers = 4000;

    /// <summary>... and the bytes they may hold together: a count alone would let 4000 large answers fill the memory.</summary>
    private const long MaxCachedBytes = 64L * 1024 * 1024;

    private readonly ConcurrentDictionary<string, CachedAnswer> _cache = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private long _cachedBytes;
    private long _remaining = long.MaxValue;
    private long _resetAt;

    public GitHubClient(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async Task<GitHubResponse> GetAsync(HttpClient client, string token, string path, CancellationToken cancellationToken)
    {
        if (Interlocked.Read(ref _remaining) < RateLimitReserve && _timeProvider.GetUtcNow().ToUnixTimeSeconds() < Interlocked.Read(ref _resetAt))
        {
            throw new HttpRequestException("GitHub rate limit reserve reached.", null, HttpStatusCode.TooManyRequests);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        var key = CacheKey(token, path);
        if (_cache.TryGetValue(key, out var cached))
        {
            request.Headers.IfNoneMatch.Add(cached.ETag);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        TrackRateLimit(response);

        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            return cached.Response;
        }

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var answer = new GitHubResponse(response.StatusCode, body, NextPage(response, client.BaseAddress), response.IsSuccessStatusCode ? null : ErrorMessage(body));

        if (response.Headers.ETag is { } etag && answer.IsSuccess)
        {
            if (_cache.Count >= MaxCachedAnswers || Interlocked.Read(ref _cachedBytes) + body.Length > MaxCachedBytes)
            {
                _cache.Clear();
                Interlocked.Exchange(ref _cachedBytes, 0);
            }

            if (_cache.TryGetValue(key, out var replaced))
            {
                Interlocked.Add(ref _cachedBytes, -replaced.Response.Body.Length);
            }

            _cache[key] = new CachedAnswer(etag, answer);
            Interlocked.Add(ref _cachedBytes, body.Length);
        }

        return answer;
    }

    /// <summary>The cache is per token: answers depend on what the token may read.</summary>
    private static string CacheKey(string token, string path)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)))[..16];
        return $"{hash} {path}";
    }

    private void TrackRateLimit(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && long.TryParse(remaining.FirstOrDefault(), out var left)
            && response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) && long.TryParse(reset.FirstOrDefault(), out var at))
        {
            Interlocked.Exchange(ref _remaining, left);
            Interlocked.Exchange(ref _resetAt, at);
        }
    }

    /// <summary>The <c>rel="next"</c> link, only when it stays on the API host: the token is never sent elsewhere.</summary>
    private static string? NextPage(HttpResponseMessage response, Uri? baseAddress)
    {
        if (baseAddress is null || !response.Headers.TryGetValues("Link", out var links))
        {
            return null;
        }

        var match = NextLinkPattern().Match(string.Join(',', links));
        return match.Success
            && Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var next)
            && next.Scheme == baseAddress.Scheme
            && next.Authority == baseAddress.Authority
            && next.AbsolutePath.StartsWith(baseAddress.AbsolutePath, StringComparison.Ordinal)
            ? next.PathAndQuery[baseAddress.AbsolutePath.Length..]
            : null;
    }

    private static string? ErrorMessage(byte[] body)
    {
        try
        {
            return body.Length == 0 ? null : JsonSerializer.Deserialize(body, GitHubJsonContext.Default.GitHubError)?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("<(?<url>[^>]+)>;\\s*rel=\"next\"")]
    private static partial Regex NextLinkPattern();

    private sealed record CachedAnswer(EntityTagHeaderValue ETag, GitHubResponse Response);
}
