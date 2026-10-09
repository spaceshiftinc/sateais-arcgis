using System.Text.RegularExpressions;
using SateAIs.Core.Api;

namespace SateAIs.Core.Wording;

/// <summary>
/// User-facing wording for estimates, coverage and failures. Shared with the other SateAIs
/// clients: the same situation reads the same everywhere. <c>contract/wording-cases.json</c>
/// pins every sentence and rounding rule.
/// </summary>
public static partial class Wording
{
    public const string LegendRequested = "Requested";
    public const string LegendCovered = "Covered";
    public const string LegendNotCovered = "Not covered";

    public const string Checking = "Checking coverage and cost…";
    public const string ScenesUnavailableHint = "Try a nearby date, a longer period, or a wider area.";
    public const string CoverageUnchecked =
        "The analysed area could not be checked this time. The cost assumes the full area.";
    public const string EstimateFailed = "The estimate could not be loaded this time. Edit the area to try again.";
    public const string SetupTitle = "Set up the analysis";

    /// <summary>Warning codes the server uses when no usable scene exists. A submitted job fails with the same codes.</summary>
    public static readonly IReadOnlySet<string> ScenesUnavailableCodes =
        new HashSet<string> { "SCENE_NOT_FOUND", "INSUFFICIENT_SCENES" };

    [GeneratedRegex(@"area\s*\(([\d.,]+)\s*km\S*\)\s*exceeds\s*([\d.,]+)\s*km", RegexOptions.IgnoreCase)]
    private static partial Regex AreaLimitPattern();

    [GeneratedRegex(@"\bGONE\b|retention period", RegexOptions.IgnoreCase)]
    private static partial Regex GonePattern();

    [GeneratedRegex("scene_id format", RegexOptions.IgnoreCase)]
    private static partial Regex SceneIdFormatPattern();

    [GeneratedRegex("_IW_SLC_", RegexOptions.IgnoreCase)]
    private static partial Regex SlcPattern();

    /// <summary>One-line credit estimate. Always an upper bound: billing follows the processed area.</summary>
    public static string CreditsLabel(double? estimated)
    {
        if (!Known(estimated))
        {
            return "Cost is known only after it runs.";
        }
        if (estimated == 0)
        {
            return "No credits for this analysis.";
        }
        return $"up to {Fmt(estimated!.Value)} credits";
    }

    public static bool CreditsUnknown(double? estimated) => !Known(estimated);

    /// <summary>Coverage sentence. Percentages round down so they never contradict a warning that fires below 90%.</summary>
    public static string CoverageLabel(double? ratio, bool past = false)
    {
        if (!Known(ratio))
        {
            return "";
        }
        if (ratio >= 1)
        {
            return past ? "All of your area was analysed" : "";
        }
        var pct = (int)Math.Floor(ratio!.Value * 100);
        return past ? $"Only {pct}% of your area was analysed" : $"{pct}% covered";
    }

    public static bool CoverageIsPartial(double? ratio) => Known(ratio) && ratio < 1;

    /// <summary>Show the balance only when it is short; otherwise nobody uses the number.</summary>
    public static string BalanceNote(bool? sufficient, double? balance) =>
        sufficient == false && Known(balance) ? $"balance {Fmt(balance!.Value)}" : "";

    public static bool ScenesUnavailable(IEnumerable<ApiWarning> warnings) =>
        warnings.Any(w => w.Code is not null && ScenesUnavailableCodes.Contains(w.Code));

    /// <summary>Server-provided warning sentences, in order, without the codes.</summary>
    public static IReadOnlyList<string> WarningMessages(IEnumerable<ApiWarning> warnings) =>
        warnings.Where(w => !string.IsNullOrEmpty(w.Message)).Select(w => w.Message!).ToList();

    /// <summary>
    /// Rewrite the server's area-limit rejection, or "" when it is not one. The limit differs per
    /// analysis and the API does not expose it, so the numbers come from the rejection itself.
    /// </summary>
    public static string AreaLimitReason(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "";
        }
        var match = AreaLimitPattern().Match(message);
        if (!match.Success)
        {
            return "";
        }
        return $"This area is {match.Groups[1].Value} km², over the {match.Groups[2].Value} km² limit for this analysis. "
            + "Draw a smaller area, or split it into several runs.";
    }

    /// <summary>Why a job failed, in the user's terms. The server's own message is never shown.</summary>
    public static string FailureLabel(string? code, string? message)
    {
        var raw = message ?? "";
        if (code == "GONE" || GonePattern().IsMatch(raw))
        {
            return "This result was deleted after the 30-day retention period. Run the analysis again to get it back.";
        }
        var overLimit = AreaLimitReason(raw);
        if (overLimit.Length > 0)
        {
            return overLimit;
        }
        if (SceneIdFormatPattern().IsMatch(raw))
        {
            return SlcPattern().IsMatch(raw)
                ? "That scene is an SLC product. Ship detection needs a GRD scene — its name contains _IW_GRDH_."
                : "That scene name is not a Sentinel-1 GRD granule. Look it up on ASF Search with File Type set to GRD.";
        }
        return code switch
        {
            "SCENE_NOT_FOUND" => "No satellite scene was found for that area and date. " + ScenesUnavailableHint,
            "INSUFFICIENT_SCENES" => "There were not enough scenes to compare. " + ScenesUnavailableHint,
            "INSUFFICIENT_CREDITS" => "There were not enough credits to run this.",
            "VALIDATION_ERROR" => "This request could not be run as written.",
            _ => "This run did not finish. Nothing was charged.",
        };
    }

    /// <summary>Area in km²: no decimals from 100 km² up, two below.</summary>
    public static string AreaKm2Label(double km2) => $"{PyFormat.Fixed(km2, km2 >= 100 ? 0 : 2)} km²";

    /// <summary>How long the run took, e.g. <c>3m 31s</c>. "" when either end is unknown.</summary>
    public static string TookLabel(string? submittedAt, string? completedAt)
    {
        var start = JobSummary.ParseIso8601(submittedAt);
        var end = JobSummary.ParseIso8601(completedAt);
        if (start is null || end is null)
        {
            return "";
        }
        var seconds = (long)Math.Round((end.Value - start.Value).TotalSeconds, MidpointRounding.ToEven);
        if (seconds < 0)
        {
            return "";
        }
        if (seconds < 60)
        {
            return $"{seconds}s";
        }
        var minutes = seconds / 60;
        if (minutes < 60)
        {
            return $"{minutes}m {seconds % 60}s";
        }
        return $"{minutes / 60}h {minutes % 60}m";
    }

    /// <summary>A credit figure on its own, at the rounding used everywhere else.</summary>
    public static string FormatCredits(double value) => Fmt(value);

    /// <summary>Captioned figures for a job row. Unknown values are left out rather than captioned blank.</summary>
    public static IReadOnlyList<(string Label, string Value)> JobMetaFields(
        string submittedDisplay,
        double? areaSqkm,
        double? creditsUsed,
        string took)
    {
        var fields = new List<(string, string)>();
        if (!string.IsNullOrEmpty(submittedDisplay))
        {
            fields.Add(("Submitted", submittedDisplay));
        }
        if (Known(areaSqkm))
        {
            fields.Add(("Area", AreaKm2Label(areaSqkm!.Value)));
        }
        if (Known(creditsUsed))
        {
            fields.Add(("Cost", creditsUsed == 0 ? "free" : $"{Fmt(creditsUsed!.Value)} credits"));
        }
        if (!string.IsNullOrEmpty(took))
        {
            fields.Add(("Took", took));
        }
        return fields;
    }

    /// <summary>True only for a usable number. NaN and infinity read as unknown, never as free or full.</summary>
    private static bool Known(double? value) => value is { } v && double.IsFinite(v);

    private static string Fmt(double value) => PyFormat.Fixed(value, 2);
}
