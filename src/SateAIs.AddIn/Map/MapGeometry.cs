using ArcGIS.Core.Geometry;
using SateAIs.Core.Geometry;

namespace SateAIs.AddIn.Map;

/// <summary>Between the map's geometry and the EPSG:4326 rectangles the API works with. Run on the MCT.</summary>
internal static class MapGeometry
{
    /// <summary>The bounding box, in longitude/latitude, of a geometry in any coordinate system.</summary>
    public static BoundingBox ToWgs84Box(Geometry geometry)
    {
        var projected = geometry.SpatialReference is { Wkid: 4326 }
            ? geometry
            : GeometryEngine.Instance.Project(geometry, SpatialReferences.WGS84);
        var extent = projected.Extent;
        return new BoundingBox(extent.XMin, extent.YMin, extent.XMax, extent.YMax);
    }

    public static double GeodesicAreaKm2(Geometry geometry) =>
        GeometryEngine.Instance.GeodesicArea(geometry, AreaUnit.SquareKilometers);

    public static Geometry FromWkt(string wkt) =>
        GeometryEngine.Instance.ImportFromWKT(WktImportFlags.WktImportDefaults, wkt, SpatialReferences.WGS84);

    public static Polygon FromBox(BoundingBox box) =>
        PolygonBuilderEx.CreatePolygon(
            EnvelopeBuilderEx.CreateEnvelope(box.West, box.South, box.East, box.North, SpatialReferences.WGS84));

    /// <summary>The geometry's extent grown by a share of its size, for a comfortable zoom.</summary>
    public static Envelope Padded(Geometry geometry, double ratio)
    {
        var extent = geometry.Extent;
        var dx = (extent.Width > 0 ? extent.Width : 1) * ratio;
        var dy = (extent.Height > 0 ? extent.Height : 1) * ratio;
        return EnvelopeBuilderEx.CreateEnvelope(
            extent.XMin - dx, extent.YMin - dy, extent.XMax + dx, extent.YMax + dy, extent.SpatialReference);
    }
}
