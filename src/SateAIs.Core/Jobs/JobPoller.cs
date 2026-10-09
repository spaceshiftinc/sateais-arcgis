using SateAIs.Core.Api;
using SateAIs.Core.Credentials;

namespace SateAIs.Core.Jobs;

/// <summary>Why the poller stopped following a job before it finished.</summary>
public enum AbandonReason
{
    /// <summary>Too many status requests failed in a row.</summary>
    Errors,

    /// <summary>The job has been followed for the maximum time.</summary>
    Expired,

    /// <summary>The server does not know the job (404 / 410), which no retry will change.</summary>
    Gone,
}

/// <summary>
/// Follows running jobs until each reaches a terminal state. One loop serves every tracked job;
/// a job that keeps failing or keeps running is given up so the loop cannot run forever.
/// </summary>
public sealed class JobPoller : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultMaxTracking = TimeSpan.FromHours(24);
    public const int MaxConsecutiveErrors = 5;

    private static readonly HashSet<string> Terminal = ["completed", "failed"];

    private readonly Func<string, CancellationToken, Task<Job>> _fetch;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _maxTracking;
    private readonly object _gate = new();
    private readonly List<string> _pending = [];
    private readonly Dictionary<string, string> _lastStatus = [];
    private readonly Dictionary<string, int> _errors = [];
    private readonly Dictionary<string, DateTimeOffset> _firstSeen = [];
    private CancellationTokenSource? _cts;
    private int _loopId;
    private bool _running;

    public JobPoller(
        Func<string, CancellationToken, Task<Job>> fetch,
        TimeSpan? interval = null,
        TimeSpan? maxTracking = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? clock = null)
    {
        _fetch = fetch;
        _interval = interval ?? DefaultInterval;
        _maxTracking = maxTracking ?? DefaultMaxTracking;
        _delay = delay ?? Task.Delay;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The server reported a status this poller had not seen for the job.</summary>
    public event Action<string, string>? StatusChanged;

    public event Action<string>? Completed;

    /// <summary>(job id, error code, error message), the last two possibly empty.</summary>
    public event Action<string, string, string>? Failed;

    public event Action<string, AbandonReason>? Abandoned;

    /// <summary>Polling stopped because no API key is configured. The jobs stay queued for the next <see cref="Track"/>.</summary>
    public event Action? AuthMissing;

    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count > 0;
            }
        }
    }

    /// <summary>The current loop, for tests to await. Null before the first <see cref="Track"/>.</summary>
    internal Task? Loop { get; private set; }

    /// <summary>Follow these jobs. Starts the loop when none is running; otherwise the running loop picks them up.</summary>
    public void Track(IEnumerable<string> jobIds)
    {
        lock (_gate)
        {
            foreach (var jobId in jobIds)
            {
                if (_pending.Contains(jobId) || (_lastStatus.TryGetValue(jobId, out var last) && Terminal.Contains(last)))
                {
                    continue;
                }
                _firstSeen.TryAdd(jobId, _clock());
                _pending.Add(jobId);
            }
            if (_pending.Count == 0 || _running)
            {
                return;
            }
            _running = true;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            Loop = RunAsync(++_loopId, _cts.Token);
        }
    }

    public void Stop() => _cts?.Cancel();

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    private async Task RunAsync(int loopId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string[] snapshot;
                lock (_gate)
                {
                    snapshot = _pending.ToArray();
                }
                foreach (var jobId in snapshot)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    if (_clock() - FirstSeen(jobId) > _maxTracking)
                    {
                        Drop(jobId);
                        Abandoned?.Invoke(jobId, AbandonReason.Expired);
                        continue;
                    }

                    Job job;
                    try
                    {
                        job = await _fetch(jobId, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (AuthNotConfiguredException)
                    {
                        AuthMissing?.Invoke();
                        return;
                    }
                    catch (NotFoundException)
                    {
                        // A permanent answer: retrying, then asking the user to refresh, would repeat something that cannot succeed.
                        Drop(jobId);
                        Abandoned?.Invoke(jobId, AbandonReason.Gone);
                        continue;
                    }
                    catch (Exception)
                    {
                        if (RegisterError(jobId))
                        {
                            Abandoned?.Invoke(jobId, AbandonReason.Errors);
                        }
                        continue;
                    }

                    var status = job.Status.WireValue();
                    bool changed;
                    lock (_gate)
                    {
                        _errors.Remove(jobId); // a good answer resets the budget
                        changed = !_lastStatus.TryGetValue(jobId, out var previous) || previous != status;
                        _lastStatus[jobId] = status;
                    }
                    if (changed)
                    {
                        StatusChanged?.Invoke(jobId, status);
                    }
                    if (job.IsCompleted)
                    {
                        Drop(jobId);
                        Completed?.Invoke(jobId);
                    }
                    else if (job.IsFailed)
                    {
                        Drop(jobId);
                        Failed?.Invoke(jobId, job.ErrorCode ?? "", job.ErrorMessage ?? "");
                    }
                }

                if (TryFinish(loopId))
                {
                    return;
                }
                await _delay(_interval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
        finally
        {
            lock (_gate)
            {
                if (_loopId == loopId)
                {
                    _running = false;
                }
            }
        }
    }

    /// <summary>Ends the loop when nothing is left, under the lock so a concurrent Track either sees it running or restarts it.</summary>
    private bool TryFinish(int loopId)
    {
        lock (_gate)
        {
            if (_pending.Count > 0)
            {
                return false;
            }
            if (_loopId == loopId)
            {
                _running = false;
            }
            return true;
        }
    }

    private DateTimeOffset FirstSeen(string jobId)
    {
        lock (_gate)
        {
            if (!_firstSeen.TryGetValue(jobId, out var seen))
            {
                seen = _clock();
                _firstSeen[jobId] = seen;
            }
            return seen;
        }
    }

    /// <summary>Count a failed poll; true when the job is dropped because too many failed in a row.</summary>
    private bool RegisterError(string jobId)
    {
        lock (_gate)
        {
            var count = _errors.GetValueOrDefault(jobId) + 1;
            _errors[jobId] = count;
            if (count < MaxConsecutiveErrors)
            {
                return false;
            }
        }
        Drop(jobId);
        return true;
    }

    private void Drop(string jobId)
    {
        lock (_gate)
        {
            _pending.Remove(jobId);
            _errors.Remove(jobId);
            _firstSeen.Remove(jobId);
        }
    }
}
