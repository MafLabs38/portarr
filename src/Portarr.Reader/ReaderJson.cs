using System.Text.Json.Serialization;

namespace Portarr.Reader;

/// <summary>What <c>/v1/info</c> says about the reader.</summary>
internal sealed record InfoResponse(string Name, string Version, string Host, IReadOnlyList<string> Capabilities);

/// <summary>An error answer: a short sentence, never what Docker or the kernel said.</summary>
internal sealed record ErrorResponse(string Message);

/// <summary>
/// The reader's own JSON, generated at build time: the reader is compiled ahead of time (native AOT), where serializing
/// an anonymous type through reflection does not exist.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InfoResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class ReaderJsonContext : JsonSerializerContext;
