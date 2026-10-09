"""Every case in contract/wording-cases.json, against this implementation.

The same file drives the C# tests, so a sentence cannot change in one client without
the other noticing.
"""

from __future__ import annotations

import json
from datetime import timedelta
from pathlib import Path

import pytest

from sateais_arcgis.core import job_summary, wording

CONTRACT = json.loads(
    (Path(__file__).resolve().parents[2] / "contract" / "wording-cases.json").read_text(
        encoding="utf-8"
    )
)


def _parse_iso8601(value):
    parsed = job_summary.parse_iso8601(value)
    return parsed.isoformat() if parsed else None


def _job_meta_fields(*args):
    return [list(pair) for pair in wording.job_meta_fields(*args)]


FUNCTIONS = {
    "credits_label": wording.credits_label,
    "coverage_label": wording.coverage_label,
    "coverage_is_partial": wording.coverage_is_partial,
    "balance_note": wording.balance_note,
    "area_km2_label": wording.area_km2_label,
    "took_label": wording.took_label,
    "format_credits": wording.format_credits,
    "job_meta_fields": _job_meta_fields,
    "area_limit_reason": wording.area_limit_reason,
    "failure_label": wording.failure_label,
    "warning_messages": wording.warning_messages,
    "scenes_unavailable": wording.scenes_unavailable,
    "format_analysis_label": job_summary.format_analysis_label,
    "format_detection_summary": job_summary.format_detection_summary,
    "format_detection_outcome": job_summary.format_detection_outcome,
    "format_detection_count": job_summary.format_detection_count,
    "format_relative_seconds": lambda s: job_summary.format_relative(timedelta(seconds=s)),
    "parse_iso8601": _parse_iso8601,
}


def _cases():
    for name, cases in CONTRACT.items():
        if name in FUNCTIONS:
            for case in cases:
                yield pytest.param(name, case["args"], case["expected"], id=f"{name}{case['args']}")


@pytest.mark.parametrize(("name", "args", "expected"), list(_cases()))
def test_matches_the_contract(name, args, expected):
    assert FUNCTIONS[name](*args) == expected


def test_every_function_in_the_contract_is_covered():
    in_contract = {k for k in CONTRACT if k not in ("_about", "constants")}
    assert in_contract == set(FUNCTIONS)


def test_constants_match():
    constants = CONTRACT["constants"]
    assert constants["LEGEND_REQUESTED"] == wording.LEGEND_REQUESTED
    assert constants["LEGEND_COVERED"] == wording.LEGEND_COVERED
    assert constants["LEGEND_NOT_COVERED"] == wording.LEGEND_NOT_COVERED
    assert constants["CHECKING"] == wording.CHECKING
    assert constants["SCENES_UNAVAILABLE_HINT"] == wording.SCENES_UNAVAILABLE_HINT
    assert constants["COVERAGE_UNCHECKED"] == wording.COVERAGE_UNCHECKED
    assert constants["ESTIMATE_FAILED"] == wording.ESTIMATE_FAILED
    assert constants["SETUP_TITLE"] == wording.SETUP_TITLE
    assert constants["ANALYSIS_LABELS"] == job_summary.ANALYSIS_LABELS
    assert constants["DETECTION_NOUNS"] == {
        k: list(v) for k, v in job_summary.DETECTION_NOUNS.items()
    }
