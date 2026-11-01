using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portarr.Providers.Backrest;

// Subset of Backrest's API messages (proto/v1/config.proto, operations.proto, service.proto, v1.14.1) that
// Portarr reads, in the Connect protocol's JSON form: camelCase names, int64 as strings, enums as names,
// default values omitted. Everything else in an answer (repository passwords, hooks and their tokens) is
// ignored by the deserializer and never kept.

internal sealed record BackrestConfig([property: JsonPropertyName("plans")] IReadOnlyList<BackrestPlan>? Plans);

internal sealed record BackrestPlan(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("repo")] string? Repo,
    [property: JsonPropertyName("schedule")] BackrestSchedule? Schedule);

internal sealed record BackrestSchedule(
    [property: JsonPropertyName("disabled")] bool? Disabled,
    [property: JsonPropertyName("cron")] string? Cron,
    [property: JsonPropertyName("maxFrequencyDays")] int? MaxFrequencyDays,
    [property: JsonPropertyName("maxFrequencyHours")] int? MaxFrequencyHours);

/// <summary>Request of <c>GetOperations</c>: the operations of one plan, the last <c>LastN</c> of them.</summary>
internal sealed record GetOperationsRequest(
    [property: JsonPropertyName("selector")] OperationSelector Selector,
    [property: JsonPropertyName("lastN")] long LastN);

internal sealed record OperationSelector([property: JsonPropertyName("planId")] string PlanId);

internal sealed record OperationList([property: JsonPropertyName("operations")] IReadOnlyList<BackrestOperation>? Operations);

internal sealed record BackrestOperation(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("status")] OperationStatus Status,
    [property: JsonPropertyName("unixTimeStartMs")] long UnixTimeStartMs,
    [property: JsonPropertyName("unixTimeEndMs")] long UnixTimeEndMs,
    [property: JsonPropertyName("snapshotId")] string? SnapshotId,
    [property: JsonPropertyName("operationBackup")] BackupOperation? OperationBackup);

internal sealed record BackupOperation(
    [property: JsonPropertyName("lastStatus")] BackupProgressEntry? LastStatus,
    [property: JsonPropertyName("dryRun")] bool? DryRun);

internal sealed record BackupProgressEntry([property: JsonPropertyName("summary")] BackupProgressSummary? Summary);

internal sealed record BackupProgressSummary(
    [property: JsonPropertyName("totalBytesProcessed")] long? TotalBytesProcessed,
    [property: JsonPropertyName("snapshotId")] string? SnapshotId);

/// <summary>Backrest's <c>OperationStatus</c>; numbers are the proto values.</summary>
[JsonConverter(typeof(OperationStatusConverter))]
internal enum OperationStatus
{
    Unknown = 0,
    Pending = 1,
    InProgress = 2,
    Success = 3,
    Error = 4,
    SystemCancelled = 5,
    UserCancelled = 6,
    Warning = 7,
}

/// <summary>Reads <c>STATUS_SUCCESS</c> or <c>3</c>; anything unknown is <see cref="OperationStatus.Unknown"/>.</summary>
internal sealed class OperationStatusConverter : JsonConverter<OperationStatus>
{
    public override OperationStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            return Enum.IsDefined(typeof(OperationStatus), number) ? (OperationStatus)number : OperationStatus.Unknown;
        }

        return reader.TokenType == JsonTokenType.String ? reader.GetString() switch
        {
            "STATUS_PENDING" => OperationStatus.Pending,
            "STATUS_INPROGRESS" => OperationStatus.InProgress,
            "STATUS_SUCCESS" => OperationStatus.Success,
            "STATUS_WARNING" => OperationStatus.Warning,
            "STATUS_ERROR" => OperationStatus.Error,
            "STATUS_SYSTEM_CANCELLED" => OperationStatus.SystemCancelled,
            "STATUS_USER_CANCELLED" => OperationStatus.UserCancelled,
            _ => OperationStatus.Unknown,
        } : OperationStatus.Unknown;
    }

    public override void Write(Utf8JsonWriter writer, OperationStatus value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((int)value);
    }
}

[JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(BackrestConfig))]
[JsonSerializable(typeof(OperationList))]
[JsonSerializable(typeof(GetOperationsRequest))]
internal sealed partial class BackrestJsonContext : JsonSerializerContext;
