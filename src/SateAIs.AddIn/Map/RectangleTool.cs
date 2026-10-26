using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using SateAIs.Core.Geometry;

namespace SateAIs.AddIn.Map;

/// <summary>Drag a rectangle on the map; the area of interest is its longitude/latitude bounds.</summary>
internal class RectangleTool : MapTool
{
    public const string Id = "SateAIs_RectangleTool";
    public const string ExploreToolId = "esri_mapping_exploreTool";

    private bool _completed;

    public RectangleTool()
    {
        IsSketchTool = true;
        SketchType = SketchGeometryType.Rectangle;
        SketchOutputMode = SketchOutputMode.Map;
    }

    /// <summary>A rectangle was drawn: its WGS84 box and geodesic area in km².</summary>
    public static event Action<BoundingBox, double>? Drawn;

    /// <summary>The tool was left without a rectangle.</summary>
    public static event Action? Cancelled;

    public static Task ActivateAsync() => FrameworkApplication.SetCurrentToolAsync(Id);

    protected override Task OnToolActivateAsync(bool active)
    {
        _completed = false;
        return Task.CompletedTask;
    }

    protected override async Task<bool> OnSketchCompleteAsync(Geometry geometry)
    {
        if (geometry is null || geometry.IsEmpty)
        {
            return false;
        }
        var (box, km2) = await QueuedTask.Run(() =>
            (MapGeometry.ToWgs84Box(geometry), MapGeometry.GeodesicAreaKm2(geometry)));
        if (box.IsDegenerate)
        {
            return false; // a click without a drag
        }
        _completed = true;
        Drawn?.Invoke(box, km2);
        // Not awaited: awaiting a tool switch from inside the sketch callback deadlocks.
        _ = FrameworkApplication.SetCurrentToolAsync(ExploreToolId);
        return true;
    }

    protected override Task OnToolDeactivateAsync(bool hasMapViewChanged)
    {
        if (!_completed)
        {
            Cancelled?.Invoke();
        }
        return Task.CompletedTask;
    }
}
