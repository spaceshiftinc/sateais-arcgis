using System.Text.Json;
using SateAIs.Core.Jobs;
using Xunit;

namespace SateAIs.Core.Tests;

public class JobStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sateais-tests", Guid.NewGuid().ToString("N"));
    private readonly JobStore _store;

    public JobStoreTests()
    {
        _store = new JobStore(Path.Combine(_dir, "jobs.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Add_inserts_at_the_head_and_is_idempotent()
    {
        _store.Add("ship", "id-1", submittedAt: "2026-01-01T00:00:00+00:00");
        _store.Add("oilslick", "id-2", submittedAt: "2026-01-02T00:00:00+00:00");
        var again = _store.Add("ship", "id-1");

        Assert.Equal("id-1", again.JobId);
        Assert.Equal(["id-2", "id-1"], _store.ListAll().Select(j => j.JobId));
        Assert.Equal("pending", _store.ListAll()[0].Status);
    }

    [Fact]
    public void List_is_newest_first_with_unreadable_dates_last()
    {
        _store.Add("ship", "old", submittedAt: "2026-01-01T00:00:00Z");
        _store.Add("ship", "garbage", submittedAt: "when?");
        _store.Add("ship", "new", submittedAt: "2026-03-01T00:00:00Z");

        Assert.Equal(["new", "old", "garbage"], _store.ListAll().Select(j => j.JobId));
    }

    [Fact]
    public void Completion_figures_survive_the_next_write()
    {
        _store.Add("ship", "j-1");
        _store.UpdateStatus("j-1", "completed", completedAt: "2026-09-01T04:25:52Z", areaSqkm: 196.4, creditsUsed: 1.96);
        _store.Add("ship", "j-2");
        _store.UpdateStatus("j-1", "completed"); // a plain poll must not blank them

        var job = _store.ListAll().Single(j => j.JobId == "j-1");
        Assert.Equal("2026-09-01T04:25:52Z", job.CompletedAt);
        Assert.Equal(196.4, job.AreaSqkm);
        Assert.Equal(1.96, job.CreditsUsed);
    }

    [Fact]
    public void Request_context_keeps_the_drawn_polygon_and_follows_the_source_rules()
    {
        var request = new Dictionary<string, string?> { ["polygon"] = "POLYGON ((0 0, 1 0, 1 1, 0 0))", ["date"] = "2026-01-01", ["ignored"] = "x" };
        _store.Add("ship", "j", request: request, requestSource: "local");

        var echoed = new Dictionary<string, string?> { ["polygon"] = "POLYGON ((9 9, 9 9, 9 9, 9 9))", ["date"] = "2026-01-02" };
        var job = _store.SetRequestContext("j", echoed, "server")!;
        Assert.Equal("POLYGON ((0 0, 1 0, 1 1, 0 0))", job.Polygon);
        Assert.Equal("2026-01-02", job.Date);
        Assert.Equal("server", job.RequestSource);

        job = _store.SetRequestContext("j", null, "unavailable")!;
        Assert.Equal("server", job.RequestSource);

        job = _store.SetRequestContext("j", null, "")!;
        Assert.Equal("server", job.RequestSource);
    }

    [Fact]
    public void Unknown_statuses_are_stored_as_unknown()
    {
        _store.Add("ship", "j");
        Assert.Equal("unknown", _store.UpdateStatus("j", "queued")!.Status);
        Assert.Null(_store.UpdateStatus("missing", "completed"));
    }

    [Fact]
    public void Detection_count_remove_and_cleanup()
    {
        _store.Add("ship", "old", submittedAt: "2026-01-01T00:00:00Z");
        _store.Add("ship", "fresh", submittedAt: "2026-09-25T00:00:00Z");
        _store.Add("ship", "undated", submittedAt: "?");

        Assert.Equal(7, _store.SetDetectionCount("fresh", 7)!.DetectionCount);
        Assert.Equal(1, _store.CleanupExpired(30, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(["fresh", "undated"], _store.ListAll().Select(j => j.JobId));
        Assert.True(_store.Remove("undated"));
        Assert.False(_store.Remove("undated"));
    }

    [Fact]
    public void A_damaged_file_reads_as_empty_and_odd_values_as_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_store.FilePath, "not json");
        Assert.Empty(_store.ListAll());

        File.WriteAllText(_store.FilePath, """
            [{"job_id":"x","analysis_type":"ship","submitted_at":"2026-01-01T00:00:00+00:00","area_sqkm":"big","credits_used":true,"detection_count":-1,"status":5},
             {"analysis_type":"ship"}, "junk"]
            """);
        var job = Assert.Single(_store.ListAll());
        Assert.Null(job.AreaSqkm);
        Assert.Null(job.CreditsUsed);
        Assert.Null(job.DetectionCount);
        Assert.Equal("pending", job.Status);
    }

    [Fact]
    public void Request_from_json_keeps_only_strings()
    {
        var element = JsonDocument.Parse("""{"date":"2026-01-01","polygon":"P","nested":{"a":1},"n":3}""").RootElement.Clone();
        var request = JobStore.RequestFromJson(element);
        Assert.Equal(new Dictionary<string, string?> { ["date"] = "2026-01-01", ["polygon"] = "P" }, request);
        Assert.Empty(JobStore.RequestFromJson(null));
    }
}
