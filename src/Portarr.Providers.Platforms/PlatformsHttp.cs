using System.Text.Json;
using Portarr.Core.Net;

namespace Portarr.Providers.Platforms;

/// <summary>What the platform connectors share: the one named HTTP client of public services (GET only, behind the address policy) and how an answer is read.</summary>
internal static class PlatformsHttp
{
    public const string ClientName = "platforms";

    /// <summary>The largest answer read, in bytes once decompressed: the channels of K3s and the releases of a product are a few hundred kilobytes at most.</summary>
    public const int MaxResponseBytes = 2 * 1024 * 1024;

    /// <summary>The body of an answer as a stream that fails past <see cref="MaxResponseBytes"/>: <c>HttpClient</c>'s own buffer limit does not apply to a streamed read.</summary>
    public static Stream Capped(Stream body) => new CappedStream(body, MaxResponseBytes);

    /// <summary>A string property of an object; null when it is absent or is anything else.</summary>
    public static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
