using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;

namespace SateAIs.AddIn;

internal class SateAIsModule : Module
{
    private static SateAIsModule? _this;

    public static SateAIsModule Current =>
        _this ??= (SateAIsModule)FrameworkApplication.FindModule("SateAIs_Module");
}

internal class ShowDockpaneButton : Button
{
    protected override void OnClick() => Dockpane.SateAIsDockpaneViewModel.Show();
}

/// <summary>The key can be set or changed from the ribbon at any time, not only from the welcome page.</summary>
internal class OpenSettingsButton : Button
{
    protected override void OnClick() => Dockpane.SateAIsDockpaneViewModel.OpenSettings();
}
