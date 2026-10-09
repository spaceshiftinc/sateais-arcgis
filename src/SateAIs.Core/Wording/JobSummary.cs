using System.Globalization;
using System.Text.RegularExpressions;

namespace SateAIs.Core.Wording;

/// <summary>Human-readable summaries of a job: labels, counts and dates.</summary>
public static partial class JobSummary
{
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

    private static (string Singular, string Plural) Nouns(string analysisType) =>
        DetectionNouns.GetValueOrDefault(analysisType, ("detection", "detections"));

    private static string Grouped(int count) => count.ToString("N0", CultureInfo.InvariantCulture);
}
