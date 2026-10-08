using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using SateAIs.Core;

namespace SateAIs.AddIn.Dockpane;

internal class SateAIsDockpaneViewModel : DockPane
{
    private const string DockPaneId = "SateAIs_Dockpane";

    public string VersionLabel => $"v{ClientVersion.Current}";

    internal static void Show() => FrameworkApplication.DockPaneManager.Find(DockPaneId)?.Activate();
}
