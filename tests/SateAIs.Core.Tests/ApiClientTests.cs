using System.Net;
using System.Text.Json;
using SateAIs.Core.Api;
using Xunit;

namespace SateAIs.Core.Tests;

public class ApiClientTests
{
    private static readonly AnalysisRequest Range = new(
        AnalysisTypes.NewBuilding, "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", DateStart: "2026-01-01", DateEnd: "2026-06-30");

    private static SateAIsApiClient Client(FakeHandler handler) => new("sk_test_key", handler: handler);

    [Fact]
    public async Task Every_request_identifies_this_client_and_carries_the_key()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, "{}");
        await Client(handler).GetMeAsync();

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://api.spcsft.com/api/v1/me", sent.Uri.ToString());
        Assert.Equal("Bearer sk_test_key", sent.Headers["Authorization"]);
        Assert.Equal($"sateais-arcgis-addin/{ClientVersion.Current}", sent.Headers["X-SateAIs-Client"]);
        Assert.Equal(sent.Headers["X-SateAIs-Client"], sent.Headers["User-Agent"]);
        Assert.Equal("application/json", sent.Headers["Accept"]);
    }

    [Fact]
    public async Task Submit_posts_the_body_without_the_unset_fields()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, """{"job_id":"j-1","status":"pending"}""");
        var job = await Client(handler).SubmitAsync(Range);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.spcsft.com/api/v1/analyze/newbuilding", sent.Uri.ToString());
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.Equal("sentinel-1", body.GetProperty("satellite_id").GetString());
        Assert.Equal("2026-01-01", body.GetProperty("date_start").GetString());
        Assert.False(body.TryGetProperty("date", out _));
        Assert.False(body.TryGetProperty("orbit_direction", out _));
        Assert.Equal("j-1", job.JobId);
        Assert.Equal(JobStatus.Pending, job.Status);
    }

    [Fact]
    public async Task Preview_uses_the_preview_path_with_the_same_body()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, """{"credits":{"estimated":1.5,"balance":10,"sufficient":true}}""");
        var preview = await Client(handler).PreviewAsync(Range);

        Assert.Equal("https://api.spcsft.com/api/v1/analyze/newbuilding/preview", handler.Requests[0].Uri.ToString());
        Assert.Equal(1.5, preview.Credits!.Estimated);
        Assert.Null(preview.Coverage);
    }

    [Fact]
    public async Task An_incomplete_request_is_rejected_before_it_is_sent()
    {
        var handler = new FakeHandler();
        await Assert.ThrowsAsync<InvalidAnalysisRequestException>(() =>
            Client(handler).PreviewAsync(Range with { DateEnd = null }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task List_jobs_joins_statuses_and_passes_the_limit()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, """{"jobs":[{"job_id":"a","status":"completed"},{"job_id":"b","status":"failed"}]}""");
        var jobs = await Client(handler).ListJobsAsync(["pending", "processing"], 5);

        Assert.Equal("https://api.spcsft.com/api/v1/jobs?status=pending%2Cprocessing&limit=5", handler.Requests[0].Uri.ToString());
        Assert.Equal(["a", "b"], jobs.Select(j => j.JobId));
    }

    [Theory]
    [InlineData(400, typeof(ValidationException))]
    [InlineData(401, typeof(AuthenticationException))]
    [InlineData(402, typeof(InsufficientCreditsException))]
    [InlineData(403, typeof(PermissionDeniedException))]
    [InlineData(404, typeof(NotFoundException))]
    [InlineData(409, typeof(ConflictException))]
    [InlineData(410, typeof(NotFoundException))]
    [InlineData(413, typeof(PayloadTooLargeException))]
    [InlineData(429, typeof(RateLimitException))]
    [InlineData(500, typeof(ServerException))]
    [InlineData(503, typeof(ServerException))]
    [InlineData(418, typeof(ApiException))]
    public async Task Status_codes_map_to_exception_types(int status, Type expected)
    {
        var handler = new FakeHandler().Enqueue((HttpStatusCode)status, """{"error":{"code":"SOME_CODE","message":"server text"}}""");
        var exception = await Assert.ThrowsAnyAsync<ApiException>(() => Client(handler).GetJobAsync("j"));

        Assert.IsType(expected, exception);
        Assert.Equal(status, exception.StatusCode);
        Assert.Equal("SOME_CODE", exception.Code);
        Assert.Equal("server text", exception.ServerMessage);
    }

    [Theory]
    [InlineData("""{"error":"RATE_LIMITED","error_message":"slow down"}""", "RATE_LIMITED", "slow down")]
    [InlineData("""{"error_code":"VALIDATION_ERROR","message":"bad polygon"}""", "VALIDATION_ERROR", "bad polygon")]
    [InlineData("<html>gateway</html>", null, "Bad Request (body: <html>gateway</html>)")]
    [InlineData("", null, "Bad Request")]
    public void Error_bodies_in_every_shape_are_read(string body, string? code, string message)
    {
        Assert.Equal((code, message), SateAIsApiClient.ParseError(body, "Bad Request"));
    }

    [Fact]
    public async Task The_result_follows_the_redirect_to_storage_without_the_bearer_token()
    {
        var handler = new FakeHandler()
            .EnqueueRedirect("https://bucket.s3.amazonaws.com/results/j.geojson?X-Amz-Signature=abc")
            .Enqueue(HttpStatusCode.OK, """{"type":"FeatureCollection","features":[]}""", "application/geo+json");

        var text = await Client(handler).GetJobResultAsync("j");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://api.spcsft.com/api/v1/jobs/j/result.geojson", handler.Requests[0].Uri.ToString());
        Assert.True(handler.Requests[0].Headers.ContainsKey("Authorization"));
        Assert.StartsWith("https://bucket.s3.amazonaws.com/", handler.Requests[1].Uri.ToString());
        Assert.False(handler.Requests[1].Headers.ContainsKey("Authorization"));
        Assert.True(handler.Requests[1].Headers.ContainsKey("User-Agent"));
        Assert.Contains("FeatureCollection", text);
    }

    [Fact]
    public async Task A_redirect_within_the_api_keeps_the_token()
    {
        var handler = new FakeHandler()
            .EnqueueRedirect("https://api.spcsft.com/api/v1/jobs/j/result.geojson?v=2")
            .Enqueue(HttpStatusCode.OK, "{}");

        await Client(handler).GetJobResultAsync("j");

        Assert.True(handler.Requests[1].Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task A_result_that_is_not_geojson_is_reported_as_such()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, "PK\u0003\u0004", "application/zip");
        var exception = await Assert.ThrowsAsync<UnsupportedResultFormatException>(() => Client(handler).GetJobResultAsync("j"));
        Assert.Equal("application/zip", exception.ContentType);
    }

    [Fact]
    public async Task A_missing_content_type_is_not_a_rejection()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, """{"type":"FeatureCollection"}""", contentType: null);
        Assert.Contains("FeatureCollection", await Client(handler).GetJobResultAsync("j"));
    }

    [Fact]
    public async Task Connection_failures_become_status_zero()
    {
        var handler = new FakeHandler().EnqueueThrow(new HttpRequestException("refused"));
        var exception = await Assert.ThrowsAsync<ApiException>(() => Client(handler).GetMeAsync());
        Assert.Equal(0, exception.StatusCode);
        Assert.StartsWith("connection error", exception.ServerMessage);
    }

    [Fact]
    public async Task Invalid_json_on_success_is_an_error_not_a_crash()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, "not json");
        var exception = await Assert.ThrowsAsync<ApiException>(() => Client(handler).GetMeAsync());
        Assert.StartsWith("invalid JSON response", exception.ServerMessage);
    }

    [Fact]
    public async Task Me_reads_the_balance_from_credits()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, """{"email":"a@b.c","credits":{"balance":480.5}}""");
        var account = await Client(handler).GetMeAsync();
        Assert.Equal("a@b.c", account.Email);
        Assert.Equal(480.5, account.CreditBalance);
    }

    [Fact]
    public void An_unsupported_scheme_is_refused()
    {
        Assert.Throws<ApiException>(() => new SateAIsApiClient("k", "ftp://example.com"));
    }
}
