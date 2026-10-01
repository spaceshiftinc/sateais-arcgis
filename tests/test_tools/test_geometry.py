"""The pure parts of core.geometry, with the arcpy stand-in from conftest."""

from __future__ import annotations

import arcpy
import pytest

from sateais_arcgis.core import geometry


class TestBboxWkt:
    def test_closed_ring_from_the_corners(self):
        extent = arcpy.Extent(139.5, 35.2, 139.9, 35.6)
        assert geometry.bbox_wkt(extent) == (
            "POLYGON ((139.5 35.2, 139.9 35.2, 139.9 35.6, 139.5 35.6, 139.5 35.2))"
        )

    def test_plain_polygon_not_multipolygon(self):
        # The API accepts POLYGON only; arcpy's own WKT would say MULTIPOLYGON.
        wkt = geometry.bbox_wkt(arcpy.Extent(0, 0, 1, 1))
        assert wkt.startswith("POLYGON ((")
        assert "MULTI" not in wkt and " Z" not in wkt

    def test_coordinates_are_rounded_and_trailing_zeros_dropped(self):
        wkt = geometry.bbox_wkt(arcpy.Extent(139.123456789, 35.0, 140.0, 36.0))
        assert wkt.startswith("POLYGON ((139.123457 35, 140 35, 140 36, 139.123457 36")


class TestDescribeBounds:
    def test_reads_as_compass_bounds_in_wgs84(self):
        prepared = geometry.PreparedPolygon(
            wkt="POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))",
            area_sqkm=1.0,
            west=139.5,
            south=35.2,
            east=139.9,
            north=35.6,
        )
        assert prepared.describe_bounds() == (
            "W 139.50000  S 35.20000  E 139.90000  N 35.60000  (WGS 84)"
        )


class TestExtentToWgs84:
    def test_already_wgs84_is_returned_untouched(self):
        extent = arcpy.Extent(0, 0, 1, 1, arcpy.SpatialReference(4326))
        assert geometry.extent_to_wgs84(extent) is extent

    def test_missing_coordinate_system_is_an_error_with_a_sentence(self):
        with pytest.raises(geometry.GeometryError) as excinfo:
            geometry.extent_to_wgs84(arcpy.Extent(0, 0, 1, 1, None))
        assert "coordinate system" in str(excinfo.value)

    def test_unknown_coordinate_system_is_treated_as_missing(self):
        with pytest.raises(geometry.GeometryError):
            geometry.extent_to_wgs84(arcpy.Extent(0, 0, 1, 1, arcpy.SpatialReference(0)))
