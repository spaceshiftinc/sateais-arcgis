namespace SateAIs.Core.Api;

/// <summary>Detection types, by the identifier the API uses.</summary>
public static class AnalysisTypes
{
    public const string Ship = "ship";
    public const string OilSlick = "oilslick";
    public const string NewBuilding = "newbuilding";
    public const string DisappearBuilding = "disappearbuilding";
    public const string TimeSeries = "timeseries";

    public static readonly IReadOnlyList<string> All = [Ship, OilSlick, NewBuilding, DisappearBuilding, TimeSeries];

    /// <summary>Ship and oil slick take one reference date; the rest take a date range.</summary>
    public static bool TakesSingleDate(string analysisType) => analysisType is Ship or OilSlick;

    public static bool IsKnown(string? analysisType) => analysisType is not null && All.Contains(analysisType);
}

/// <summary>What is sent to <c>POST /analyze/{type}</c> and to its preview.</summary>
public sealed record AnalysisRequest(
    string AnalysisType,
    string Polygon,
    string? Date = null,
    string? DateDirection = null,
    string? DateStart = null,
    string? DateEnd = null,
    string? OrbitDirection = null,
    string SatelliteId = "sentinel-1")
{
    public void Validate()
    {
        if (!AnalysisTypes.IsKnown(AnalysisType))
        {
            throw new InvalidAnalysisRequestException($"unknown analysis type: {AnalysisType}");
        }
        if (string.IsNullOrWhiteSpace(Polygon))
        {
            throw new InvalidAnalysisRequestException($"{AnalysisType}: polygon is required");
        }
        if (AnalysisTypes.TakesSingleDate(AnalysisType))
        {
            if (string.IsNullOrEmpty(Date))
            {
                throw new InvalidAnalysisRequestException($"{AnalysisType}: date is required");
            }
        }
        else if (string.IsNullOrEmpty(DateStart) || string.IsNullOrEmpty(DateEnd))
        {
            throw new InvalidAnalysisRequestException($"{AnalysisType}: date_start and date_end are required");
        }
    }

    /// <summary>The request body, without the fields that are not set.</summary>
    public Dictionary<string, string> ToBody()
    {
        var body = new Dictionary<string, string>
        {
            ["satellite_id"] = SatelliteId,
            ["polygon"] = Polygon,
        };
        Put(body, "date", Date);
        Put(body, "date_direction", DateDirection);
        Put(body, "date_start", DateStart);
        Put(body, "date_end", DateEnd);
        Put(body, "orbit_direction", OrbitDirection);
        return body;

        static void Put(Dictionary<string, string> body, string key, string? value)
        {
            if (value is not null)
            {
                body[key] = value;
            }
        }
    }
}
