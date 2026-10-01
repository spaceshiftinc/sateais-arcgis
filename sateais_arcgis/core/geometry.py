"""Convert an ArcGIS extent into the WKT the SateAIs API expects.

The API takes the area of interest as an EPSG:4326 WKT polygon, so this module
is the boundary between arcpy and the API.

The area is always a rectangle: the tool's parameter is a `GPExtent`. SAR scenes
are rectangular and analysis runs on rectangles, so accepting arbitrary shapes
and quietly squaring them off would be misleading.

The WKT is assembled here rather than taken from `arcpy.Geometry.WKT`, because
arcpy returns `MULTIPOLYGON` (and `MULTIPOLYGON Z` for geometry drawn on a map),
neither of which the API accepts.
"""

from __future__ import annotations

from dataclasses import dataclass

import arcpy

#: About 0.1 m, which is far finer than the imagery resolution.
COORD_DECIMALS = 6


def wgs84() -> arcpy.SpatialReference:
    """EPSG:4326, created fresh each time.

    Do not cache this at module level: geoprocessing runs on its own thread, and
    a spatial reference built at import time fails there.
    """
    return arcpy.SpatialReference(4326)


class GeometryError(Exception):
    """The area of interest could not be turned into something the API accepts."""


@dataclass(frozen=True)
class PreparedPolygon:
    """An area of interest ready to send."""

    wkt: str
    area_sqkm: float
    west: float
    south: float
    east: float
    north: float

    def describe_bounds(self, decimals: int = 5) -> str:
        return (
            f"W {self.west:.{decimals}f}  S {self.south:.{decimals}f}  "
            f"E {self.east:.{decimals}f}  N {self.north:.{decimals}f}  (WGS 84)"
        )


def geodesic_area_sqkm(geometry: arcpy.Geometry) -> float:
    """Geodesic area in km².

    Not the planar `.area`: the API measures and bills on the geodesic area, and
    a mismatch here means the request passes locally and is rejected remotely.
    """
    return float(geometry.getArea("GEODESIC", "SQUAREKILOMETERS"))


def extent_polygon(extent: arcpy.Extent, sr: arcpy.SpatialReference) -> arcpy.Polygon:
    """Build a polygon from the extent's corners with an explicit spatial reference.

    `extent.polygon` does not always carry one, which breaks projection.
    """
    corners = arcpy.Array(
        [
            arcpy.Point(extent.XMin, extent.YMin),
            arcpy.Point(extent.XMax, extent.YMin),
            arcpy.Point(extent.XMax, extent.YMax),
            arcpy.Point(extent.XMin, extent.YMax),
            arcpy.Point(extent.XMin, extent.YMin),
        ]
    )
    return arcpy.Polygon(corners, sr)


def bbox_wkt(extent: arcpy.Extent, decimals: int = COORD_DECIMALS) -> str:
    """`POLYGON ((...))` for an extent, counter-clockwise and closed."""

    def fmt(value: float) -> str:
        return f"{value:.{decimals}f}".rstrip("0").rstrip(".")

    x0, y0 = fmt(extent.XMin), fmt(extent.YMin)
    x1, y1 = fmt(extent.XMax), fmt(extent.YMax)
    ring = f"{x0} {y0}, {x1} {y0}, {x1} {y1}, {x0} {y1}, {x0} {y0}"
    return f"POLYGON (({ring}))"


def extent_to_wgs84(extent: arcpy.Extent) -> arcpy.Extent:
    """Reproject an extent to EPSG:4326.

    A projected rectangle is not a rectangle in degrees, so this takes the extent
    of the reprojected shape. That can only grow the area, never shrink it, so
    nothing the user asked for is dropped.
    """
    sr = extent.spatialReference
    if sr is None or sr.factoryCode == 0:
        raise GeometryError(
            "The area of interest has no coordinate system. "
            "Set the map's coordinate system and try again."
        )
    if sr.factoryCode == 4326:
        return extent
    try:
        return extent_polygon(extent, sr).projectAs(wgs84()).extent
    except RuntimeError as exc:
        raise GeometryError(
            "Could not convert the area of interest to longitude/latitude "
            f"from {sr.name!r}. Use a projection that can be transformed to "
            f"WGS 1984, or enter the extent in degrees. ({exc})"
        ) from exc


def prepare_extent(extent: arcpy.Extent) -> PreparedPolygon:
    """Turn a `GPExtent` value into WKT the API accepts."""
    if extent is None:
        raise GeometryError("No area of interest was given.")

    wgs = extent_to_wgs84(extent)

    if wgs.XMin == wgs.XMax or wgs.YMin == wgs.YMax:
        raise GeometryError(
            "The area of interest has no width or height. Draw a rectangle on the map."
        )
    if not (wgs.XMin >= -180 and wgs.XMax <= 180 and wgs.YMin >= -90 and wgs.YMax <= 90):
        raise GeometryError("The area of interest falls outside valid longitude/latitude bounds.")

    return PreparedPolygon(
        wkt=bbox_wkt(wgs),
        area_sqkm=geodesic_area_sqkm(extent_polygon(wgs, wgs84())),
        west=wgs.XMin,
        south=wgs.YMin,
        east=wgs.XMax,
        north=wgs.YMax,
    )
