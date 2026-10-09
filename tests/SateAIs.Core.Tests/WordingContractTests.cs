using System.Text.Json;
using System.Text.Json.Nodes;
using SateAIs.Core.Api;
using SateAIs.Core.Wording;
using Xunit;

namespace SateAIs.Core.Tests;

/// <summary>Every sentence and rounding rule in contract/wording-cases.json, checked against this implementation.</summary>
public class WordingContractTests
{
    private static readonly JsonDocument Contract = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contract", "wording-cases.json")));

    private static readonly string[] Functions =
    [
        "credits_label", "coverage_label", "coverage_is_partial", "balance_note", "area_km2_label",
        "took_label", "format_credits", "job_meta_fields", "area_limit_reason", "failure_label",
        "warning_messages", "scenes_unavailable", "format_analysis_label", "format_detection_summary",
        "format_detection_outcome", "format_detection_count", "format_relative_seconds", "parse_iso8601",
    ];

    public static IEnumerable<object[]> FunctionNames() => Functions.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(FunctionNames))]
    public void Matches_the_contract(string function)
    {
        var cases = Contract.RootElement.GetProperty(function);
        var mismatches = new List<string>();
        foreach (var @case in cases.EnumerateArray())
        {
            var args = @case.GetProperty("args");
            var expected = JsonNode.Parse(@case.GetProperty("expected").GetRawText());
            var actual = JsonSerializer.SerializeToNode(Invoke(function, args));
            if (!JsonNode.DeepEquals(expected, actual))
            {
                mismatches.Add($"{function}({args.GetRawText()}): expected {expected?.ToJsonString() ?? "null"}, got {actual?.ToJsonString() ?? "null"}");
            }
        }
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    [Fact]
    public void Every_function_in_the_contract_is_covered()
    {
        var inContract = Contract.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .Where(name => name != "_about" && name != "constants");
        Assert.Empty(inContract.Except(Functions));
    }

    [Fact]
    public void Constants_match()
    {
        var constants = Contract.RootElement.GetProperty("constants");
        Assert.Equal(Wording.Wording.LegendRequested, constants.GetProperty("LEGEND_REQUESTED").GetString());
        Assert.Equal(Wording.Wording.LegendCovered, constants.GetProperty("LEGEND_COVERED").GetString());
        Assert.Equal(Wording.Wording.LegendNotCovered, constants.GetProperty("LEGEND_NOT_COVERED").GetString());
        Assert.Equal(Wording.Wording.Checking, constants.GetProperty("CHECKING").GetString());
        Assert.Equal(Wording.Wording.ScenesUnavailableHint, constants.GetProperty("SCENES_UNAVAILABLE_HINT").GetString());
        Assert.Equal(Wording.Wording.CoverageUnchecked, constants.GetProperty("COVERAGE_UNCHECKED").GetString());
        Assert.Equal(Wording.Wording.EstimateFailed, constants.GetProperty("ESTIMATE_FAILED").GetString());
        Assert.Equal(Wording.Wording.SetupTitle, constants.GetProperty("SETUP_TITLE").GetString());

        var labels = constants.GetProperty("ANALYSIS_LABELS").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        Assert.Equal(labels, JobSummary.AnalysisLabels.ToDictionary(p => p.Key, p => (string?)p.Value));

        var nouns = constants.GetProperty("DETECTION_NOUNS").EnumerateObject()
            .ToDictionary(p => p.Name, p => (p.Value[0].GetString(), p.Value[1].GetString()));
        Assert.Equal(nouns, JobSummary.DetectionNouns.ToDictionary(p => p.Key, p => ((string?)p.Value.Singular, (string?)p.Value.Plural)));
    }

    private static object? Invoke(string function, JsonElement a) => function switch
    {
        "credits_label" => Wording.Wording.CreditsLabel(Num(a[0])),
        "coverage_label" => Wording.Wording.CoverageLabel(Num(a[0]), a[1].GetBoolean()),
        "coverage_is_partial" => Wording.Wording.CoverageIsPartial(Num(a[0])),
        "balance_note" => Wording.Wording.BalanceNote(Bool(a[0]), Num(a[1])),
        "area_km2_label" => Wording.Wording.AreaKm2Label(a[0].GetDouble()),
        "took_label" => Wording.Wording.TookLabel(Str(a[0]), Str(a[1])),
        "format_credits" => Wording.Wording.FormatCredits(a[0].GetDouble()),
        "job_meta_fields" => Wording.Wording.JobMetaFields(Str(a[0]) ?? "", Num(a[1]), Num(a[2]), Str(a[3]) ?? "")
            .Select(f => new[] { f.Label, f.Value }).ToList(),
        "area_limit_reason" => Wording.Wording.AreaLimitReason(Str(a[0])),
        "failure_label" => Wording.Wording.FailureLabel(Str(a[0]), Str(a[1])),
        "warning_messages" => Wording.Wording.WarningMessages(Warnings(a[0])).ToList(),
        "scenes_unavailable" => Wording.Wording.ScenesUnavailable(Warnings(a[0])),
        "format_analysis_label" => JobSummary.FormatAnalysisLabel(Str(a[0])),
        "format_detection_summary" => JobSummary.FormatDetectionSummary(Str(a[0])!, a[1].GetInt32()),
        "format_detection_outcome" => JobSummary.FormatDetectionOutcome(Str(a[0])!, a[1].GetInt32()),
        "format_detection_count" => JobSummary.FormatDetectionCount(a[0].GetInt32()),
        "format_relative_seconds" => JobSummary.FormatRelative(TimeSpan.FromSeconds(a[0].GetDouble())),
        "parse_iso8601" => JobSummary.ParseIso8601(Str(a[0])) is { } parsed ? JobSummary.IsoString(parsed) : null,
        _ => throw new ArgumentOutOfRangeException(nameof(function), function, "not covered by this test"),
    };

    private static double? Num(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;

    private static bool? Bool(JsonElement e) => e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : null;

    private static string? Str(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static List<ApiWarning> Warnings(JsonElement e) => e.EnumerateArray()
        .Select(w => new ApiWarning(
            w.TryGetProperty("code", out var c) ? c.GetString() : null,
            w.TryGetProperty("message", out var m) ? m.GetString() : null))
        .ToList();
}
