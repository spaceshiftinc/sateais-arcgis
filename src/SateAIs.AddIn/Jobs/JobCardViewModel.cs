using SateAIs.AddIn.Analysis;
using SateAIs.Core.Api;
using SateAIs.Core.Jobs;
using SateAIs.Core.Wording;
using CoreWording = SateAIs.Core.Wording.Wording;

namespace SateAIs.AddIn.Jobs;

/// <summary>A caption and its value on a job row, e.g. "Area" / "196 km²".</summary>
internal sealed record MetaField(string Label, string Value);

/// <summary>One row of the Jobs tab. Holds its own copy of the record and is told about changes.</summary>
internal sealed class JobCardViewModel : ObservableObject
{
    /// <summary>How long a fresh completion glows.</summary>
    public static readonly TimeSpan Pulse = TimeSpan.FromMilliseconds(650);

    private readonly TrackedJob _job;
    private bool _loading;
    private bool _pulsing;
    private bool _shown = true;

    public JobCardViewModel(TrackedJob job)
    {
        _job = job;
        SearchText = JobSummary.BuildSearchText(job);
    }

    public TrackedJob Job => _job;

    public string JobId => _job.JobId;

    public string Status => _job.Status;

    public bool HasPolygon => !string.IsNullOrEmpty(_job.Polygon);

    /// <summary>Lower-cased haystack for the search box, rebuilt only when the request context changes.</summary>
    public string SearchText { get; private set; }

    // --- first line: what, how many ------------------------------------------------

    public string TypeLabel => JobSummary.FormatAnalysisLabel(_job.AnalysisType);

    public string TypeIcon => AnalysisTypes.IsKnown(_job.AnalysisType) ? _job.AnalysisType : "target";

    public string TypeColor => TypeOption.All.FirstOrDefault(t => t.Id == _job.AnalysisType)?.Color ?? "#8695A2";

    public bool FindVisible => _job.Status == "completed" && _job.DetectionCount is not null;

    /// <summary>The spark marks a find; zero does not get one.</summary>
    public bool FindSparkVisible => FindVisible && _job.DetectionCount > 0;

    public string FindText => _job.DetectionCount is { } count ? JobSummary.FormatDetectionCount(count) : "";

    public string FindTooltip => _job.DetectionCount is { } count ? JobSummary.FormatDetectionOutcome(_job.AnalysisType, count) : "";

    /// <summary>"ok" for a find, "hint" for none: nothing found is a result too, at a different weight.</summary>
    public string FindKind => _job.DetectionCount > 0 ? "ok" : "hint";

    // --- second line: when, what state ----------------------------------------------

    public string SubmittedText => JobSummary.FormatSubmittedShort(_job.SubmittedAt);

    public string StatusLabel => _job.Status.ToUpperInvariant();

    /// <summary>"ok" / "error" / "muted", for the pill colours.</summary>
    public string StatusKind => _job.Status switch
    {
        "completed" => "ok",
        "failed" => "error",
        _ => "muted",
    };

    // --- detail lines ---------------------------------------------------------------

    public string RequestText => JobSummary.BuildRequestSummary(_job);

    public bool RequestVisible => RequestText.Length > 0;

    /// <summary>Captioned figures. Spaces inside a value are non-breaking so "1.96 credits" never splits.</summary>
    public IReadOnlyList<MetaField> MetaFields =>
        CoreWording.JobMetaFields("", _job.AreaSqkm, _job.CreditsUsed, CoreWording.TookLabel(_job.SubmittedAt, _job.CompletedAt))
            .Select(f => new MetaField(f.Label, f.Value.Replace(' ', ' ')))
            .ToList();

    public bool MetaVisible => MetaFields.Count > 0;

    public string ErrorText =>
        _job.Status == "failed" && (!string.IsNullOrEmpty(_job.ErrorCode) || !string.IsNullOrEmpty(_job.ErrorMessage))
            ? CoreWording.FailureLabel(_job.ErrorCode, _job.ErrorMessage)
            : "";

    public bool ErrorVisible => ErrorText.Length > 0;

    public string Tooltip => JobSummary.BuildRequestTooltip(_job);

    // --- transient state ------------------------------------------------------------

    /// <summary>A result fetch is in flight for this job.</summary>
    public bool Loading
    {
        get => _loading;
        set => Set(ref _loading, value);
    }

    /// <summary>Briefly true after the job turns completed, so the moment is noticed.</summary>
    public bool Pulsing
    {
        get => _pulsing;
        private set => Set(ref _pulsing, value);
    }

    /// <summary>False when the search box filters this row out.</summary>
    public bool Shown
    {
        get => _shown;
        set => Set(ref _shown, value);
    }

    // --- updates --------------------------------------------------------------------

    public void SetStatus(string status, string? errorCode = null, string? errorMessage = null)
    {
        var prior = _job.Status;
        _job.Status = status;
        _job.ErrorCode = errorCode;
        _job.ErrorMessage = errorMessage;
        if (status != "completed")
        {
            Loading = false; // e.g. a retried job went back to processing mid-fetch
        }
        RaiseAll();
        if (status == "completed" && prior != "completed")
        {
            _ = PulseAsync();
        }
    }

    /// <summary>Adopt the completion figures a refresh brought back.</summary>
    public void ApplyOutcome(TrackedJob job)
    {
        _job.CompletedAt = job.CompletedAt;
        _job.AreaSqkm = job.AreaSqkm;
        _job.CreditsUsed = job.CreditsUsed;
        RaiseAll();
    }

    /// <summary>Adopt request details resolved after the row was built.</summary>
    public void ApplyRequestContext(TrackedJob job)
    {
        _job.SceneId = job.SceneId;
        _job.Date = job.Date;
        _job.DateStart = job.DateStart;
        _job.DateEnd = job.DateEnd;
        _job.RequestSource = job.RequestSource;
        _job.Polygon = job.Polygon;
        SearchText = JobSummary.BuildSearchText(_job);
        RaiseAll();
    }

    public void SetDetectionCount(int count)
    {
        _job.DetectionCount = count;
        RaiseAll();
    }

    private async Task PulseAsync()
    {
        Pulsing = true;
        await Task.Delay(Pulse);
        Pulsing = false;
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
                 {
                     nameof(Status), nameof(HasPolygon), nameof(SearchText),
                     nameof(TypeLabel), nameof(TypeIcon), nameof(TypeColor),
                     nameof(FindVisible), nameof(FindSparkVisible), nameof(FindText), nameof(FindTooltip), nameof(FindKind),
                     nameof(SubmittedText), nameof(StatusLabel), nameof(StatusKind),
                     nameof(RequestText), nameof(RequestVisible), nameof(MetaFields), nameof(MetaVisible),
                     nameof(ErrorText), nameof(ErrorVisible), nameof(Tooltip),
                 })
        {
            Raise(name);
        }
    }
}
