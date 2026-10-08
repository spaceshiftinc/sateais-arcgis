using SateAIs.Core.Analysis;
using Xunit;

namespace SateAIs.Core.Tests;

public class AnalysisFormTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);

    private static AnalysisForm Form() => new(() => Today);

    [Fact]
    public void Starts_with_todays_dates_and_nothing_else()
    {
        var form = Form();
        Assert.Null(form.AnalysisType);
        Assert.False(form.IsComplete);
        Assert.Equal(Today, form.Date);
        Assert.Equal(Today, form.DateEnd);
        Assert.Equal(new DateOnly(2026, 4, 8), form.DateStart);
        Assert.Null(form.BuildRequest());
    }

    [Fact]
    public void A_type_and_an_area_complete_the_form()
    {
        var form = Form();
        form.SetAnalysisType("newbuilding");
        Assert.False(form.IsComplete);
        form.SetPolygonFromMap("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", 12.5);
        Assert.True(form.IsComplete);
        Assert.Equal(12.5, form.AreaKm2);

        var request = form.BuildRequest()!;
        Assert.Equal("newbuilding", request.AnalysisType);
        Assert.Equal("2026-04-08", request.DateStart);
        Assert.Equal("2026-10-08", request.DateEnd);
        Assert.Null(request.Date);
        Assert.Null(request.OrbitDirection);
    }

    [Fact]
    public void Single_date_types_send_the_reference_date_and_direction()
    {
        var form = Form();
        form.SetAnalysisType("ship");
        form.SetPolygonText("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))");
        form.Date = new DateOnly(2026, 9, 1);
        form.DateDirection = "before";
        form.OrbitDirection = "descending";

        var request = form.BuildRequest()!;
        Assert.Equal("2026-09-01", request.Date);
        Assert.Equal("before", request.DateDirection);
        Assert.Equal("descending", request.OrbitDirection);
        Assert.Null(request.DateStart);
        Assert.Null(form.AreaKm2);
    }

    [Fact]
    public void Step_two_summary_reads_as_a_period_or_a_reference_date()
    {
        var form = Form();
        form.SetAnalysisType("timeseries");
        Assert.Equal("2026-04-08 → 2026-10-08", form.WhenSummary());
        form.SetAnalysisType("oilslick");
        Assert.Equal("2026-10-08 · nearest", form.WhenSummary());
        form.DateDirection = "after";
        Assert.Equal("2026-10-08 · after", form.WhenSummary());
    }

    [Fact]
    public void Presets_count_back_from_today()
    {
        var form = Form();
        form.ApplyPreset(24);
        Assert.Equal(Today, form.DateEnd);
        Assert.Equal(new DateOnly(2024, 10, 8), form.DateStart);
    }

    [Fact]
    public void Clear_keeps_the_type_and_resets_everything_else()
    {
        var form = Form();
        form.SetAnalysisType("ship");
        form.SetPolygonFromMap("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", 1);
        form.DateDirection = "before";
        form.OrbitDirection = "ascending";
        form.Date = new DateOnly(2020, 1, 1);

        form.Clear();

        Assert.Equal("ship", form.AnalysisType);
        Assert.False(form.HasPolygon);
        Assert.Null(form.DateDirection);
        Assert.Null(form.OrbitDirection);
        Assert.Equal(Today, form.Date);
    }

    [Fact]
    public void Whitespace_is_not_an_area()
    {
        var form = Form();
        form.SetAnalysisType("ship");
        form.SetPolygonText("   ");
        Assert.False(form.IsComplete);
    }
}
