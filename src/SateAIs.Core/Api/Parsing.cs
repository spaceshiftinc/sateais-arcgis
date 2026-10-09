using System.Text.Json;

namespace SateAIs.Core.Api;

/// <summary>Reads API responses defensively: unknown fields are ignored, odd values become null.</summary>
internal static class Parsing
{
    public static Job JobFromJson(JsonElement e)
    {
        var jobId = OptString(e, "job_id");
        if (string.IsNullOrEmpty(jobId))
        {
            throw new ApiException(0, null, "response is missing a valid job_id");
        }
        string? coveragePolygon = null;
        if (e.TryGetProperty("coverage", out var coverage) && coverage.ValueKind == JsonValueKind.Object)
        {
            coveragePolygon = OptString(coverage, "polygon");
        }
        JsonElement? requestParams = null;
        if (e.TryGetProperty("request_params", out var request) && request.ValueKind == JsonValueKind.Object)
        {
            requestParams = request.Clone();
        }
        return new Job(
            JobId: jobId,
            Status: JobStatuses.Parse(OptString(e, "status")),
            CreatedAt: OptString(e, "created_at"),
            CompletedAt: OptString(e, "completed_at"),
            ErrorCode: OptString(e, "error_code") ?? OptString(e, "error"),
            ErrorMessage: OptString(e, "error_message"),
            EndpointId: OptString(e, "endpoint_id"),
            CoveragePolygon: coveragePolygon,
            AreaSqkm: OptNumber(e, "area_sqkm"),
            CreditsUsed: OptNumber(e, "credits_used"),
            RequestParams: requestParams);
    }

    public static Preview PreviewFromJson(JsonElement e)
    {
        PreviewCredits? credits = null;
        if (e.TryGetProperty("credits", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            credits = new PreviewCredits(OptNumber(c, "estimated"), OptNumber(c, "balance"), OptBool(c, "sufficient"));
        }
        PreviewCoverage? coverage = null;
        if (e.TryGetProperty("coverage", out var v) && v.ValueKind == JsonValueKind.Object)
        {
            var polygon = OptString(v, "polygon");
            coverage = new PreviewCoverage(
                OptString(v, "method"),
                OptNumber(v, "ratio"),
                OptNumber(v, "requested_area_sqkm"),
                string.IsNullOrEmpty(polygon) ? null : polygon);
        }
        var warnings = new List<ApiWarning>();
        if (e.TryGetProperty("warnings", out var w) && w.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in w.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    warnings.Add(new ApiWarning(OptString(item, "code"), OptString(item, "message")));
                }
            }
        }
        return new Preview
        {
            EndpointId = OptString(e, "endpoint_id"),
            AreaSqkm = OptNumber(e, "area_sqkm"),
            Credits = credits,
            Coverage = coverage,
            Warnings = warnings,
        };
    }

    public static Account AccountFromJson(JsonElement e)
    {
        double? balance = null;
        if (e.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Object)
        {
            balance = OptNumber(credits, "balance");
        }
        return new Account(OptString(e, "email"), OptString(e, "display_name"), balance);
    }

    public static string? OptString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Numbers only, and only finite ones; anything else is "unknown".</summary>
    public static double? OptNumber(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            return null;
        }
        var number = v.GetDouble();
        return double.IsFinite(number) ? number : null;
    }

    public static bool? OptBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;
}
