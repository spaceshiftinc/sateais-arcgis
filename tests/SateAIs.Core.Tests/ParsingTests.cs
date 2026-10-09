using System.Text.Json;
using SateAIs.Core.Api;
using Xunit;

namespace SateAIs.Core.Tests;

public class ParsingTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void Full_preview_payload()
    {
        var preview = Parsing.PreviewFromJson(Json("""
            {"endpoint_id":"newbuilding","area_sqkm":78.4,
             "coverage":{"method":"estimated","requested_area_sqkm":100.2,"ratio":0.78,"polygon":"POLYGON ((0 0, 1 0, 1 1, 0 0))"},
             "credits":{"estimated":1.0,"balance":480.0,"sufficient":true},
             "warnings":[{"code":"LOW_AOI_COVERAGE","message":"Scenes cover only 78% of the requested area."}]}
            """));
        Assert.Equal("newbuilding", preview.EndpointId);
        Assert.Equal(78.4, preview.AreaSqkm);
        Assert.Equal(0.78, preview.Coverage!.Ratio);
        Assert.StartsWith("POLYGON", preview.Coverage.Polygon);
        Assert.True(preview.Credits!.Sufficient);
        Assert.Single(preview.Warnings);
    }

    [Fact]
    public void Missing_coverage_is_unknown_and_null_estimate_stays_null()
    {
        var preview = Parsing.PreviewFromJson(Json("""{"credits":{"estimated":null,"balance":480.0,"sufficient":null}}"""));
        Assert.Null(preview.Coverage);
        Assert.Null(preview.Credits!.Estimated);
        Assert.Null(preview.Credits.Sufficient);
    }

    [Fact]
    public void Garbage_is_safe()
    {
        var preview = Parsing.PreviewFromJson(Json("""{"credits":"?","coverage":3,"warnings":["x",{"code":"A","message":"m"}],"area_sqkm":true}"""));
        Assert.Null(preview.Credits);
        Assert.Null(preview.Coverage);
        Assert.Null(preview.AreaSqkm);
        Assert.Equal([new ApiWarning("A", "m")], preview.Warnings);
    }

    [Fact]
    public void An_empty_polygon_is_no_polygon()
    {
        var preview = Parsing.PreviewFromJson(Json("""{"coverage":{"ratio":1.0,"polygon":""}}"""));
        Assert.Null(preview.Coverage!.Polygon);
    }

    [Fact]
    public void Job_fields_are_read_and_the_error_key_falls_back()
    {
        var job = Parsing.JobFromJson(Json("""
            {"job_id":"j","status":"failed","error":"SCENE_NOT_FOUND","error_message":"none","endpoint_id":"ship",
             "coverage":{"polygon":"POLYGON ((0 0, 1 0, 1 1, 0 0))"},"area_sqkm":12.5,"credits_used":0.13,
             "request_params":{"date":"2026-01-01"}}
            """));
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("SCENE_NOT_FOUND", job.ErrorCode);
        Assert.Equal("ship", job.EndpointId);
        Assert.StartsWith("POLYGON", job.CoveragePolygon);
        Assert.Equal(0.13, job.CreditsUsed);
        Assert.Equal("2026-01-01", job.RequestParams!.Value.GetProperty("date").GetString());
        Assert.True(job.IsTerminal && job.IsFailed && !job.IsCompleted);
    }

    [Theory]
    [InlineData("""{"status":"pending"}""")]
    [InlineData("""{"job_id":"","status":"pending"}""")]
    [InlineData("""{"job_id":123,"status":"pending"}""")]
    public void A_job_without_an_id_is_an_error(string text)
    {
        Assert.Throws<ApiException>(() => Parsing.JobFromJson(Json(text)));
    }

    [Fact]
    public void Unknown_statuses_are_unknown()
    {
        Assert.Equal(JobStatus.Unknown, JobStatuses.Parse("queued"));
        Assert.Equal("unknown", JobStatus.Unknown.WireValue());
    }
}
