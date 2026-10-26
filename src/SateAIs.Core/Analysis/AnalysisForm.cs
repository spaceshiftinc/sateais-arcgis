using System.Globalization;
using SateAIs.Core.Api;

namespace SateAIs.Core.Analysis;

/// <summary>
/// What the user has set up: detection type, dates and area. The screen renders this; the
/// request body comes from it. Dates always have a value, so the type alone completes step ②.
/// </summary>
public sealed class AnalysisForm
{
    public const int DefaultPeriodMonths = 6;

    private readonly Func<DateOnly> _today;

    public AnalysisForm(Func<DateOnly>? today = null)
    {
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
        Clear();
    }

    public string? AnalysisType { get; private set; }

    public DateOnly Date { get; set; }

    public DateOnly DateStart { get; set; }

    public DateOnly DateEnd { get; set; }

    /// <summary>"before" / "after", or null for nearest.</summary>
    public string? DateDirection { get; set; }

    /// <summary>"ascending" / "descending", or null for any.</summary>
    public string? OrbitDirection { get; set; }

    /// <summary>WKT, drawn on the map or pasted.</summary>
    public string Polygon { get; set; } = "";

    /// <summary>Set when the area was drawn; a pasted polygon has no known area.</summary>
    public double? AreaKm2 { get; private set; }

    public bool HasType => AnalysisType is not null;

    public bool HasPolygon => Polygon.Trim().Length > 0;

    public bool IsComplete => HasType && HasPolygon;

    /// <summary>Building-change and time-series types compare two dates; ship and oil slick pick one scene.</summary>
    public bool NeedsPeriod => AnalysisType is not null && !AnalysisTypes.TakesSingleDate(AnalysisType);

    public void SetAnalysisType(string analysisType) => AnalysisType = analysisType;

    public void SetPolygonFromMap(string wkt, double areaKm2)
    {
        Polygon = wkt;
        AreaKm2 = areaKm2;
    }

    public void SetPolygonText(string wkt)
    {
        Polygon = wkt;
        AreaKm2 = null;
    }

    public void ClearPolygon() => SetPolygonText("");

    /// <summary>The last N months, ending today.</summary>
    public void ApplyPreset(int months)
    {
        DateEnd = _today();
        DateStart = DateEnd.AddMonths(-months);
    }

    /// <summary>What step ② shows once a type is chosen.</summary>
    public string WhenSummary()
    {
        if (NeedsPeriod)
        {
            return $"{Iso(DateStart)} → {Iso(DateEnd)}";
        }
        return $"{Iso(Date)} · {DateDirection ?? "nearest"}";
    }

    /// <summary>The request for the current inputs, or null while the form is incomplete.</summary>
    public AnalysisRequest? BuildRequest()
    {
        if (!IsComplete)
        {
            return null;
        }
        var polygon = Polygon.Trim();
        return NeedsPeriod
            ? new AnalysisRequest(AnalysisType!, polygon, DateStart: Iso(DateStart), DateEnd: Iso(DateEnd), OrbitDirection: OrbitDirection)
            : new AnalysisRequest(AnalysisType!, polygon, Date: Iso(Date), DateDirection: DateDirection, OrbitDirection: OrbitDirection);
    }

    /// <summary>After a submission: everything back to its default except the detection type.</summary>
    public void Clear()
    {
        var today = _today();
        Polygon = "";
        AreaKm2 = null;
        Date = today;
        DateStart = today.AddMonths(-DefaultPeriodMonths);
        DateEnd = today;
        DateDirection = null;
        OrbitDirection = null;
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
