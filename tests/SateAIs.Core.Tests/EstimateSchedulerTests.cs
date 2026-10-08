using SateAIs.Core.Analysis;
using SateAIs.Core.Api;
using Xunit;

namespace SateAIs.Core.Tests;

public class EstimateSchedulerTests
{
    /// <summary>A delay the test releases by hand, so timing is deterministic.</summary>
    private sealed class ManualDelay
    {
        private readonly Queue<TaskCompletionSource> _waits = new();

        public int Count => _waits.Count;

        public Task Delay(TimeSpan _, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource();
            ct.Register(() => tcs.TrySetCanceled());
            _waits.Enqueue(tcs);
            return tcs.Task;
        }

        /// <summary>Release the oldest wait that is still live; cancelled ones are skipped.</summary>
        public void Release()
        {
            while (_waits.Count > 0 && !_waits.Dequeue().TrySetResult())
            {
            }
        }
    }

    private static Preview PreviewOf(double credits) => new() { Credits = new PreviewCredits(Estimated: credits) };

    [Fact]
    public async Task Waits_for_the_inputs_to_settle_then_fetches_once()
    {
        var delay = new ManualDelay();
        var fetches = 0;
        var received = new List<Preview>();
        var scheduler = new EstimateScheduler(_ => { fetches++; return Task.FromResult(PreviewOf(fetches)); }, delay: delay.Delay);
        scheduler.Received += received.Add;

        scheduler.Schedule();
        scheduler.Schedule();
        scheduler.Schedule();
        Assert.Equal(0, fetches);

        delay.Release();
        await Task.Yield();

        Assert.Equal(1, fetches);
        Assert.Single(received);
    }

    [Fact]
    public async Task A_response_for_old_inputs_is_discarded()
    {
        var delay = new ManualDelay();
        var gate = new TaskCompletionSource<Preview>();
        var received = new List<Preview>();
        var scheduler = new EstimateScheduler(_ => gate.Task, delay: delay.Delay);
        scheduler.Received += received.Add;

        scheduler.Schedule();
        delay.Release();
        await Task.Yield();
        scheduler.Cancel(); // the inputs changed while the request was in flight
        gate.SetResult(PreviewOf(1));
        await Task.Yield();

        Assert.Empty(received);
    }

    [Fact]
    public async Task One_failure_is_retried_quietly_and_the_second_is_reported()
    {
        var delay = new ManualDelay();
        var attempts = 0;
        var failures = new List<Exception>();
        var checking = 0;
        var scheduler = new EstimateScheduler(
            _ => { attempts++; throw new ServerException(503, null, "boom"); }, delay: delay.Delay);
        scheduler.Failed += failures.Add;
        scheduler.Checking += () => checking++;

        scheduler.Schedule();
        delay.Release();
        await Task.Yield();
        Assert.Equal(1, attempts);
        Assert.Empty(failures);

        delay.Release(); // the pause before the retry
        await Task.Yield();

        Assert.Equal(2, attempts);
        Assert.Single(failures);
        Assert.Equal(1, checking);
    }

    [Fact]
    public async Task Cancel_before_the_inputs_settle_sends_nothing()
    {
        var delay = new ManualDelay();
        var fetches = 0;
        var scheduler = new EstimateScheduler(_ => { fetches++; return Task.FromResult(PreviewOf(1)); }, delay: delay.Delay);

        scheduler.Schedule();
        scheduler.Cancel();
        await Task.Yield();

        Assert.Equal(0, fetches);
    }
}
