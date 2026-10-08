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
