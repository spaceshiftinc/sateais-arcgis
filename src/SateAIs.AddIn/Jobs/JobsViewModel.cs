using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using SateAIs.AddIn.Analysis;
using SateAIs.AddIn.Map;
using SateAIs.Core;
using SateAIs.Core.Api;
using SateAIs.Core.Credentials;
using SateAIs.Core.Jobs;
using SateAIs.Core.Wording;

namespace SateAIs.AddIn.Jobs;

/// <summary>
/// The Jobs tab: the tracked jobs as rows, kept current by polling, merged with the server on
/// Refresh, and turned into layers on click. Mirrors the QGIS plugin's panel.
/// </summary>
internal sealed class JobsViewModel : ObservableObject, IDisposable
{
    private static readonly HashSet<string> SyncableTypes = [.. AnalysisTypes.All];
    private static readonly string[] ActiveStatuses = ["pending", "processing", "unknown"];

    private readonly JobStore _store = new(JobStore.DefaultPath);
    private readonly JobPoller _poller;
    private readonly AoiOverlay _aoi = new();
    private readonly HashSet<string> _loading = [];
    private readonly List<string> _forgotten = [];
    private SateAIsApiClient? _client;
    private string? _clientKey;
    private string? _previewedJobId;
    private string _searchText = "";
    private bool _refreshing;

    public JobsViewModel()
    {
        _poller = new JobPoller((jobId, ct) => Client().GetJobAsync(jobId, ct));
        _poller.StatusChanged += (id, status) => Ui.OnUiThread(() => OnStatusChanged(id, status));
        _poller.Completed += id => Ui.OnUiThread(() => OnStatusChanged(id, "completed"));
        _poller.Failed += (id, code, message) => Ui.OnUiThread(() => OnFailed(id, code, message));
        _poller.Abandoned += (id, reason) => Ui.OnUiThread(() => OnAbandoned(id, reason));
        _poller.AuthMissing += () => Ui.OnUiThread(() =>
            Ui.Notify("Job statuses cannot be updated — no API key is configured. Open Settings from the Analysis tab to set one."));

        RefreshCommand = new DelegateCommand(_ => _ = RefreshAsync(), _ => !Refreshing);
        CardClickedCommand = new DelegateCommand(p => OnCardClicked((JobCardViewModel)p!));
        ClearSearchCommand = new DelegateCommand(_ => SearchText = "");
        StartAnalysisCommand = new DelegateCommand(_ => StartAnalysisRequested?.Invoke());

        _store.CleanupExpired();
        LoadExisting();
    }

    /// <summary>The empty state's call to action: take the user to the Analysis tab.</summary>
    public event Action? StartAnalysisRequested;

    public ObservableCollection<JobCardViewModel> Cards { get; } = [];

    public DelegateCommand RefreshCommand { get; }

    public DelegateCommand CardClickedCommand { get; }

    public DelegateCommand ClearSearchCommand { get; }

    public DelegateCommand StartAnalysisCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                Raise(nameof(SearchActive));
                ApplySearch();
            }
        }
    }

    public bool SearchActive => _searchText.Length > 0;

    /// <summary>"shown / total" while a search is active, so a filtered list never reads as lost jobs.</summary>
    public string MatchCountText { get; private set; } = "";

    public bool MatchCountVisible { get; private set; }

    public bool NoMatchVisible { get; private set; }

    public bool EmptyVisible => Cards.Count == 0;

    public bool Refreshing
    {
        get => _refreshing;
        private set
        {
            if (Set(ref _refreshing, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // --- hand-offs from the rest of the pane ------------------------------------------

    /// <summary>A job was just submitted from the Analysis tab.</summary>
    public void Add(string jobId, string analysisType, AnalysisRequest request)
    {
        var context = new Dictionary<string, string?>
        {
            ["polygon"] = request.Polygon,
            ["date"] = request.Date,
            ["date_start"] = request.DateStart,
            ["date_end"] = request.DateEnd,
        };
        var job = _store.Add(analysisType, jobId, request: context, requestSource: "local");
        InsertCard(job, prepend: true);
        _poller.Track([jobId]);
        RefreshEmptyState();
    }

    /// <summary>Keep the submitted area on the map, now as this job's area rather than the form's.</summary>
    public void ShowSubmittedArea(string jobId, string polygon)
    {
        if (string.IsNullOrEmpty(polygon))
        {
            return;
        }
        _ = Ui.Guard(ShowAsync());

        async Task ShowAsync()
        {
            if (await _aoi.ShowAsync(polygon, zoomToFit: false))
            {
                _previewedJobId = jobId;
            }
        }
    }

    /// <summary>The dock was closed: drop what is drawn on the map; polling carries on.</summary>
    public void Suspend()
    {
        _previewedJobId = null;
        _ = Ui.Guard(_aoi.ClearAsync());
    }

    public void Dispose()
    {
        _poller.Dispose();
        _aoi.Dispose();
        _client?.Dispose();
    }

    // --- list -----------------------------------------------------------------------

    private void LoadExisting()
    {
        var jobs = _store.ListAll();
        foreach (var job in jobs)
        {
            InsertCard(job, prepend: false);
        }
        _poller.Track(jobs.Where(j => ActiveStatuses.Contains(j.Status)).Select(j => j.JobId));
        RefreshEmptyState();
    }

    private void InsertCard(TrackedJob job, bool prepend)
    {
        if (Find(job.JobId) is not null)
        {
            return;
        }
        var card = new JobCardViewModel(job);
        Cards.Insert(prepend ? 0 : Cards.Count, card);
        ApplySearch();
    }

    private JobCardViewModel? Find(string jobId) => Cards.FirstOrDefault(c => c.JobId == jobId);

    private void ApplySearch()
    {
        var query = _searchText.Trim().ToLowerInvariant();
        var shown = 0;
        foreach (var card in Cards)
        {
            var match = query.Length == 0 || card.SearchText.Contains(query, StringComparison.Ordinal);
            card.Shown = match;
            if (match)
            {
                shown++;
            }
        }
        var total = Cards.Count;
        MatchCountText = $"{shown} / {total}";
        MatchCountVisible = query.Length > 0 && total > 0;
        NoMatchVisible = query.Length > 0 && total > 0 && shown == 0;
        Raise(nameof(MatchCountText));
        Raise(nameof(MatchCountVisible));
        Raise(nameof(NoMatchVisible));
    }

    private void RefreshEmptyState() => Raise(nameof(EmptyVisible));

    // --- polling --------------------------------------------------------------------

    private void OnStatusChanged(string jobId, string status)
    {
        _store.UpdateStatus(jobId, status);
        Find(jobId)?.SetStatus(status);
    }

    private void OnFailed(string jobId, string code, string message)
    {
        var errorCode = code.Length > 0 ? code : null;
        var errorMessage = message.Length > 0 ? message : null;
        _store.UpdateStatus(jobId, "failed", errorCode, errorMessage);
        Find(jobId)?.SetStatus("failed", errorCode, errorMessage);
    }

    /// <summary>The poller gave up without a terminal state. The job may still be running, so it reads "unknown" and Refresh re-arms it.</summary>
    private void OnAbandoned(string jobId, AbandonReason reason)
    {
        _store.UpdateStatus(jobId, "unknown");
        Find(jobId)?.SetStatus("unknown");
        if (reason == AbandonReason.Gone)
        {
            Forget(jobId);
            return;
        }
        var shortId = jobId.Length > 8 ? jobId[..8] + "…" : jobId;
        Ui.Notify(reason == AbandonReason.Expired
            ? $"Stopped tracking job {shortId} after 24 hours. Press Refresh to resume, or check the job in the console."
            : $"Could not retrieve the status of job {shortId} after repeated attempts. Press Refresh to retry, or check the job in the console.");
    }

    /// <summary>Drop a job the server does not know about; otherwise the same warning returns on every refresh, forever.</summary>
    private void Forget(string jobId)
    {
        _store.Remove(jobId);
        if (Find(jobId) is { } card)
        {
            Cards.Remove(card);
        }
        if (_previewedJobId == jobId)
        {
            _previewedJobId = null;
            _ = Ui.Guard(_aoi.ClearAsync());
        }
        _forgotten.Add(jobId);
        Ui.Post(FlushForgotten); // several can go at once; one notification for all of them
    }

    private void FlushForgotten()
    {
        var count = _forgotten.Count;
        if (count == 0)
        {
            return;
        }
        _forgotten.Clear();
        ApplySearch();
        RefreshEmptyState();
        Ui.Notify($"Removed {count} job(s) from this list: they are not on your account.");
    }

    // --- refresh --------------------------------------------------------------------

    /// <summary>Pull the latest jobs from the server, merge them in, then re-arm polling for the active ones.</summary>
    private async Task RefreshAsync()
    {
        if (Refreshing)
        {
            return;
        }
        Refreshing = true;
        try
        {
            IReadOnlyList<Job> jobs;
            try
            {
                jobs = await Client().ListJobsAsync();
            }
            catch (Exception e)
            {
                Ui.Notify(Sentences.ForSync(e));
                return;
            }

            int imported = 0, updated = 0, skipped = 0;
            var active = new List<string>();
            // The server lists newest first; oldest→newest with prepend puts fresh imports on top like local submits.
            foreach (var job in jobs.Reverse())
            {
                var analysisType = job.EndpointId ?? "";
                if (!SyncableTypes.Contains(analysisType))
                {
                    skipped++;
                    continue;
                }
                var status = job.Status.WireValue();
                // The list is the only place that echoes the request back, which is how jobs
                // submitted elsewhere get their area and dates.
                var request = JobStore.RequestFromJson(job.RequestParams);
                var source = job.RequestParams is { ValueKind: JsonValueKind.Object } p && p.EnumerateObject().Any() ? "server" : "unavailable";
                if (Find(job.JobId) is { } card)
                {
                    var tracked = _store.UpdateStatus(job.JobId, status, job.ErrorCode, job.ErrorMessage, job.CompletedAt, job.AreaSqkm, job.CreditsUsed);
                    if (tracked is not null)
                    {
                        card.SetStatus(status, job.ErrorCode, job.ErrorMessage);
                        card.ApplyOutcome(tracked);
                        if (_store.SetRequestContext(job.JobId, request, source) is { } resolved)
                        {
                            card.ApplyRequestContext(resolved);
                        }
                        updated++;
                    }
                }
                else
                {
                    var tracked = _store.Add(analysisType, job.JobId, job.CreatedAt, status, request, source);
                    tracked = _store.UpdateStatus(job.JobId, status, job.ErrorCode, job.ErrorMessage, job.CompletedAt, job.AreaSqkm, job.CreditsUsed) ?? tracked;
                    InsertCard(tracked, prepend: true);
                    imported++;
                }
                if (status is "pending" or "processing")
                {
                    active.Add(job.JobId);
                }
            }
            if (active.Count > 0)
            {
                _poller.Track(active);
            }
            RefreshEmptyState();

            var summary = $"Refreshed — {imported} new, {updated} updated.";
            if (skipped > 0)
            {
                summary += $" {skipped} unsupported job(s) skipped.";
            }
            Ui.Notify(summary);
        }
        finally
        {
            Refreshing = false;
        }
    }

    // --- clicks ---------------------------------------------------------------------

    /// <summary>A row is the control: completed shows the result, otherwise the area.</summary>
    private void OnCardClicked(JobCardViewModel card)
    {
        if (card.Status == "completed")
        {
            _ = LoadResultAsync(card);
        }
        else if (card.HasPolygon)
        {
            _ = Ui.Guard(ToggleAoiAsync(card.JobId, card.Job.Polygon!));
        }
        else
        {
            Ui.Notify("This job was submitted with a scene ID — no AOI to preview.");
        }
    }

    private async Task HideAoiAsync()
    {
        _previewedJobId = null;
        await Ui.Guard(_aoi.ClearAsync());
    }

    private async Task ToggleAoiAsync(string jobId, string polygon)
    {
        if (_previewedJobId == jobId)
        {
            await HideAoiAsync();
            return;
        }
        if (!await _aoi.ShowAsync(polygon, zoomToFit: true))
        {
            Ui.Notify("Could not preview the AOI for this job.");
            return;
        }
        _previewedJobId = jobId;
    }

    private async Task LoadResultAsync(JobCardViewModel card)
    {
        var jobId = card.JobId;
        if (!_loading.Add(jobId))
        {
            return; // a fetch for this job is already running
        }
        try
        {
            var job = card.Job;
            var existing = await ResultLayers.FindResultLayerAsync(job.AnalysisType, jobId);
            if (existing is not null)
            {
                await HideAoiAsync();
                await ResultLayers.ZoomToAsync(existing);
                Ui.Notify("This result is already on the map.");
                return;
            }

            card.Loading = true;
            string geojson;
            try
            {
                geojson = await Client().GetJobResultAsync(jobId);
            }
            catch (Exception e)
            {
                Ui.Notify(Sentences.ForResult(e));
                return;
            }

            int count;
            try
            {
                var (slim, _) = ResultStats.StripHeavyProperties(geojson, job.AnalysisType);
                count = ResultStats.CountDetections(slim, job.AnalysisType);
                await ResultLayers.AddResultAsync(slim, job, count);
            }
            catch (Exception e)
            {
                Debug.WriteLine($"SateAIs: could not add the result layer for {jobId}: {e}");
                Ui.Notify("Could not add the result as a layer.");
                return;
            }

            if (!string.IsNullOrEmpty(job.Polygon))
            {
                // The frame is auxiliary; it never fails the result load.
                await Ui.Guard(ResultLayers.AddAoiAsync(job.Polygon, job));
            }
            // The result is what the user is looking at now; the area overlay (this job's or another's) would only sit on top of it.
            await HideAoiAsync();

            _store.SetDetectionCount(jobId, count);
            card.SetDetectionCount(count);
            Ui.Notify(count > 0
                ? $"{JobSummary.FormatDetectionSummary(job.AnalysisType, count)} detected — added to map"
                : "No detections found in this result.");
        }
        finally
        {
            card.Loading = false;
            _loading.Remove(jobId);
        }
    }

    // --- plumbing -------------------------------------------------------------------

    private SateAIsApiClient Client()
    {
        var key = ApiKeys.Resolve() ?? throw new AuthNotConfiguredException();
        if (_client is null || _clientKey != key)
        {
            _client?.Dispose();
            _client = new SateAIsApiClient(key);
            _clientKey = key;
        }
        return _client;
    }
}
