"""The toolbox module itself, loaded with the arcpy stand-in from conftest.

Anything that builds dialogs or touches data needs ArcGIS Pro; what is tested
here is the logic around it: labels, error sentences, request bodies, polling.
"""

from __future__ import annotations

import importlib.machinery
import importlib.util
from datetime import datetime
from pathlib import Path
from types import SimpleNamespace

import pytest

from sateais_arcgis.core import geometry, job_summary, settings
from sateais_arcgis.core.api import errors
from sateais_arcgis.core.api.types import AnalysisType, Job, JobStatus

PYT = Path(__file__).resolve().parents[2] / "sateais_arcgis" / "esri" / "toolboxes" / "SateAIs.pyt"


@pytest.fixture(scope="module")
def toolbox():
    loader = importlib.machinery.SourceFileLoader("sateais_toolbox", str(PYT))
    spec = importlib.util.spec_from_loader(loader.name, loader)
    module = importlib.util.module_from_spec(spec)
    loader.exec_module(module)
    return module


def _param(name, value=None, text=None):
    """A parameter as the tool sees it: `.name`, `.value`, `.valueAsText`."""
    return SimpleNamespace(name=name, value=value, valueAsText=text if text is not None else value)


PREPARED = geometry.PreparedPolygon(
    wkt="POLYGON ((139.5 35.2, 139.9 35.2, 139.9 35.6, 139.5 35.6, 139.5 35.2))",
    area_sqkm=1.0,
    west=139.5,
    south=35.2,
    east=139.9,
    north=35.6,
)


class TestDetectionLabels:
    def test_every_api_type_is_offered(self, toolbox):
        assert set(toolbox.DETECTION_LABELS.values()) == {t.value for t in AnalysisType}

    def test_labels_are_the_shared_display_names(self, toolbox):
        assert list(job_summary.ANALYSIS_LABELS.values()) == toolbox.DETECTION_CHOICES


class TestReport:
    """Every failure becomes a sentence; the server's own text never shows."""

    @pytest.mark.parametrize(
        ("exc", "expected"),
        [
            (errors.AuthenticationError(401, "UNAUTHORIZED", "bad key"), "Invalid API key"),
            (errors.PermissionDeniedError(403, "FORBIDDEN", "nope"), "does not have access"),
            (
                errors.InsufficientCreditsError(402, "INSUFFICIENT_CREDITS", "x"),
                "not enough credits",
            ),
            (errors.NotFoundError(410, "GONE", "retention period"), "30-day retention"),
            (errors.NotFoundError(404, "SCENE_NOT_FOUND", "x"), "No satellite scene was found"),
            (errors.NotFoundError(404, "JOB_NOT_FOUND", "x"), "Job not found"),
            (errors.ConflictError(409, "NOT_COMPLETED", "x"), "has not finished yet"),
            (errors.RateLimitError(429, "RATE_LIMITED", "slow down"), "Too many requests"),
            (
                errors.ValidationError(
                    400,
                    "VALIDATION_ERROR",
                    "Polygon area (52.6 km²) exceeds 50 km² limit for endpoint ship",
                ),
                "over the 50 km² limit",
            ),
            (errors.ValidationError(400, "INSUFFICIENT_SCENES", "x"), "not enough scenes"),
            (
                errors.ValidationError(400, "VALIDATION_ERROR", "field x is required"),
                "could not be run as written",
            ),
            (
                errors.InvalidAnalysisRequestError("preview: unexpected keyword"),
                "could not be run as written",
            ),
            (errors.UnsupportedResultFormatError("application/zip"), "not a map layer"),
            (errors.ServerError(502, "UPSTREAM_ERROR", "boom"), "temporarily unavailable"),
            (
                errors.APIError(0, None, "connection error: refused"),
                "Check your network connection",
            ),
            (
                settings.AuthNotConfiguredError("No API key is configured."),
                "No API key is configured.",
            ),
            (geometry.GeometryError("No area of interest was given."), "No area of interest"),
        ],
    )
    def test_maps_to_a_sentence(self, toolbox, exc, expected):
        assert expected in toolbox._report(exc)

    @pytest.mark.parametrize(
        "exc",
        [
            errors.AuthenticationError(401, "UNAUTHORIZED", "token signature mismatch"),
            errors.ServerError(502, "UPSTREAM_ERROR", "lambda timed out after 30s"),
            errors.NotFoundError(404, "JOB_NOT_FOUND", "no row for id 123"),
            errors.ValidationError(400, "VALIDATION_ERROR", "unexpected field 'foo'"),
        ],
    )
    def test_server_text_and_status_codes_are_never_shown(self, toolbox, exc):
        sentence = toolbox._report(exc)
        assert exc.message not in sentence
        assert str(exc.status_code) not in sentence
        assert exc.code not in sentence


class TestGuarded:
    """A known failure is one error line and a normal return: the error message
    marks the run as failed, and nothing reaches the framework to print a traceback."""

    def test_known_failures_become_one_error_line(self, toolbox, monkeypatch):
        errors_added = []
        monkeypatch.setattr(toolbox.arcpy, "AddError", errors_added.append)

        class Tool:
            @toolbox.guarded
            def execute(self, parameters, messages):
                raise errors.ServerError(503, "UNAVAILABLE", "boom")

        assert Tool().execute([], None) is None
        assert errors_added == [toolbox.SERVER_UNAVAILABLE]

    def test_fail_stops_the_tool_after_one_error_line(self, toolbox, monkeypatch):
        errors_added = []
        monkeypatch.setattr(toolbox.arcpy, "AddError", errors_added.append)

        class Tool:
            @toolbox.guarded
            def execute(self, parameters, messages):
                toolbox._fail("The job failed.")
                errors_added.append("not reached")

        assert Tool().execute([], None) is None
        assert errors_added == ["The job failed."]

    def test_bugs_keep_their_traceback(self, toolbox):
        class Tool:
            @toolbox.guarded
            def execute(self, parameters, messages):
                raise ValueError("a bug")

        with pytest.raises(ValueError):
            Tool().execute([], None)


class TestRequestKwargs:
    def test_single_date_types_send_a_date_and_a_direction(self, toolbox):
        params = [
            _param("detection_type", "Ship detection"),
            _param("date", datetime(2026, 1, 2)),
            _param("date_direction", "Before"),
            _param("date_start"),
            _param("date_end"),
            _param("orbit_direction", "Ascending"),
        ]
        assert toolbox._AoiToolBase._request_kwargs(params, PREPARED) == {
            "polygon": PREPARED.wkt,
            "orbit_direction": "ascending",
            "date": "2026-01-02",
            "date_direction": "before",
        }

    def test_date_range_types_send_start_and_end(self, toolbox):
        params = [
            _param("detection_type", "New building detection"),
            _param("date"),
            _param("date_direction", "Nearest"),
            _param("date_start", datetime(2026, 1, 1)),
            _param("date_end", datetime(2026, 6, 30)),
            _param("orbit_direction", "Automatic"),
        ]
        assert toolbox._AoiToolBase._request_kwargs(params, PREPARED) == {
            "polygon": PREPARED.wkt,
            "orbit_direction": "auto",
            "date_start": "2026-01-01",
            "date_end": "2026-06-30",
        }


class TestStatusText:
    @pytest.fixture(autouse=True)
    def _no_stored_key(self, monkeypatch, tmp_path):
        monkeypatch.setattr(settings, "get_stored_api_key", lambda: None)
        monkeypatch.setattr(settings, "CREDENTIALS_FILE", tmp_path / "missing")
        monkeypatch.delenv("SATEAIS_API_KEY", raising=False)

    def test_unconfigured_is_marked(self, toolbox):
        text = toolbox.ConfigureAccount._status_text()
        assert text.startswith("✕") and "NOT CONFIGURED" in text

    def test_configured_shows_a_masked_key_and_its_source(self, toolbox, monkeypatch):
        monkeypatch.setenv("SATEAIS_API_KEY", "sk_live_abcdefghijkl1234")
        text = toolbox.ConfigureAccount._status_text()
        assert text.startswith("✓")
        assert "sk_live_****1234" in text
        assert "abcdefghijkl" not in text
        assert "environment variable" in text


class _Jobs:
    def __init__(self, answers):
        self.answers = list(answers)

    def status(self, job_id):
        answer = self.answers.pop(0)
        if isinstance(answer, Exception):
            raise answer
        return answer


def _client_with(answers):
    return SimpleNamespace(jobs=_Jobs(answers))


class TestWait:
    @pytest.fixture(autouse=True)
    def _no_sleep(self, toolbox, monkeypatch):
        monkeypatch.setattr(toolbox, "_sleep", lambda seconds: None)

    def test_returns_the_job_once_it_is_terminal(self, toolbox):
        done = Job(job_id="j", status=JobStatus.COMPLETED)
        client = _client_with([Job(job_id="j", status=JobStatus.PROCESSING), done])
        assert toolbox.RunDetection._wait(client, "j") is done

    def test_tolerates_a_few_failed_status_checks(self, toolbox):
        done = Job(job_id="j", status=JobStatus.FAILED)
        flaky = [errors.ServerError(502, None, "x")] * (toolbox.POLL_ERROR_BUDGET - 1) + [done]
        assert toolbox.RunDetection._wait(_client_with(flaky), "j") is done

    def test_gives_up_after_the_error_budget(self, toolbox):
        flaky = [errors.ServerError(502, None, "x")] * toolbox.POLL_ERROR_BUDGET
        with pytest.raises(errors.ServerError):
            toolbox.RunDetection._wait(_client_with(flaky), "j")

    def test_a_job_the_server_does_not_know_is_not_retried(self, toolbox):
        client = _client_with([errors.NotFoundError(404, "JOB_NOT_FOUND", "x")])
        with pytest.raises(errors.NotFoundError):
            toolbox.RunDetection._wait(client, "j")
