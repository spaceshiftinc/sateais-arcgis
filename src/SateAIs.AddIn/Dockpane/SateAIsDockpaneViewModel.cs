using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using SateAIs.AddIn.Analysis;
using SateAIs.Core;

namespace SateAIs.AddIn.Dockpane;

internal class SateAIsDockpaneViewModel : DockPane
{
    private const string DockPaneId = "SateAIs_Dockpane";

    public AnalysisViewModel Analysis { get; } = new();

    public string VersionLabel => $"v{ClientVersion.Current}";

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
        base.OnHidden();
    }
}
