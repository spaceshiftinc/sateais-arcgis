using System.Text.Json;
using System.Text.Json.Serialization;

namespace SateAIs.Core.Jobs;

/// <summary>A job this client follows. Same fields as the QGIS plugin's record, so the two can be compared.</summary>
public sealed class TrackedJob
{
    [JsonPropertyName("job_id")] public string JobId { get; set; } = "";
    [JsonPropertyName("analysis_type")] public string AnalysisType { get; set; } = "";
    [JsonPropertyName("submitted_at")] public string SubmittedAt { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("error_code")] public string? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("polygon")] public string? Polygon { get; set; }
    /// <summary>Number of detections, filled the first time the result is loaded.</summary>
    [JsonPropertyName("detection_count")] public int? DetectionCount { get; set; }
    [JsonPropertyName("completed_at")] public string? CompletedAt { get; set; }
    [JsonPropertyName("area_sqkm")] public double? AreaSqkm { get; set; }
    [JsonPropertyName("credits_used")] public double? CreditsUsed { get; set; }
    [JsonPropertyName("scene_id")] public string? SceneId { get; set; }
    [JsonPropertyName("date")] public string? Date { get; set; }
    [JsonPropertyName("date_start")] public string? DateStart { get; set; }
    [JsonPropertyName("date_end")] public string? DateEnd { get; set; }
    /// <summary>"" never resolved, "local" captured at submit, "server" from a refresh, "unavailable" when the server had nothing.</summary>
    [JsonPropertyName("request_source")] public string RequestSource { get; set; } = "";

    /// <summary>Reads one stored entry; null when it has no usable job id. Odd values become defaults.</summary>
    public static TrackedJob? FromJson(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var jobId = Str(e, "job_id");
        if (string.IsNullOrEmpty(jobId))
        {
            return null;
        }
        return new TrackedJob
        {
            JobId = jobId,
            AnalysisType = Str(e, "analysis_type") ?? "",
            SubmittedAt = Str(e, "submitted_at") ?? "",
            Status = Str(e, "status") ?? "pending",
            ErrorCode = Str(e, "error_code"),
            ErrorMessage = Str(e, "error_message"),
            Polygon = Str(e, "polygon"),
            DetectionCount = NonNegativeInt(e, "detection_count"),
            CompletedAt = Str(e, "completed_at"),
            AreaSqkm = Num(e, "area_sqkm"),
            CreditsUsed = Num(e, "credits_used"),
            SceneId = Str(e, "scene_id"),
            Date = Str(e, "date"),
            DateStart = Str(e, "date_start"),
            DateEnd = Str(e, "date_end"),
            RequestSource = Str(e, "request_source") ?? "",
        };
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? NonNegativeInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= 0
            ? n
            : null;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
