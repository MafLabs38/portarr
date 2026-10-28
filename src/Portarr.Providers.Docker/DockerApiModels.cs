using System.Text.Json.Serialization;

namespace Portarr.Providers.Docker;

// Subset of the Docker Engine API responses Portarr reads.

internal sealed record ContainerSummary(
    [property: JsonPropertyName("Id")] string Id,
    [property: JsonPropertyName("Names")] IReadOnlyList<string> Names,
    [property: JsonPropertyName("Image")] string Image,
    [property: JsonPropertyName("State")] string State,
    [property: JsonPropertyName("Status")] string Status,
    [property: JsonPropertyName("Created")] long Created,
    [property: JsonPropertyName("Labels")] IReadOnlyDictionary<string, string>? Labels);

internal sealed record ContainerInspect(
    [property: JsonPropertyName("Id")] string Id,
    [property: JsonPropertyName("Config")] ContainerConfig Config);

internal sealed record ContainerConfig([property: JsonPropertyName("Tty")] bool Tty);

internal sealed record ContainerStats(
    [property: JsonPropertyName("cpu_stats")] CpuStats CpuStats,
    [property: JsonPropertyName("precpu_stats")] CpuStats PreCpuStats,
    [property: JsonPropertyName("memory_stats")] MemoryStats MemoryStats);

internal sealed record CpuStats(
    [property: JsonPropertyName("cpu_usage")] CpuUsage CpuUsage,
    [property: JsonPropertyName("system_cpu_usage")] ulong? SystemCpuUsage,
    [property: JsonPropertyName("online_cpus")] int? OnlineCpus);

internal sealed record CpuUsage([property: JsonPropertyName("total_usage")] ulong TotalUsage);

internal sealed record MemoryStats(
    [property: JsonPropertyName("usage")] long? Usage,
    [property: JsonPropertyName("limit")] long? Limit,
    [property: JsonPropertyName("stats")] IReadOnlyDictionary<string, long>? Stats);

[JsonSerializable(typeof(List<ContainerSummary>))]
[JsonSerializable(typeof(ContainerInspect))]
[JsonSerializable(typeof(ContainerStats))]
internal sealed partial class DockerJsonContext : JsonSerializerContext;
