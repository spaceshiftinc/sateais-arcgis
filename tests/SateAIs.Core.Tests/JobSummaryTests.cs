using SateAIs.Core.Jobs;
using SateAIs.Core.Wording;
using Xunit;

namespace SateAIs.Core.Tests;

public class JobSummaryTests
{
    private const string SceneId = "S1A_IW_GRDH_1SDV_20260101T123456_20260101T123521_051234_062ABC_1234";

    private static TrackedJob Job(
        string analysisType = "timeseries",
        string? sceneId = null,
        string? date = null,
        string? dateStart = null,
        string? dateEnd = null,
        string requestSource = "local") => new()
    {
        JobId = "00000000-1111-2222-3333-444444444444",
        AnalysisType = analysisType,
        SubmittedAt = "2026-07-30T05:12:00+00:00",
        SceneId = sceneId,
        Date = date,
        DateStart = dateStart,
        DateEnd = dateEnd,
        RequestSource = requestSource,
    };

    [Fact]
    public void A_sentinel1_scene_id_is_shortened_to_platform_and_date()
    {
        Assert.Equal("S1A 2026-01-01", JobSummary.FormatSceneId(SceneId));
    }

    [Fact]
    public void Other_scene_ids_are_kept_or_cut_to_length()
    {
        Assert.Equal("SOME_SCENE", JobSummary.FormatSceneId("SOME_SCENE"));
        var shortened = JobSummary.FormatSceneId(new string('X', 60));
        Assert.Equal(30, shortened.Length);
        Assert.EndsWith("…", shortened);
        Assert.Equal("S1A_IW_GRDH_1SDV_NOTADATE", JobSummary.FormatSceneId("S1A_IW_GRDH_1SDV_NOTADATE"));
        Assert.Equal("", JobSummary.FormatSceneId(""));
        Assert.Equal("", JobSummary.FormatSceneId(null));
    }

    [Fact]
    public void The_request_line_names_what_each_date_is()
    {
        Assert.Equal("Period 2026-01-03 → 2026-07-03", JobSummary.BuildRequestSummary(Job(dateStart: "2026-01-03", dateEnd: "2026-07-03")));
        Assert.Equal("Scene S1A 2026-01-01", JobSummary.BuildRequestSummary(Job("ship", sceneId: SceneId)));
        Assert.Equal("Reference date 2026-01-01", JobSummary.BuildRequestSummary(Job("ship", date: "2026-01-01")));
    }

    [Fact]
    public void The_request_line_is_empty_when_nothing_or_only_half_is_known()
    {
        Assert.Equal("", JobSummary.BuildRequestSummary(Job()));
        Assert.Equal("", JobSummary.BuildRequestSummary(Job(dateStart: "2026-01-03")));
    }

    [Fact]
    public void The_tooltip_carries_the_full_ids_and_the_period()
    {
        var job = Job("ship", sceneId: SceneId);
        var tooltip = JobSummary.BuildRequestTooltip(job);
        Assert.Contains(job.JobId, tooltip);
        Assert.Contains(SceneId, tooltip);
        Assert.Contains("Period: 2026-01-03 → 2026-07-03", JobSummary.BuildRequestTooltip(Job(dateStart: "2026-01-03", dateEnd: "2026-07-03")));
    }

    [Fact]
    public void The_tooltip_prompts_for_a_refresh_only_while_the_request_is_unresolved()
    {
        Assert.Contains(JobSummary.MissingRequestHint, JobSummary.BuildRequestTooltip(Job(requestSource: "")));
        Assert.DoesNotContain(JobSummary.MissingRequestHint,
            JobSummary.BuildRequestTooltip(Job(dateStart: "2026-01-03", dateEnd: "2026-07-03", requestSource: "server")));
    }

    [Fact]
    public void The_tooltip_is_plain_text()
    {
        var tooltip = JobSummary.BuildRequestTooltip(Job("<b>oops</b>", sceneId: "<i>x</i>"));
        Assert.Contains("<b>oops</b>", tooltip);
        Assert.DoesNotContain("<html>", tooltip.ToLowerInvariant());
    }

    [Fact]
    public void The_search_text_matches_id_fragments_both_type_spellings_dates_and_scene()
    {
        var job = new TrackedJob
        {
            JobId = "7cf9025b-a262-491d-9d5e-2ba638448273",
            AnalysisType = "newbuilding",
            SubmittedAt = "2026-09-03T00:12:12Z",
            DateStart = "2026-07-26",
            DateEnd = "2026-09-02",
            SceneId = "S1A_IW_GRDH_1SDV_20260101T123456_X",
        };
        var haystack = JobSummary.BuildSearchText(job);
        foreach (var fragment in new[] { "7cf9", "7cf9025b", "a262-491d", "2ba638448273", job.JobId, "newbuilding", "new building detection", "2026-07-26", "s1a_iw_grdh" })
        {
            Assert.Contains(fragment, haystack);
        }
        Assert.Equal(haystack, haystack.ToLowerInvariant());
    }

    [Fact]
    public void Missing_context_does_not_break_the_search_text()
    {
        var job = Job();
        Assert.Contains(job.JobId, JobSummary.BuildSearchText(job));
    }
}

public class ResultNamesTests
{
    [Fact]
    public void Layer_names_read_type_short_id_date_and_count()
    {
        Assert.Equal("SateAIs ship a1b2c3d4 (2026-07-03) · 23", ResultNames.LayerName("ship", "a1b2c3d4-0000-0000-0000-000000000000", "2026-07-03T01:02:03Z", 23));
        Assert.Equal("SateAIs ship a1b2c3d4 (2026-07-03)", ResultNames.LayerName("ship", "a1b2c3d4-0000", "2026-07-03T01:02:03Z"));
        Assert.Equal("SateAIs ship unknown (n/a)", ResultNames.LayerName("ship", "", null));
        Assert.Equal("SateAIs AOI ship a1b2c3d4", ResultNames.AoiLayerName("ship", "a1b2c3d4-0000"));
    }

    [Fact]
    public void Feature_class_names_are_geodatabase_safe()
    {
        Assert.Equal("SateAIs_ship_a1b2c3d4", ResultNames.FeatureClassName("ship", "a1b2c3d4-0000"));
        Assert.Equal("SateAIs_AOI_new_type_x_y", ResultNames.AoiFeatureClassName("new-type", "x y"));
    }
}
