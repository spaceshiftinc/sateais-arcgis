using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace SateAIs.AddIn.Map;

/// <summary>
/// Requested and analysed area drawn together on the map, so the gap between them is visible
/// before any credits are spent. Colours and widths match the QGIS plugin's bands.
/// </summary>
internal sealed class CoverageOverlay : IDisposable
{
    /// <summary>Below this share of the requested area the shortfall is numerical noise.</summary>
    public const double NegligibleGapRatio = 0.005;

    private const double ZoomPaddingRatio = 0.1;

    private IDisposable? _requested;
    private IDisposable? _covered;
    private IDisposable? _uncovered;
    private string _requestedWkt = "";

    /// <summary>Draw the requested area (dashed grey outline). Empty WKT clears everything.</summary>
    public Task ShowRequestedAsync(string wkt, bool recenter) => QueuedTask.Run(() =>
    {
        DisposeAll();
        _requestedWkt = wkt;
        var view = MapView.Active;
        if (view is null || string.IsNullOrWhiteSpace(wkt))
        {
            return;
        }
        Geometry requested;
        try
        {
            requested = MapGeometry.FromWkt(wkt);
        }
        catch (Exception)
        {
            return; // not drawable; the estimate will say so
        }
        _requested = view.AddOverlay(requested, RequestedSymbol().MakeSymbolReference());
        if (recenter)
        {
            view.ZoomTo(MapGeometry.Padded(requested, ZoomPaddingRatio), TimeSpan.FromMilliseconds(300));
        }
    });

    /// <summary>Draw the analysed area (blue) and what is left uncovered (grey). Null clears both.</summary>
    public Task ShowAnalysedAsync(string? analysedWkt) => QueuedTask.Run(() =>
    {
        _covered?.Dispose();
        _uncovered?.Dispose();
        _covered = _uncovered = null;
        var view = MapView.Active;
        if (view is null || string.IsNullOrWhiteSpace(analysedWkt) || string.IsNullOrWhiteSpace(_requestedWkt))
        {
            return;
        }
        Geometry requested;
        Geometry analysed;
        try
        {
            requested = MapGeometry.FromWkt(_requestedWkt);
            analysed = MapGeometry.FromWkt(analysedWkt);
        }
        catch (Exception)
        {
            return;
        }
        _covered = view.AddOverlay(analysed, CoveredSymbol().MakeSymbolReference());

        Geometry gap;
        try
        {
            gap = GeometryEngine.Instance.Difference(requested, analysed);
        }
        catch (Exception)
        {
            return; // an odd shape; better no grey than a wrong one
        }
        if (gap is null || gap.IsEmpty)
        {
            return;
        }
        var requestedArea = MapGeometry.GeodesicAreaKm2(requested);
        if (requestedArea > 0 && MapGeometry.GeodesicAreaKm2(gap) / requestedArea < NegligibleGapRatio)
        {
            return;
        }
        _uncovered = view.AddOverlay(gap, UncoveredSymbol().MakeSymbolReference());
    });

    public Task ClearAsync() => QueuedTask.Run(() =>
    {
        DisposeAll();
        _requestedWkt = "";
    });

    public void Dispose() => DisposeAll();

    private void DisposeAll()
    {
        _requested?.Dispose();
        _covered?.Dispose();
        _uncovered?.Dispose();
        _requested = _covered = _uncovered = null;
    }

    private static CIMPolygonSymbol RequestedSymbol() => SymbolFactory.Instance.ConstructPolygonSymbol(
        CIMColor.CreateRGBColor(143, 160, 173, 0),
        SimpleFillStyle.Null,
        SymbolFactory.Instance.ConstructStroke(CIMColor.CreateRGBColor(143, 160, 173, 90), 2, SimpleLineStyle.Dash));

    private static CIMPolygonSymbol CoveredSymbol() => SymbolFactory.Instance.ConstructPolygonSymbol(
        CIMColor.CreateRGBColor(0, 159, 232, 18),
        SimpleFillStyle.Solid,
        SymbolFactory.Instance.ConstructStroke(CIMColor.CreateRGBColor(0, 159, 232, 86), 2, SimpleLineStyle.Solid));

    private static CIMPolygonSymbol UncoveredSymbol() => SymbolFactory.Instance.ConstructPolygonSymbol(
        CIMColor.CreateRGBColor(124, 138, 148, 19),
        SimpleFillStyle.Solid,
        SymbolFactory.Instance.ConstructStroke(CIMColor.CreateRGBColor(124, 138, 148, 67), 1, SimpleLineStyle.Solid));
}
