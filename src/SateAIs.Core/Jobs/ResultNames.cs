namespace SateAIs.Core.Jobs;

/// <summary>Names for what a result becomes on the map: the layer, its feature class and the group.</summary>
public static class ResultNames
{
    public const string GroupName = "SateAIs Results";

    /// <summary>The first block of the job id, which is how a layer name refers to a job.</summary>
    public static string ShortId(string? jobId) => string.IsNullOrEmpty(jobId) ? "unknown" : jobId.Split('-')[0];

    /// <summary>E.g. <c>SateAIs ship a1b2c3d4 (2026-07-03) · 23</c>; the count is left off when unknown.</summary>
    public static string LayerName(string analysisType, string jobId, string? submittedAt, int? count = null)
    {
        var date = string.IsNullOrEmpty(submittedAt) ? "n/a" : submittedAt[..Math.Min(10, submittedAt.Length)];
        var name = $"SateAIs {analysisType} {ShortId(jobId)} ({date})";
        return count is >= 0 ? $"{name} · {count}" : name;
    }

    public static string AoiLayerName(string analysisType, string jobId) => $"SateAIs AOI {analysisType} {ShortId(jobId)}";

    /// <summary>A geodatabase name: letters, digits and underscores only.</summary>
    public static string FeatureClassName(string analysisType, string jobId) => Sanitise($"SateAIs_{analysisType}_{ShortId(jobId)}");

    public static string AoiFeatureClassName(string analysisType, string jobId) => Sanitise($"SateAIs_AOI_{analysisType}_{ShortId(jobId)}");

    private static string Sanitise(string name) =>
        new(name.Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
}
