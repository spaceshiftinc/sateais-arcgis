using System.Text.Json;

namespace SateAIs.Core.Api;

public enum JobStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Unknown,
}

public static class JobStatuses
{
    public static JobStatus Parse(string? raw) => raw switch
    {
        "pending" => JobStatus.Pending,
        "processing" => JobStatus.Processing,
        "completed" => JobStatus.Completed,
        "failed" => JobStatus.Failed,
        _ => JobStatus.Unknown,
    };

    public static string WireValue(this JobStatus status) => status switch
    {
        JobStatus.Pending => "pending",
        JobStatus.Processing => "processing",
        JobStatus.Completed => "completed",
        JobStatus.Failed => "failed",
        _ => "unknown",
    };
}

/// <summary>A job as the API describes it.</summary>
public sealed record Job(
    string JobId,
    JobStatus Status,
    string? CreatedAt = null,
    string? CompletedAt = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    string? EndpointId = null,
    string? CoveragePolygon = null,
    double? AreaSqkm = null,
    double? CreditsUsed = null,
    JsonElement? RequestParams = null)
{
    public bool IsTerminal => Status is JobStatus.Completed or JobStatus.Failed;
    public bool IsCompleted => Status == JobStatus.Completed;
    public bool IsFailed => Status == JobStatus.Failed;
}

/// <summary>Credit estimate for a would-be job. Null means "not known before the run", never free.</summary>
public sealed record PreviewCredits(double? Estimated = null, double? Balance = null, bool? Sufficient = null);

/// <summary>How much of the requested area would actually be analysed.</summary>
public sealed record PreviewCoverage(
    string? Method = null,
    double? Ratio = null,
    double? RequestedAreaSqkm = null,
    string? Polygon = null);

public sealed record ApiWarning(string? Code, string? Message);

/// <summary>Response of <c>POST /analyze/{type}/preview</c>. A missing coverage is unknown, not full.</summary>
public sealed record Preview
{
    public string? EndpointId { get; init; }
    public double? AreaSqkm { get; init; }
    public PreviewCredits? Credits { get; init; }
    public PreviewCoverage? Coverage { get; init; }
    public IReadOnlyList<ApiWarning> Warnings { get; init; } = [];
}

/// <summary>Response of <c>GET /me</c>.</summary>
public sealed record Account(string? Email, string? DisplayName, double? CreditBalance);
