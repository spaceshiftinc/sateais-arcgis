using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using SateAIs.AddIn.Analysis;
using SateAIs.AddIn.Jobs;
using SateAIs.Core;
using SateAIs.Core.Api;

namespace SateAIs.AddIn.Dockpane;

internal class SateAIsDockpaneViewModel : DockPane
{
    private const string DockPaneId = "SateAIs_Dockpane";
    private const int AnalysisTab = 0;
    private const int JobsTab = 1;

    private int _selectedTab = AnalysisTab;

    public SateAIsDockpaneViewModel()
    {
        Analysis.JobSubmitted += OnJobSubmitted;
        Jobs.StartAnalysisRequested += () => SelectedTab = AnalysisTab;
    }

    public AnalysisViewModel Analysis { get; } = new();

    public JobsViewModel Jobs { get; } = new();

    public string VersionLabel => $"v{ClientVersion.Current}";

    public int SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    internal static void Show() => FrameworkApplication.DockPaneManager.Find(DockPaneId)?.Activate();

    protected override void OnShow(bool isVisible)
    {
        if (isVisible)
        {
            Analysis.RefreshAuthState();
        }
        base.OnShow(isVisible);
    }

    /// <summary>Closing the pane only hides it; polling and submissions in flight carry on.</summary>
    protected override void OnHidden()
    {
        Analysis.Suspend();
        Jobs.Suspend();
        base.OnHidden();
    }

    /// <summary>The area on the map passes from the form to the job, and the user follows it to the Jobs tab.</summary>
    private void OnJobSubmitted(string jobId, string analysisType, AnalysisRequest request)
    {
        Jobs.Add(jobId, analysisType, request);
        Jobs.ShowSubmittedArea(jobId, request.Polygon);
        SelectedTab = JobsTab;
    }
}
