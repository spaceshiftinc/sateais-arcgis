using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace SateAIs.AddIn.Map;

/// <summary>The area of a tracked job, drawn over the map while the user looks at it in the Jobs tab.</summary>
internal sealed class AoiOverlay : IDisposable
{
    private const double ZoomPaddingRatio = 0.1;

    private IDisposable? _band;

    /// <summary>Draw the polygon (WKT in longitude/latitude). False when it cannot be drawn.</summary>
    public Task<bool> ShowAsync(string wkt, bool zoomToFit) => QueuedTask.Run(() =>
    {
        Discard();
        var view = MapView.Active;
        if (view is null || string.IsNullOrWhiteSpace(wkt))
        {
            return false;
        }
        Geometry geometry;
        try
        {
            geometry = MapGeometry.FromWkt(wkt);
        }
        catch (Exception)
        {
            return false;
        }
        if (geometry is null || geometry.IsEmpty)
        {
            return false;
        }
        _band = view.AddOverlay(geometry, Symbol().MakeSymbolReference());
        if (zoomToFit)
        {
            view.ZoomTo(MapGeometry.Padded(geometry, ZoomPaddingRatio), TimeSpan.FromMilliseconds(300));
        }
        return true;
    });

    public Task ClearAsync() => QueuedTask.Run(Discard);

    public void Dispose() => Discard();

    private void Discard()
    {
        _band?.Dispose();
        _band = null;
    }

    private static CIMPolygonSymbol Symbol() => SymbolFactory.Instance.ConstructPolygonSymbol(
        CIMColor.CreateRGBColor(0, 160, 233, 24),
        SimpleFillStyle.Solid,
        SymbolFactory.Instance.ConstructStroke(CIMColor.CreateRGBColor(0, 160, 233, 86), 2, SimpleLineStyle.Dash));
}
