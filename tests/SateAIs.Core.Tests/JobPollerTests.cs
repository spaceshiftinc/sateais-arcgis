using SateAIs.Core.Api;
using SateAIs.Core.Credentials;
using SateAIs.Core.Jobs;
using Xunit;

namespace SateAIs.Core.Tests;

public class JobPollerTests
{
    /// <summary>Answers each job from a script, one snapshot per poll; the last snapshot repeats.</summary>
    private sealed class FakeJobs
    {
        private readonly Dictionary<string, Queue<Job>> _script = new();

        public Dictionary<string, Exception> Exceptions { get; } = new();

        public int Calls { get; private set; }

        public FakeJobs Script(string jobId, params JobStatus[] statuses)
        {
            _script[jobId] = new Queue<Job>(statuses.Select(s => new Job(jobId, s)));
            return this;
        }

        public FakeJobs Script(string jobId, params Job[] jobs)
        {
            _script[jobId] = new Queue<Job>(jobs);
            return this;
        }

        public Task<Job> Fetch(string jobId, CancellationToken _)
        {
            Calls++;
            if (Exceptions.TryGetValue(jobId, out var e))
            {
                throw e;
            }
            if (!_script.TryGetValue(jobId, out var queue) || queue.Count == 0)
            {
                return Task.FromResult(new Job(jobId, JobStatus.Pending));
            }
            return Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        }
    }

    private static Task NoDelay(TimeSpan _, CancellationToken ct) => ct.IsCancellationRequested ? Task.FromCanceled(ct) : Task.CompletedTask;

    private static JobPoller Poller(FakeJobs jobs, TimeSpan? maxTracking = null) =>
        new(jobs.Fetch, delay: NoDelay, maxTracking: maxTracking);

    [Fact]
    public async Task Reports_each_new_status_and_the_completion()
    {
        var jobs = new FakeJobs().Script("a", JobStatus.Pending, JobStatus.Processing, JobStatus.Completed);
        var poller = Poller(jobs);
        var statuses = new List<(string, string)>();
        var completed = new List<string>();
        poller.StatusChanged += (id, s) => statuses.Add((id, s));
        poller.Completed += completed.Add;

        poller.Track(["a"]);
        await poller.Loop!;

        Assert.Equal([("a", "pending"), ("a", "processing"), ("a", "completed")], statuses);
        Assert.Equal(["a"], completed);
        Assert.False(poller.HasPending);
    }

    [Fact]
    public async Task Reports_a_failure_with_its_details()
    {
        var jobs = new FakeJobs().Script("x", new Job("x", JobStatus.Failed, ErrorCode: "VALIDATION_ERROR", ErrorMessage: "bad"));
        var poller = Poller(jobs);
        var failures = new List<(string, string, string)>();
        poller.Failed += (id, code, message) => failures.Add((id, code, message));

        poller.Track(["x"]);
        await poller.Loop!;

        Assert.Equal([("x", "VALIDATION_ERROR", "bad")], failures);
    }

    [Fact]
    public async Task A_missing_key_stops_the_loop_and_keeps_the_jobs_for_later()
    {
        var jobs = new FakeJobs();
        jobs.Exceptions["a"] = new AuthNotConfiguredException();
        var poller = Poller(jobs);
        var missing = 0;
        poller.AuthMissing += () => missing++;

        poller.Track(["a"]);
        await poller.Loop!;
        Assert.Equal(1, missing);
        Assert.True(poller.HasPending);

        // The key arrives; Refresh re-arms tracking and the same loop machinery picks the job up.
        jobs.Exceptions.Clear();
        jobs.Script("a", JobStatus.Completed);
        var completed = new List<string>();
        poller.Completed += completed.Add;
        poller.Track(["a"]);
        await poller.Loop!;
        Assert.Equal(["a"], completed);
    }

    [Fact]
    public async Task A_transient_error_does_not_drop_the_job()
    {
        var jobs = new FakeJobs().Script("a", JobStatus.Completed);
        jobs.Exceptions["a"] = new AuthenticationException("UNAUTHORIZED", "boom");
        var poller = new JobPoller(jobs.Fetch, delay: (_, _) =>
        {
            jobs.Exceptions.Clear(); // the next round succeeds
            return Task.CompletedTask;
        });
        var completed = new List<string>();
        poller.Completed += completed.Add;

        poller.Track(["a"]);
        await poller.Loop!;

        Assert.Equal(["a"], completed);
    }

    [Fact]
    public async Task Tracking_more_jobs_extends_the_set()
    {
        var jobs = new FakeJobs().Script("a", JobStatus.Completed).Script("b", JobStatus.Completed);
        var poller = Poller(jobs);
        var completed = new List<string>();
        poller.Completed += completed.Add;

        poller.Track(["a"]);
        poller.Track(["b"]);
        await poller.Loop!;

        Assert.Equal(["a", "b"], completed.Order());
    }

    [Fact]
    public async Task Five_errors_in_a_row_abandon_the_job()
    {
        var jobs = new FakeJobs();
        jobs.Exceptions["a"] = new ServerException(503, null, "down");
        var poller = Poller(jobs);
        var abandoned = new List<(string, AbandonReason)>();
        poller.Abandoned += (id, reason) => abandoned.Add((id, reason));

        poller.Track(["a"]);
        await poller.Loop!;

        Assert.Equal([("a", AbandonReason.Errors)], abandoned);
        Assert.Equal(JobPoller.MaxConsecutiveErrors, jobs.Calls);
        Assert.False(poller.HasPending);
    }

    [Fact]
    public async Task A_successful_poll_resets_the_error_budget()
    {
        var remaining = JobPoller.MaxConsecutiveErrors - 1;
        var poller = new JobPoller((id, _) =>
        {
            if (remaining-- > 0)
            {
                throw new ServerException(502, null, "flaky");
            }
            return Task.FromResult(new Job(id, JobStatus.Completed));
        }, delay: NoDelay);
        var completed = new List<string>();
        var abandoned = new List<string>();
        poller.Completed += completed.Add;
        poller.Abandoned += (id, _) => abandoned.Add(id);

        poller.Track(["a"]);
        await poller.Loop!;

        Assert.Equal(["a"], completed);
        Assert.Empty(abandoned);
    }

    [Fact]
    public async Task A_job_followed_for_too_long_is_abandoned()
    {
        var jobs = new FakeJobs().Script("a", JobStatus.Pending);
        var poller = Poller(jobs, maxTracking: TimeSpan.FromSeconds(-1));
        var abandoned = new List<(string, AbandonReason)>();
        poller.Abandoned += (id, reason) => abandoned.Add((id, reason));

        poller.Track(["a"]);
        await poller.Loop!;

        Assert.Equal([("a", AbandonReason.Expired)], abandoned);
        Assert.False(poller.HasPending);
    }

    [Fact]
    public async Task A_job_the_server_does_not_know_is_dropped_on_the_first_answer()
    {
        var jobs = new FakeJobs();
        jobs.Exceptions["a"] = new NotFoundException(410, "GONE", "result deleted");
        var poller = Poller(jobs);
        var abandoned = new List<(string, AbandonReason)>();
        poller.Abandoned += (id, reason) => abandoned.Add((id, reason));

        poller.Track(["a"]);
        await poller.Loop!;

        Assert.Equal([("a", AbandonReason.Gone)], abandoned);
        Assert.Equal(1, jobs.Calls);
        Assert.False(poller.HasPending);
    }

    [Fact]
    public async Task One_missing_job_does_not_stop_the_others()
    {
        var jobs = new FakeJobs().Script("b", JobStatus.Completed);
        jobs.Exceptions["a"] = new NotFoundException(404, "NOT_FOUND", "no such job");
        var poller = Poller(jobs);
        var abandoned = new List<(string, AbandonReason)>();
        var completed = new List<string>();
        poller.Abandoned += (id, reason) => abandoned.Add((id, reason));
        poller.Completed += completed.Add;

        poller.Track(["a", "b"]);
        await poller.Loop!;

        Assert.Equal([("a", AbandonReason.Gone)], abandoned);
        Assert.Equal(["b"], completed);
    }

    [Fact]
    public async Task Stop_ends_the_loop_between_rounds()
    {
        var gate = new TaskCompletionSource();
        var poller = new JobPoller(
            (id, _) => Task.FromResult(new Job(id, JobStatus.Pending)),
            delay: (_, ct) => gate.Task.WaitAsync(ct));
        poller.Track(["a"]);
        Assert.False(poller.Loop!.IsCompleted);

        poller.Stop();
        await poller.Loop!;

        Assert.True(poller.HasPending); // not finished, just stopped
    }
}
