using System.Text.Json;
using System.Text.RegularExpressions;
using Portarr.Core.Platforms;

namespace Portarr.Providers.Platforms;

/// <summary>
/// The release lines of K3s (D-119): <c>update.k3s.io</c> publishes a channel per Kubernetes line (<c>v1.31</c>) whose newest version
/// is the newest K3s release on that line. The channels <c>stable</c>, <c>latest</c> and <c>testing</c> and the pre-release channels
/// of each line are not lines and are skipped. One GET, nothing sent.
/// </summary>
public sealed partial class K3sReleaseLineSource : IReleaseLineSource
{
    public static readonly Uri ChannelsUrl = new("https://update.k3s.io/v1-release/channels");

    private readonly IHttpClientFactory _httpClientFactory;

    public K3sReleaseLineSource(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string Platform => PlatformKinds.K3s;

    public string DisplayName => "K3s";

    [GeneratedRegex(@"^v(?<line>[0-9]{1,3}\.[0-9]{1,3})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex LineChannel();

    public async Task<IReadOnlyList<ReleaseLine>> ReadAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ChannelsUrl);
        using var response = await _httpClientFactory.CreateClient(PlatformsHttp.ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = PlatformsHttp.Capped(await response.Content.ReadAsStreamAsync(cancellationToken));
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The K3s channels answer has no list of channels.");
        }

        var lines = new List<(ReleaseLine Line, KubernetesVersion Version)>();
        foreach (var channel in data.EnumerateArray())
        {
            var id = PlatformsHttp.Text(channel, "id");
            var latest = PlatformsHttp.Text(channel, "latest");
            if (id is not null && latest is not null && LineChannel().Match(id) is { Success: true } match && KubernetesVersion.Parse(latest) is { Distribution: "k3s", PreRelease: false } version)
            {
                lines.Add((new ReleaseLine(match.Groups["line"].Value, latest), version));
            }
        }

        return lines.OrderByDescending(l => l.Version).Select(l => l.Line).ToList();
    }
}
