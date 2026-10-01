"""Unit tests for the pre-run preview: response parsing and the client facade."""

from __future__ import annotations

from typing import Any

import pytest

from sateais_arcgis.core.api.client import Client
from sateais_arcgis.core.api.errors import InvalidAnalysisRequestError
from sateais_arcgis.core.api.types import AnalysisRequest, Preview, preview_from_dict

# A full response from POST /analyze/{endpoint}/preview.
FULL_PAYLOAD: dict[str, Any] = {
    "endpoint_id": "newbuilding",
    "area_sqkm": 78.4,
    "coverage": {
        "method": "estimated",
        "requested_area_sqkm": 100.2,
        "ratio": 0.78,
        "polygon": "POLYGON ((139.000000 35.000000, 139.110000 35.000000, 139.000000 35.000000))",
    },
    "credits": {"estimated": 1.0, "balance": 480.0, "sufficient": True},
    "warnings": [
        {"code": "LOW_AOI_COVERAGE", "message": "Scenes cover only 78% of the requested area."}
    ],
}


class TestPreviewFromDict:
    def test_full_payload(self):
        p = preview_from_dict(FULL_PAYLOAD)
        assert p.endpoint_id == "newbuilding"
        assert p.area_sqkm == 78.4
        assert p.credits is not None
        assert p.credits.estimated == 1.0
        assert p.credits.sufficient is True
        assert p.coverage is not None
        assert p.coverage.ratio == 0.78
        assert p.coverage.polygon.startswith("POLYGON")
        assert len(p.warnings) == 1

    def test_coverage_omitted_means_unknown(self):
        # When the catalogue search times out only credits come back. No
        # coverage means unknown, not fully covered.
        p = preview_from_dict({"credits": {"estimated": 81.44, "balance": 100, "sufficient": True}})
        assert p.coverage is None
        assert p.credits is not None
        assert p.credits.estimated == 81.44

    def test_null_estimate_survives_as_none(self):
        # estimated: null means "not known before the run", never zero.
        p = preview_from_dict(
            {"credits": {"estimated": None, "balance": 480.0, "sufficient": None}}
        )
        assert p.credits is not None
        assert p.credits.estimated is None
        assert p.credits.sufficient is None

    def test_empty_and_garbage_are_safe(self):
        p = preview_from_dict({})
        assert p == Preview()
        p = preview_from_dict(
            {"credits": "?", "coverage": 3, "warnings": ["x", {"code": "A", "message": "m"}]}
        )
        assert p.credits is None
        assert p.coverage is None
        assert p.warnings == [{"code": "A", "message": "m"}]

    def test_non_finite_numbers_become_none(self):
        # json.loads accepts NaN / Infinity by default. Kept as numbers, they
        # would be shown as facts rather than as unknown.
        import json

        raw = json.loads('{"credits":{"estimated":NaN},"coverage":{"ratio":Infinity}}')
        p = preview_from_dict(raw)
        assert p.credits is not None
        assert p.credits.estimated is None
        assert p.coverage is not None
        assert p.coverage.ratio is None

    def test_bool_is_not_a_number(self):
        p = preview_from_dict({"area_sqkm": True})
        assert p.area_sqkm is None

    def test_empty_polygon_becomes_none(self):
        p = preview_from_dict({"coverage": {"ratio": 1.0, "polygon": ""}})
        assert p.coverage is not None
        assert p.coverage.polygon is None


class FakeApiClient:
    """Captures preview requests; other methods are unused here."""

    def __init__(self) -> None:
        self.previewed: list[AnalysisRequest] = []

    def preview_analysis(self, request: AnalysisRequest) -> Preview:
        self.previewed.append(request)
        return preview_from_dict(FULL_PAYLOAD)

    def close(self) -> None:  # pragma: no cover - interface completeness
        pass


class TestClientPreview:
    def test_builds_and_validates_request(self):
        api = FakeApiClient()
        client = Client(api_key="sk_test", api=api)
        preview = client.analyze.preview(
            "newbuilding",
            polygon="POLYGON((0 0, 1 0, 1 1, 0 1, 0 0))",
            date_start="2026-01-01",
            date_end="2026-06-30",
        )
        assert preview.credits is not None
        assert len(api.previewed) == 1
        request = api.previewed[0]
        assert request.analysis_type.value == "newbuilding"
        assert request.date_start == "2026-01-01"

    def test_incomplete_request_fails_client_side(self):
        # Same validation as a submission; a lax preview would fail only on submit.
        api = FakeApiClient()
        client = Client(api_key="sk_test", api=api)
        with pytest.raises(InvalidAnalysisRequestError):
            client.analyze.preview("newbuilding", polygon="POLYGON((0 0, 1 0, 1 1, 0 1, 0 0))")
        assert api.previewed == []

    def test_bad_type_and_bad_kwarg_are_input_errors(self):
        # A raw ValueError / TypeError would look like a network failure to the caller.
        api = FakeApiClient()
        client = Client(api_key="sk_test", api=api)
        with pytest.raises(InvalidAnalysisRequestError):
            client.analyze.preview("nope", polygon="P", date_start="a", date_end="b")
        with pytest.raises(InvalidAnalysisRequestError):
            client.analyze.preview("newbuilding", polygonn="typo")
        assert api.previewed == []


class TestSceneShortagePayload:
    """Pin the shape of the server response."""

    # The response when no scene exists for the inputs.
    PAYLOAD = {
        "area_sqkm": None,
        "coverage": None,
        "credits": {"estimated": 0.04, "balance": 480.0, "sufficient": True},
        "warnings": [
            {
                "code": "SCENE_NOT_FOUND",
                "message": "No scenes are available for this area and period.",
            }
        ],
    }

    def test_parses_without_coverage(self):
        p = preview_from_dict(self.PAYLOAD)
        assert p.coverage is None
        assert p.area_sqkm is None
        # A cost comes back, but with no scene it is not a sign the job can run.
        assert p.credits is not None
        assert p.credits.estimated == 0.04
        assert p.warnings[0]["code"] == "SCENE_NOT_FOUND"
