"""The pure parts of core.layers: symbology JSON and result summaries."""

from __future__ import annotations

import json

import pytest

from sateais_arcgis.core import layers
from sateais_arcgis.core.api.types import AnalysisType


def _renderer(text: str) -> dict:
    prefix = "JSONRENDERER="
    assert text.startswith(prefix)
    return json.loads(text[len(prefix) :])


class TestCoverageRenderer:
    def test_three_classes_keyed_on_kind_in_legend_order(self):
        renderer = _renderer(layers.coverage_renderer_json())
        assert renderer["type"] == "CIMUniqueValueRenderer"
        assert renderer["fields"] == [layers.COVERAGE_FIELD]
        classes = renderer["groups"][0]["classes"]
        assert [c["label"] for c in classes] == ["Requested", "Covered", "Not covered"]
        assert [c["values"][0]["fieldValues"] for c in classes] == [
            ["Requested"],
            ["Covered"],
            ["Not covered"],
        ]

    def test_requested_is_an_outline_only(self):
        renderer = _renderer(layers.coverage_renderer_json())
        requested = renderer["groups"][0]["classes"][0]["symbol"]["symbol"]
        assert [layer["type"] for layer in requested["symbolLayers"]] == ["CIMSolidStroke"]


class TestDetectionRenderer:
    @pytest.mark.parametrize("analysis_type", [t.value for t in AnalysisType])
    def test_every_detection_type_has_its_own_colour(self, analysis_type):
        renderer = _renderer(layers.detection_renderer_json(analysis_type))
        assert renderer["type"] == "CIMSimpleRenderer"
        stroke, fill = renderer["symbol"]["symbol"]["symbolLayers"]
        rgb, _ = layers.DETECTION_STYLE[analysis_type]
        assert stroke["color"]["values"][:3] == list(rgb)
        assert fill["color"]["values"][:3] == list(rgb)

    def test_colours_are_distinct(self):
        colours = {rgb for rgb, _ in layers.DETECTION_STYLE.values()}
        assert len(colours) == len(layers.DETECTION_STYLE)

    def test_fill_alpha_is_converted_to_the_cim_percent_scale(self):
        renderer = _renderer(layers.detection_renderer_json("ship"))
        _, fill = renderer["symbol"]["symbol"]["symbolLayers"]
        assert fill["color"]["values"][3] == round(100 * 100 / 255)

    def test_unknown_type_still_renders(self):
        renderer = _renderer(layers.detection_renderer_json("something-new"))
        assert renderer["type"] == "CIMSimpleRenderer"
        renderer = _renderer(layers.detection_renderer_json(None))
        assert renderer["type"] == "CIMSimpleRenderer"


def _collection(*deviations):
    return {
        "type": "FeatureCollection",
        "features": [
            {"type": "Feature", "geometry": None, "properties": {"deviation": d}}
            for d in deviations
        ],
    }


class TestDescribeResult:
    def test_names_what_was_found(self):
        geojson = _collection(None, None, None)
        assert layers.describe_result(geojson, "ship") == "3 ships found"

    def test_an_empty_result_says_so(self):
        assert layers.describe_result(_collection(), "ship") == "No ships found"

    def test_timeseries_counts_changed_cells_and_shows_the_grid_size(self):
        geojson = _collection(0.10, 0.00, 0.20)
        assert layers.describe_result(geojson, "timeseries") == (
            "2 changes found across 3 grid cells"
        )

    def test_unknown_type_uses_a_neutral_noun(self):
        assert layers.describe_result(_collection(None), None) == "1 detection found"
