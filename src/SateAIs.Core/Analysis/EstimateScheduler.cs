using SateAIs.Core.Api;

namespace SateAIs.Core.Analysis;

/// <summary>
/// Runs the pre-run estimate for the current inputs: waits for typing to settle, discards a
/// response that arrives after the inputs changed again, and retries one failure silently.
/// </summary>
public sealed class EstimateScheduler
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(600);

    private readonly Func<CancellationToken, Task<Preview>> _fetch;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _debounce;
    private CancellationTokenSource? _pending;
    private int _generation;

    public EstimateScheduler(
        Func<CancellationToken, Task<Preview>> fetch,
        TimeSpan? debounce = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _fetch = fetch;
        _debounce = debounce ?? DefaultDebounce;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>The request is about to go out: show that the estimate is being checked.</summary>
    public event Action? Checking;

    public event Action<Preview>? Received;

    /// <summary>Both attempts failed; the argument is the second failure.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Forget the pending estimate. A response still in flight will be discarded.</summary>
    public void Cancel()
    {
        _generation++;
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
    }

    /// <summary>Estimate the current inputs once they have settled.</summary>
    public void Schedule()
    {
        Cancel();
        var cts = new CancellationTokenSource();
        _pending = cts;
        _ = RunAsync(_generation, cts.Token);
    }

    private async Task RunAsync(int generation, CancellationToken ct)
    {
        try
        {
            await _delay(_debounce, ct);
            if (generation != _generation)
            {
                return;
            }
            Checking?.Invoke();

            Exception? failure = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var preview = await _fetch(ct);
                    if (generation == _generation)
                    {
                        Received?.Invoke(preview);
                    }
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    failure = e;
                }
                if (attempt == 0)
                {
                    // Once more, quietly: a cold start often fails the first time and answers the second.
                    await _delay(_debounce, ct);
                    if (generation != _generation)
                    {
                        return;
                    }
                }
            }
            if (generation == _generation)
            {
                Failed?.Invoke(failure!);
            }
        }
        catch (OperationCanceledException)
        {
            // cancelled by a newer schedule
        }
    }
}
