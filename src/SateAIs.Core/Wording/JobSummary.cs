using System.Globalization;
using System.Text.RegularExpressions;
using SateAIs.Core.Jobs;

namespace SateAIs.Core.Wording;

/// <summary>Human-readable summaries of a job: labels, counts and dates.</summary>
public static partial class JobSummary
{
    /// <summary>Shown while a job's request parameters are still unknown.</summary>
    public const string MissingRequestHint = "Request details unavailable — press Refresh to fetch them.";

    private const int SceneIdMax = 30;

    /// <summary>Display names. The same names head the public API documentation.</summary>
    public static readonly IReadOnlyDictionary<string, string> AnalysisLabels = new Dictionary<string, string>
    {
        ["ship"] = "Ship detection",
        ["oilslick"] = "Oil slick detection",
        ["newbuilding"] = "New building detection",
        ["disappearbuilding"] = "Disappeared building detection",
        ["timeseries"] = "Time-series change",
    };

    /// <summary>(singular, plural) so a result reads "23 ships" / "1 change" rather than a bare count.</summary>
    public static readonly IReadOnlyDictionary<string, (string Singular, string Plural)> DetectionNouns =
        new Dictionary<string, (string, string)>
        {
            ["ship"] = ("ship", "ships"),
            ["oilslick"] = ("oil slick", "oil slicks"),
            ["newbuilding"] = ("new building", "new buildings"),
            ["disappearbuilding"] = ("removed building", "removed buildings"),
            ["timeseries"] = ("change", "changes"),
        };

    private static readonly string[] IsoFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.ffffffzzz",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.ffffff",
        "yyyy-MM-dd'T'HH:mm:ss",
    ];

    [GeneratedRegex(@"(?<=:\d\d)\.(\d+)")]
    private static partial Regex FractionalSeconds();

    /// <summary>Display name for a detection type, falling back to the raw value.</summary>
    public static string FormatAnalysisLabel(string? analysisType) =>
        string.IsNullOrEmpty(analysisType) ? "" : AnalysisLabels.GetValueOrDefault(analysisType, analysisType);

    /// <summary>E.g. <c>23 ships</c> / <c>1 change</c> / <c>12 detections</c>.</summary>
    public static string FormatDetectionSummary(string analysisType, int count)
    {
        var (singular, plural) = Nouns(analysisType);
        return $"{count} {(count == 1 ? singular : plural)}";
    }

    /// <summary>E.g. <c>138 new buildings found</c> / <c>No ships found</c>.</summary>
    public static string FormatDetectionOutcome(string analysisType, int count)
    {
        var (singular, plural) = Nouns(analysisType);
        if (count == 0)
        {
            return $"No {plural} found";
        }
        return $"{Grouped(count)} {(count == 1 ? singular : plural)} found";
    }

    /// <summary>The count beside the type name: <c>138 found</c> / <c>None found</c>.</summary>
    public static string FormatDetectionCount(int count) => count != 0 ? $"{Grouped(count)} found" : "None found";

    /// <summary>
    /// Parse an ISO 8601 timestamp, or null when it cannot be read. The API trims trailing zeros
    /// from fractional seconds and ends in <c>Z</c>; a timestamp without a zone is UTC.
    /// </summary>
    public static DateTimeOffset? ParseIso8601(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        var normalised = value.Replace("Z", "+00:00");
        normalised = FractionalSeconds().Replace(normalised, m =>
        {
            var digits = m.Groups[1].Value;
            digits = digits.Length > 6 ? digits[..6] : digits.PadRight(6, '0');
            return "." + digits;
        }, 1);
        return DateTimeOffset.TryParseExact(
            normalised, IsoFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Python's <c>isoformat()</c>: microseconds only when they are not zero.</summary>
    public static string IsoString(DateTimeOffset value)
    {
        var format = value.Ticks % TimeSpan.TicksPerSecond == 0
            ? "yyyy-MM-dd'T'HH:mm:sszzz"
            : "yyyy-MM-dd'T'HH:mm:ss.ffffffzzz";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Just <c>2026-07-30 14:12</c> in local time; the raw text when it cannot be parsed.</summary>
    public static string FormatSubmittedShort(string? isoTimestamp)
    {
        if (string.IsNullOrEmpty(isoTimestamp))
        {
            return "";
        }
        var parsed = ParseIso8601(isoTimestamp);
        return parsed is null ? isoTimestamp : parsed.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary><c>2026-07-30 14:12 (18h ago)</c> in local time. <paramref name="now"/> is injectable for tests.</summary>
    public static string FormatSubmittedAt(string? isoTimestamp, DateTimeOffset? now = null)
    {
        if (string.IsNullOrEmpty(isoTimestamp))
        {
            return "";
        }
        var parsed = ParseIso8601(isoTimestamp);
        if (parsed is null)
        {
            return isoTimestamp;
        }
        var reference = now ?? DateTimeOffset.UtcNow;
        var absolute = parsed.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return $"{absolute} ({FormatRelative(reference - parsed.Value)})";
    }

    /// <summary>Coarse "N ago" wording; anything in the future reads as <c>just now</c>.</summary>
    public static string FormatRelative(TimeSpan delta)
    {
        var seconds = (long)delta.TotalSeconds;
        if (seconds < 0)
        {
            return "just now";
        }
        if (seconds < 60)
        {
            return $"{seconds}s ago";
        }
        var minutes = seconds / 60;
        if (minutes < 60)
        {
            return $"{minutes}m ago";
        }
        var hours = minutes / 60;
        if (hours < 24)
        {
            return $"{hours}h ago";
        }
        return $"{hours / 24}d ago";
    }

    /// <summary>
    /// A scene id shortened to what identifies it: platform and acquisition date for a Sentinel-1
    /// granule, otherwise cut to length. The full id stays in the tooltip.
    /// </summary>
    public static string FormatSceneId(string? sceneId)
    {
        if (string.IsNullOrEmpty(sceneId))
        {
            return "";
        }
        var parts = sceneId.Split('_');
        if (parts.Length >= 5 && parts[0].Length >= 2 && parts[0][..2].Equals("S1", StringComparison.OrdinalIgnoreCase))
        {
            var stamp = parts[4];
            if (stamp.Length >= 8 && stamp[..8].All(char.IsAsciiDigit))
            {
                return $"{parts[0]} {stamp[..4]}-{stamp[4..6]}-{stamp[6..8]}";
            }
        }
        return Shorten(sceneId, SceneIdMax);
    }

    /// <summary>
    /// One line saying what was asked for, or "" when nothing is known. The dates are labelled:
    /// a card also shows when the job was submitted, and the two look alike unlabelled.
    /// </summary>
    public static string BuildRequestSummary(TrackedJob job)
    {
        if (!string.IsNullOrEmpty(job.DateStart) && !string.IsNullOrEmpty(job.DateEnd))
        {
            return $"Period {job.DateStart} → {job.DateEnd}";
        }
        if (!string.IsNullOrEmpty(job.SceneId))
        {
            return $"Scene {FormatSceneId(job.SceneId)}";
        }
        if (!string.IsNullOrEmpty(job.Date))
        {
            return $"Reference date {job.Date}";
        }
        return "";
    }

    /// <summary>Everything the card itself elides, as plain text lines.</summary>
    public static string BuildRequestTooltip(TrackedJob job, DateTimeOffset? now = null)
    {
        var lines = new List<string> { FormatAnalysisLabel(job.AnalysisType), $"Job ID: {job.JobId}" };
        var submitted = FormatSubmittedAt(job.SubmittedAt, now);
        if (submitted.Length > 0)
        {
            lines.Add($"Submitted: {submitted}");
        }
        if (!string.IsNullOrEmpty(job.DateStart) && !string.IsNullOrEmpty(job.DateEnd))
        {
            lines.Add($"Period: {job.DateStart} → {job.DateEnd}");
        }
        else if (!string.IsNullOrEmpty(job.Date))
        {
            lines.Add($"Reference date: {job.Date}");
        }
        if (!string.IsNullOrEmpty(job.SceneId))
        {
            lines.Add($"Scene: {job.SceneId}");
        }
        if (job.RequestSource.Length == 0)
        {
            lines.Add("");
            lines.Add(MissingRequestHint);
        }
        return string.Join("\n", lines);
    }

    /// <summary>Lower-cased haystack for the search box: full id, both spellings of the type, scene and dates.</summary>
    public static string BuildSearchText(TrackedJob job)
    {
        string?[] parts =
        [
            job.JobId, job.AnalysisType, FormatAnalysisLabel(job.AnalysisType),
            job.SceneId, job.Date, job.DateStart, job.DateEnd,
        ];
        return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p))).ToLowerInvariant();
    }

    private static string Shorten(string text, int limit) =>
        text.Length <= limit ? text : text[..Math.Max(limit - 1, 0)] + "…";

    private static (string Singular, string Plural) Nouns(string analysisType) =>
        DetectionNouns.GetValueOrDefault(analysisType, ("detection", "detections"));

    private static string Grouped(int count) => count.ToString("N0", CultureInfo.InvariantCulture);
}
