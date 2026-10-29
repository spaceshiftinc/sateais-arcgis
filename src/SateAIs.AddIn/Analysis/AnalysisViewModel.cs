using System.Diagnostics;
using System.Windows;
using SateAIs.AddIn.Map;
using SateAIs.AddIn.Settings;
using SateAIs.Core.Analysis;
using SateAIs.Core.Api;
using SateAIs.Core.Credentials;
using SateAIs.Core.Geometry;
using SateAIs.Core.Wording;
using CoreWording = SateAIs.Core.Wording.Wording;

namespace SateAIs.AddIn.Analysis;

/// <summary>
/// The Analysis tab: the three setup steps, the live estimate and the submission. Mirrors the
/// QGIS plugin's panel; every sentence comes from the shared wording.
/// </summary>
internal sealed class AnalysisViewModel : ObservableObject, IDisposable
{
    private enum Step { None, Kind, When, Area }

    private readonly AnalysisForm _form = new();
    private readonly EstimateScheduler _estimate;
    private readonly CoverageOverlay _overlay = new();
    private SateAIsApiClient? _client;
    private string? _clientKey;
    private Step _openStep = Step.Kind;
    private string _shownPolygon = "";
    private string? _analysedWkt;
    private bool _polygonFromMap;
    private bool _hasApiKey;
    private bool _picking;
    private bool _submitting;
    private SubmitHold _submitHold;
    private string _statusText = "";
    private string _statusKind = "hint";
    private string _polygonText = "";

    public AnalysisViewModel()
    {
        _estimate = new EstimateScheduler(FetchEstimateAsync);
        _estimate.Checking += ShowEstimateBusy;
        _estimate.Received += ShowPreview;
        _estimate.Failed += e => ShowEstimateFailed(Sentences.ForEstimate(e), SubmitGate.Blocks(e));

        ChooseTypeCommand = new DelegateCommand(p => ChooseType((string)p!));
        ToggleKindCommand = new DelegateCommand(_ => Toggle(Step.Kind));
        ToggleWhenCommand = new DelegateCommand(_ => Toggle(Step.When), _ => WhenReachable);
        ToggleAreaCommand = new DelegateCommand(_ => ToggleArea());
        PresetCommand = new DelegateCommand(p => ApplyPreset(Convert.ToInt32(p)));
        RedrawCommand = new DelegateCommand(_ => Redraw());
        ClearPolygonCommand = new DelegateCommand(_ => ClearPolygon());
        SubmitCommand = new DelegateCommand(_ => _ = SubmitAsync(), _ => CanSubmit);
        OpenSettingsCommand = new DelegateCommand(_ => OpenSettings());
        CreateAccountCommand = new DelegateCommand(_ =>
            Process.Start(new ProcessStartInfo(ApiKeyViewModel.RegisterUrl) { UseShellExecute = true }));

        RectangleTool.Drawn += OnRectangleDrawn;
        RectangleTool.Cancelled += OnRectangleCancelled;
        RefreshAuthState();
        RefreshSteps();
    }

    /// <summary>A job was submitted: id, type and the request as sent.</summary>
    public event Action<string, string, AnalysisRequest>? JobSubmitted;

    public IReadOnlyList<TypeOption> Types => TypeOption.All;

    // --- welcome / form ---------------------------------------------------------

    public bool HasApiKey
    {
        get => _hasApiKey;
        private set => Set(ref _hasApiKey, value);
    }

    public void RefreshAuthState() => HasApiKey = ApiKeys.Resolve() is not null;

    // --- step ① detection type --------------------------------------------------

    public bool IsKindOpen => _openStep == Step.Kind;

    public bool KindDone => _form.HasType;

    public string KindValue => _form.HasType ? JobSummary.FormatAnalysisLabel(_form.AnalysisType) : "";

    public string KindIcon => _form.AnalysisType ?? "target";

    public string KindIconColor => Types.FirstOrDefault(t => t.Id == _form.AnalysisType)?.Color ?? "#8695A2";

    // --- step ② period / reference date -----------------------------------------

    public bool IsWhenOpen => _openStep == Step.When && _form.HasType;

    public bool WhenReachable => _form.HasType;

    public bool WhenDone => _form.HasType;

    public string WhenLabel => _form.NeedsPeriod || !_form.HasType ? "Period" : "Reference date";

    public string WhenValue => _form.HasType ? _form.WhenSummary() : "";

    public bool NeedsPeriod => _form.NeedsPeriod;

    public DateTime? DateStart
    {
        get => ToDateTime(_form.DateStart);
        set => SetDate(value, d => _form.DateStart = d);
    }

    public DateTime? DateEnd
    {
        get => ToDateTime(_form.DateEnd);
        set => SetDate(value, d => _form.DateEnd = d);
    }

    public DateTime? Date
    {
        get => ToDateTime(_form.Date);
        set => SetDate(value, d => _form.Date = d);
    }

    public bool IsNearest { get => _form.DateDirection is null; set { if (value) SetDateDirection(null); } }

    public bool IsBefore { get => _form.DateDirection == "before"; set { if (value) SetDateDirection("before"); } }

    public bool IsAfter { get => _form.DateDirection == "after"; set { if (value) SetDateDirection("after"); } }

    public bool IsAnyOrbit { get => _form.OrbitDirection is null; set { if (value) SetOrbit(null); } }

    public bool IsAscending { get => _form.OrbitDirection == "ascending"; set { if (value) SetOrbit("ascending"); } }

    public bool IsDescending { get => _form.OrbitDirection == "descending"; set { if (value) SetOrbit("descending"); } }

    // --- step ③ area ------------------------------------------------------------

    public bool IsAreaOpen => _openStep == Step.Area;

    public bool AreaDone => _form.HasPolygon;

    public string AreaLabel => _form.HasPolygon ? "Area drawn" : "Draw an area on the map";

    public string AreaValue => _form.AreaKm2 is { } km2 ? $"{CoreWording.FormatCredits(km2)} km²" : "";

    /// <summary>The WKT field: drawn or pasted.</summary>
    public string PolygonText
    {
        get => _polygonText;
        set
        {
            if (Set(ref _polygonText, value))
            {
                _form.SetPolygonText(value);
                OnInputsChanged();
            }
        }
    }

    // --- status and submit -------------------------------------------------------

    public bool Picking
    {
        get => _picking;
        private set
        {
            if (Set(ref _picking, value))
            {
                RaiseSubmitState();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    /// <summary>"ok" / "error" / "hint".</summary>
    public string StatusKind
    {
        get => _statusKind;
        private set => Set(ref _statusKind, value);
    }

    public bool Submitting
    {
        get => _submitting;
        private set
        {
            if (Set(ref _submitting, value))
            {
                RaiseSubmitState();
            }
        }
    }

    /// <summary>
    /// Only a complete form submits; never while picking, while a submission is in flight,
    /// while the estimate for these inputs is still coming, or after it showed that they cannot run.
    /// </summary>
    public bool CanSubmit => _form.IsComplete && !Picking && !Submitting && _submitHold == SubmitHold.None;

    // --- estimate card ----------------------------------------------------------

    public bool EstimateVisible { get; private set; }

    public bool EstimateBusy { get; private set; }

    public string Note { get; private set; } = "";

    public bool NoteVisible => Note.Length > 0;

    public string NoteKind { get; private set; } = "hint";

    public bool CreditsVisible { get; private set; }

    /// <summary>"up to 1.44 credits" is shown as small words around a large number.</summary>
    public bool CreditsIsUpTo { get; private set; }

    public string CreditsNumber { get; private set; } = "";

    public string CreditsText { get; private set; } = "";

    public bool MeterVisible { get; private set; }

    public int MeterValue { get; private set; }

    public bool MeterPartial { get; private set; }

    public bool LegendVisible { get; private set; }

    public bool LegendAnalysed { get; private set; }

    public bool LegendUncovered { get; private set; }

    // --- balance footer ---------------------------------------------------------

    public bool BalanceVisible { get; private set; }

    public string BalanceText { get; private set; } = "";

    public bool AfterVisible { get; private set; }

    public string AfterText { get; private set; } = "";

    public bool AfterNegative { get; private set; }

    /// <summary>"error" when the run would overdraw the balance, else "hint".</summary>
    public string AfterKind => AfterNegative ? "error" : "hint";

    // --- commands ---------------------------------------------------------------

    public DelegateCommand ChooseTypeCommand { get; }

    public DelegateCommand ToggleKindCommand { get; }

    public DelegateCommand ToggleWhenCommand { get; }

    public DelegateCommand ToggleAreaCommand { get; }

    public DelegateCommand PresetCommand { get; }

    public DelegateCommand RedrawCommand { get; }

    public DelegateCommand ClearPolygonCommand { get; }

    public DelegateCommand SubmitCommand { get; }

    public DelegateCommand OpenSettingsCommand { get; }

    public DelegateCommand CreateAccountCommand { get; }

    public void Dispose()
    {
        RectangleTool.Drawn -= OnRectangleDrawn;
        RectangleTool.Cancelled -= OnRectangleCancelled;
        _estimate.Cancel();
        _overlay.Dispose();
        _client?.Dispose();
    }

    /// <summary>The dock was closed: drop the pending estimate and what is drawn, keep everything else.</summary>
    public void Suspend()
    {
        _estimate.Cancel();
        HideEstimate();
        HideBands();
    }

    /// <summary>The tab went out of sight: its bands come off the map so they never sit over a result.</summary>
    public void HideBands() => _ = Guard(_overlay.ClearAsync());

    /// <summary>The tab is back: draw what the form holds, where the map already is.</summary>
    public void ShowBands()
    {
        var polygon = _form.BuildRequest()?.Polygon ?? "";
        _shownPolygon = polygon;
        _ = Guard(_overlay.ShowRequestedAsync(polygon, recenter: false));
        if (!string.IsNullOrEmpty(_analysedWkt))
        {
            _ = Guard(_overlay.ShowAnalysedAsync(_analysedWkt));
        }
    }

    // --- actions ----------------------------------------------------------------

    private void ChooseType(string analysisType)
    {
        _form.SetAnalysisType(analysisType);
        foreach (var option in Types)
        {
            option.IsChosen = option.Id == analysisType;
        }
        _openStep = Step.When; // on to the next step; no need to fold this one by hand
        RefreshSteps();
        OnInputsChanged();
    }

    private void Toggle(Step step)
    {
        _openStep = _openStep == step ? Step.None : step;
        RefreshSteps();
    }

    private void ToggleArea()
    {
        // Before an area exists the click means "draw"; afterwards it opens the field.
        _openStep = Step.Area;
        RefreshSteps();
        if (!_form.HasPolygon)
        {
            StartPicking();
        }
    }

    private void Redraw()
    {
        _openStep = Step.Area;
        RefreshSteps();
        StartPicking();
    }

    private void ClearPolygon()
    {
        _form.ClearPolygon();
        _polygonText = "";
        Raise(nameof(PolygonText));
        _openStep = Step.Area;
        RefreshSteps();
        OnInputsChanged();
    }

    private void ApplyPreset(int months)
    {
        _form.ApplyPreset(months);
        Raise(nameof(DateStart));
        Raise(nameof(DateEnd));
        OnDatesEdited();
    }

    private void SetDate(DateTime? value, Action<DateOnly> assign)
    {
        if (value is null)
        {
            return;
        }
        assign(DateOnly.FromDateTime(value.Value));
        OnDatesEdited();
    }

    private void SetDateDirection(string? value)
    {
        _form.DateDirection = value;
        Raise(nameof(IsNearest));
        Raise(nameof(IsBefore));
        Raise(nameof(IsAfter));
        OnDatesEdited();
    }

    private void SetOrbit(string? value)
    {
        _form.OrbitDirection = value;
        Raise(nameof(IsAnyOrbit));
        Raise(nameof(IsAscending));
        Raise(nameof(IsDescending));
        OnDatesEdited();
    }

    private void OnDatesEdited()
    {
        RefreshSteps();
        OnInputsChanged();
    }

    private void StartPicking()
    {
        Picking = true;
        SetStatus("Drag a rectangle on the map to set the polygon.", "ok");
        _ = Guard(RectangleTool.ActivateAsync());
    }

    private void OnRectangleDrawn(BoundingBox box, double km2) => OnUiThread(() =>
    {
        _polygonFromMap = true;
        try
        {
            _form.SetPolygonFromMap(box.ToWkt(), km2);
            _polygonText = _form.Polygon;
            Raise(nameof(PolygonText));
            _openStep = Step.None;
            RefreshSteps();
            OnInputsChanged();
        }
        finally
        {
            _polygonFromMap = false;
        }
        EndPicking();
    });

    private void OnRectangleCancelled() => OnUiThread(EndPicking);

    private void EndPicking()
    {
        if (!Picking)
        {
            return;
        }
        Picking = false;
        ClearStatus();
    }

    /// <summary>Inputs changed: drop the stale estimate, redraw the requested area, schedule a fresh estimate.</summary>
    private void OnInputsChanged()
    {
        RaiseSubmitState();
        _estimate.Cancel();
        HideEstimate();
        var polygon = _form.BuildRequest()?.Polygon ?? "";
        // A typed area is usually far from the current view; a drawn one is already in sight.
        var recenter = polygon.Length > 0 && polygon != _shownPolygon && !_polygonFromMap;
        _shownPolygon = polygon;
        _ = Guard(_overlay.ShowRequestedAsync(polygon, recenter));
        if (_form.IsComplete)
        {
            SetSubmitHold(SubmitHold.Estimating);
            _estimate.Schedule();
        }
    }

    private async Task<Preview> FetchEstimateAsync(CancellationToken ct)
    {
        var request = _form.BuildRequest() ?? throw new InvalidAnalysisRequestException("incomplete");
        return await Client().PreviewAsync(request, ct);
    }

    private async Task SubmitAsync()
    {
        if (Submitting)
        {
            return;
        }
        var request = _form.BuildRequest();
        if (request is null)
        {
            SetStatus(Sentences.InvalidInput, "error");
            return;
        }
        var analysisType = request.AnalysisType;
        Submitting = true;
        SetStatus("Submitting…", "hint");
        try
        {
            var job = await Client().SubmitAsync(request);
            SetStatus("Submitted.", "ok");
            Notify("Job submitted. It is now tracked in the Jobs tab.");
            _form.Clear();
            _polygonText = "";
            Raise(nameof(PolygonText));
            Raise(nameof(Date));
            Raise(nameof(DateStart));
            Raise(nameof(DateEnd));
            SetDateDirection(null);
            SetOrbit(null);
            _openStep = Step.Area;
            RefreshSteps();
            OnInputsChanged();
            JobSubmitted?.Invoke(job.JobId, analysisType, request);
        }
        catch (Exception e)
        {
            SetStatus(Sentences.ForSubmit(e), "error");
        }
        finally
        {
            Submitting = false;
        }
    }

    private void OpenSettings()
    {
        var saved = ApiKeyWindow.Open(Application.Current.MainWindow);
        RefreshAuthState();
        if (saved)
        {
            Notify("Credentials saved.");
            _client?.Dispose();
            _client = null;
        }
    }

    // --- estimate card rendering --------------------------------------------------

    private void ShowEstimateBusy()
    {
        SetSubmitHold(SubmitHold.Estimating);
        EstimateVisible = true;
        EstimateBusy = true;
        Note = "";
        CreditsVisible = MeterVisible = LegendVisible = false;
        RaiseEstimate();
    }

    private void ShowEstimateFailed(string reason, bool blocksSubmit)
    {
        SetSubmitHold(blocksSubmit ? SubmitHold.Rejected : SubmitHold.None);
        EstimateVisible = true;
        EstimateBusy = false;
        Note = reason.Length > 0 ? reason : CoreWording.EstimateFailed;
        NoteKind = "error";
        CreditsVisible = MeterVisible = LegendVisible = false;
        RaiseEstimate();
    }

    private void HideEstimate()
    {
        SetSubmitHold(SubmitHold.None);
        _analysedWkt = null;
        EstimateVisible = EstimateBusy = false;
        Note = "";
        CreditsVisible = MeterVisible = LegendVisible = false;
        BalanceVisible = AfterVisible = false;
        RaiseEstimate();
        RaiseBalance();
    }

    private void ShowPreview(Preview preview)
    {
        SetSubmitHold(SubmitGate.Blocks(preview) ? SubmitHold.Rejected : SubmitHold.None);
        EstimateVisible = true;
        EstimateBusy = false;

        if (CoreWording.ScenesUnavailable(preview.Warnings))
        {
            var serverSays = CoreWording.WarningMessages(preview.Warnings);
            var headline = serverSays.Count > 0 ? serverSays[0] : "No scenes are available.";
            Note = $"{headline} {CoreWording.ScenesUnavailableHint}";
            NoteKind = "error";
            CreditsVisible = MeterVisible = false;
            LegendVisible = true;
            LegendAnalysed = LegendUncovered = false;
            RaiseEstimate();
        }
        else
        {
            var credits = preview.Credits;
            var ratio = preview.Coverage?.Ratio;
            var parts = new List<string>();
            var warn = false;
            if (preview.Coverage is null)
            {
                parts.Add(CoreWording.CoverageUnchecked);
                warn = true;
            }
            else
            {
                var sentence = CoreWording.CoverageLabel(ratio);
                if (sentence.Length > 0)
                {
                    parts.Add(sentence);
                }
                warn = CoreWording.CoverageIsPartial(ratio);
            }
            var balance = CoreWording.BalanceNote(credits?.Sufficient, credits?.Balance);
            if (balance.Length > 0)
            {
                parts.Add(balance);
                warn = true;
            }
            parts.AddRange(CoreWording.WarningMessages(preview.Warnings));

            Note = string.Join(" · ", parts);
            NoteKind = warn ? "error" : "hint";

            var text = CoreWording.CreditsLabel(credits?.Estimated);
            CreditsIsUpTo = text.StartsWith("up to ", StringComparison.Ordinal);
            CreditsNumber = CreditsIsUpTo ? text["up to ".Length..^" credits".Length] : "";
            CreditsText = text;
            CreditsVisible = true;

            MeterVisible = ratio is >= 0 and <= 1;
            MeterValue = MeterVisible ? (int)(ratio!.Value * 100) : 0;
            MeterPartial = MeterVisible && ratio < 1;

            // The blue outline is only drawn when the server returned the analysed polygon,
            // and the grey one only when the gap is big enough to draw.
            var analysedDrawn = !string.IsNullOrEmpty(preview.Coverage?.Polygon);
            LegendVisible = true;
            LegendAnalysed = analysedDrawn;
            LegendUncovered = analysedDrawn && ratio is { } r && r < 1 - CoverageOverlay.NegligibleGapRatio;
            RaiseEstimate();
        }

        ShowBalance(preview.Credits?.Balance, preview.Credits?.Estimated);
        _analysedWkt = preview.Coverage?.Polygon;
        _ = Guard(_overlay.ShowAnalysedAsync(_analysedWkt));
    }

    private void ShowBalance(double? balance, double? estimated)
    {
        if (balance is null || !double.IsFinite(balance.Value))
        {
            BalanceVisible = AfterVisible = false;
            RaiseBalance();
            return;
        }
        BalanceVisible = true;
        BalanceText = CoreWording.FormatCredits(balance.Value);
        if (CoreWording.CreditsUnknown(estimated))
        {
            AfterVisible = false;
        }
        else
        {
            var remaining = balance.Value - estimated!.Value;
            AfterVisible = true;
            AfterText = CoreWording.FormatCredits(remaining);
            AfterNegative = remaining < 0;
        }
        RaiseBalance();
    }

    // --- plumbing ---------------------------------------------------------------

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

    private void RefreshSteps()
    {
        foreach (var name in new[]
                 {
                     nameof(IsKindOpen), nameof(KindDone), nameof(KindValue), nameof(KindIcon), nameof(KindIconColor),
                     nameof(IsWhenOpen), nameof(WhenReachable), nameof(WhenDone), nameof(WhenLabel), nameof(WhenValue), nameof(NeedsPeriod),
                     nameof(IsAreaOpen), nameof(AreaDone), nameof(AreaLabel), nameof(AreaValue),
                 })
        {
            Raise(name);
        }
        ToggleWhenCommand.RaiseCanExecuteChanged();
    }

    private void SetSubmitHold(SubmitHold value)
    {
        if (_submitHold != value)
        {
            _submitHold = value;
            RaiseSubmitState();
        }
    }

    private void RaiseSubmitState()
    {
        Raise(nameof(CanSubmit));
        SubmitCommand.RaiseCanExecuteChanged();
    }

    private void RaiseEstimate()
    {
        foreach (var name in new[]
                 {
                     nameof(EstimateVisible), nameof(EstimateBusy), nameof(Note), nameof(NoteVisible), nameof(NoteKind),
                     nameof(CreditsVisible), nameof(CreditsIsUpTo), nameof(CreditsNumber), nameof(CreditsText),
                     nameof(MeterVisible), nameof(MeterValue), nameof(MeterPartial),
                     nameof(LegendVisible), nameof(LegendAnalysed), nameof(LegendUncovered),
                 })
        {
            Raise(name);
        }
    }

    private void RaiseBalance()
    {
        foreach (var name in new[] { nameof(BalanceVisible), nameof(BalanceText), nameof(AfterVisible), nameof(AfterText), nameof(AfterNegative), nameof(AfterKind) })
        {
            Raise(name);
        }
    }

    private void SetStatus(string text, string kind)
    {
        StatusText = text;
        StatusKind = kind;
    }

    private void ClearStatus() => SetStatus("", "hint");

    private static DateTime? ToDateTime(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static void Notify(string message) => Ui.Notify(message);

    private static void OnUiThread(Action action) => Ui.OnUiThread(action);

    private static Task Guard(Task task) => Ui.Guard(task);

    /// <summary>Why the estimate holds Submit, if it does.</summary>
    private enum SubmitHold
    {
        None,

        /// <summary>The estimate for the current inputs is scheduled or running.</summary>
        Estimating,

        /// <summary>The estimate showed that these inputs cannot run.</summary>
        Rejected,
    }
}
