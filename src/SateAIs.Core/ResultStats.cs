using System.Text.Json;
using System.Text.Json.Nodes;

namespace SateAIs.Core;

/// <summary>Finding counts and payload slimming over result GeoJSON.</summary>
public static class ResultStats
{
    /// <summary>A time-series cell counts as a change once its deviation clears this floor.</summary>
    public const double SignificantDeviation = 0.05;

    /// <summary>Beyond this the change is drawn at the saturated end of the ramp.</summary>
    public const double StrongDeviation = 0.50;

    private const string DeviationKey = "deviation";

    /// <summary>Chart series that dominate the payload but mean nothing on a map.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> HeavyPropertyKeys = new Dictionary<string, string[]>
    {
        ["timeseries"] = ["upper_chart", "lower_chart"],
    };

    /// <summary>
    /// The number of findings, which is not always the feature count: a time-series result has
    /// one feature per grid cell, most of which did not change.
    /// </summary>
    public static int CountDetections(JsonElement geojson, string? analysisType)
    {
        var features = Features(geojson);
        if (analysisType != "timeseries")
        {
            return features.Count;
        }
        var deviations = features.Select(Deviation).ToList();
        if (deviations.All(d => d is null))
        {
            return features.Count; // unknown schema: better the feature count than "nothing found"
        }
        return deviations.Count(d => d is { } value && Math.Abs(value) >= SignificantDeviation);
    }

    public static int CountDetections(string geojson, string? analysisType)
    {
        using var document = JsonDocument.Parse(geojson);
        return CountDetections(document.RootElement, analysisType);
    }

    /// <summary>Drop the bulk chart properties before the GeoJSON becomes features. Returns the text unchanged when there is nothing to drop.</summary>
    public static (string Text, int Removed) StripHeavyProperties(string geojson, string? analysisType)
    {
        if (analysisType is null || !HeavyPropertyKeys.TryGetValue(analysisType, out var heavy))
        {
            return (geojson, 0);
        }
        var root = JsonNode.Parse(geojson);
        if (root?["features"] is not JsonArray features)
        {
            return (geojson, 0);
        }
        var removed = 0;
        foreach (var feature in features)
        {
            if (feature?["properties"] is not JsonObject properties)
            {
                continue;
            }
            foreach (var key in heavy)
            {
                if (properties.Remove(key))
                {
                    removed++;
                }
            }
        }
        return removed == 0 ? (geojson, 0) : (root!.ToJsonString(), removed);
    }

    private static List<JsonElement> Features(JsonElement geojson) =>
        geojson.ValueKind == JsonValueKind.Object
        && geojson.TryGetProperty("features", out var features)
        && features.ValueKind == JsonValueKind.Array
            ? features.EnumerateArray().ToList()
            : [];

    private static double? Deviation(JsonElement feature) =>
        feature.ValueKind == JsonValueKind.Object
        && feature.TryGetProperty("properties", out var properties)
        && properties.ValueKind == JsonValueKind.Object
        && properties.TryGetProperty(DeviationKey, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
