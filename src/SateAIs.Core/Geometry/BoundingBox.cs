using SateAIs.Core.Wording;

namespace SateAIs.Core.Geometry;

/// <summary>
/// The area of interest, always a rectangle in EPSG:4326. The WKT is assembled by hand so it
/// is a plain <c>POLYGON</c> without Z values, which is the only form the API accepts.
/// </summary>
public sealed record BoundingBox(double West, double South, double East, double North)
{
    /// <summary>About 0.1 m, far finer than the imagery.</summary>
    public const int CoordinateDecimals = 6;

    public bool IsDegenerate => West == East || South == North;

    public bool IsWithinWorld => West >= -180 && East <= 180 && South >= -90 && North <= 90;

    /// <summary><c>POLYGON ((x0 y0, x1 y0, x1 y1, x0 y1, x0 y0))</c>, counter-clockwise and closed.</summary>
    public string ToWkt(int decimals = CoordinateDecimals)
    {
        var x0 = Coordinate(West, decimals);
        var y0 = Coordinate(South, decimals);
        var x1 = Coordinate(East, decimals);
        var y1 = Coordinate(North, decimals);
        return $"POLYGON (({x0} {y0}, {x1} {y0}, {x1} {y1}, {x0} {y1}, {x0} {y0}))";
    }

    public string DescribeBounds(int decimals = 5) =>
        $"W {PyFormat.Fixed(West, decimals)}  S {PyFormat.Fixed(South, decimals)}  "
        + $"E {PyFormat.Fixed(East, decimals)}  N {PyFormat.Fixed(North, decimals)}  (WGS 84)";

    private static string Coordinate(double value, int decimals)
    {
        var text = PyFormat.Fixed(value, decimals);
        return text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;
    }
}
