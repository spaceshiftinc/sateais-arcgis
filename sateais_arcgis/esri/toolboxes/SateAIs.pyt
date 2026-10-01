"""SateAIs API for ArcGIS Pro — Python Toolbox.

Satellite SAR detection as ArcGIS Pro geoprocessing tools: ship, oil slick,
building change and time-series change.

Design:
  - No third-party packages. ArcGIS Pro's Python environment is read-only, so
    the toolbox uses only the standard library and arcpy.
  - Let the framework do the UI. Dialogs, validation, progress and history come
    from geoprocessing, which is also what puts these tools in ModelBuilder and
    batch runs for free.
  - Messages are in English and come from the shared `wording` module, so the
    same situation reads the same in every SateAIs client. Server text and
    internal identifiers are never shown.

Category names start with a number because the geoprocessing pane sorts
alphabetically.
"""

from __future__ import annotations

import contextlib
import functools
import sys
import time
from pathlib import Path
from typing import NoReturn

import arcpy

# When shipped as a zip, this file sits at <root>/sateais_arcgis/esri/toolboxes/,
# so <root> has to be importable.
_ROOT = Path(__file__).resolve().parents[3]
if str(_ROOT) not in sys.path:
    sys.path.insert(0, str(_ROOT))

from sateais_arcgis import __version__  # noqa: E402
from sateais_arcgis.core import (  # noqa: E402
    geometry,
    job_summary,
    layers,
    result_stats,
    settings,
    wording,
)
from sateais_arcgis.core.api import errors as api_errors  # noqa: E402
from sateais_arcgis.core.api.client import Client  # noqa: E402
from sateais_arcgis.core.api.types import AnalysisType  # noqa: E402

# Label shown to the user -> identifier sent to the API. The labels come from
# `job_summary` so every message names a detection the same way.
DETECTION_LABELS: dict[str, str] = {
    label: kind for kind, label in job_summary.ANALYSIS_LABELS.items()
}
DETECTION_CHOICES = list(DETECTION_LABELS)

# These take a single reference date; the rest take a date range.
SINGLE_DATE_TYPES = {AnalysisType.SHIP.value, AnalysisType.OILSLICK.value}

ORBIT_LABELS = {"Automatic": "auto", "Ascending": "ascending", "Descending": "descending"}
DATE_DIRECTION_LABELS = {"Nearest": "nearest", "Before": "before", "After": "after"}

POLL_INTERVAL_SECONDS = 30
#: Consecutive failed status checks tolerated before the wait gives up.
POLL_ERROR_BUDGET = 5
MAX_WAIT_SECONDS = 24 * 60 * 60

# Sentences for failures the API reports. Same wording as the QGIS plugin.
AUTH_FAILED = "Invalid API key. Check the key in '1 Setup > Configure Account'."
PERMISSION_DENIED = "Your account does not have access to this analysis. Please contact support."
JOB_NOT_FOUND = "Job not found. It may have been removed on the server."
JOB_NOT_COMPLETED = "The job has not finished yet. Try again in a moment."
RATE_LIMITED = "Too many requests. Wait a moment and try again."
INVALID_REQUEST = "This request could not be run as written."
NOT_A_MAP_LAYER = (
    "This result is not a map layer, so it cannot be added to the map. "
    "Open it in the console instead."
)
SERVER_UNAVAILABLE = "The server is temporarily unavailable. Please try again later."
NO_CONNECTION = "Could not reach the SateAIs API. Check your network connection."


def by_name(parameters) -> dict:
    """Look parameters up by name.

    Positional access breaks as soon as a parameter is inserted, so never index.
    """
    return {p.name: p for p in parameters}


def _client(api_key: str | None = None) -> Client:
    return Client(api_key=settings.require_api_key(api_key))


def _endpoint_of(label: str) -> str:
    return DETECTION_LABELS[label]


def _report(exc: Exception) -> str:
    """The sentence for a failure. Server text and status codes are never shown."""
    if isinstance(exc, api_errors.AuthenticationError):
        return AUTH_FAILED
    if isinstance(exc, api_errors.PermissionDeniedError):
        return PERMISSION_DENIED
    if isinstance(exc, api_errors.InsufficientCreditsError):
        return wording.failure_label("INSUFFICIENT_CREDITS", None)
    if isinstance(exc, api_errors.NotFoundError):
        if exc.code == "GONE" or exc.code in wording.SCENES_UNAVAILABLE_CODES:
            return wording.failure_label(exc.code, None)
        return JOB_NOT_FOUND
    if isinstance(exc, api_errors.ConflictError):
        return JOB_NOT_COMPLETED
    if isinstance(exc, api_errors.RateLimitError):
        return RATE_LIMITED
    if isinstance(exc, api_errors.ValidationError):
        if exc.code in wording.SCENES_UNAVAILABLE_CODES:
            return wording.failure_label(exc.code, None)
        return wording.area_limit_reason(exc.message) or INVALID_REQUEST
    if isinstance(exc, api_errors.InvalidAnalysisRequestError):
        return INVALID_REQUEST
    if isinstance(exc, api_errors.UnsupportedResultFormatError):
        return NOT_A_MAP_LAYER
    if isinstance(exc, api_errors.APIError):
        return NO_CONNECTION if exc.status_code == 0 else SERVER_UNAVAILABLE
    # Our own exceptions already carry a sentence.
    return str(exc)


def _describe_job_failure(job) -> str:
    return wording.failure_label(job.error_code, job.error_message)


class _ToolFailed(Exception):
    """Stops a tool after its error has been reported. Never leaves `guarded`."""


def _fail(sentence: str) -> NoReturn:
    """Stop the tool with one sentence in the messages, and no traceback.

    An error message is what marks the run as failed. Letting any exception
    reach the framework would add a traceback, so the stop is swallowed by
    `guarded`.
    """
    arcpy.AddError(sentence)
    raise _ToolFailed


# Failures that have a sentence. Anything else is a bug and keeps its traceback.
_REPORTABLE = (api_errors.SateAIsError, settings.AuthNotConfiguredError, geometry.GeometryError)


def guarded(execute):
    """Turn a known failure into one sentence instead of a traceback."""

    @functools.wraps(execute)
    def wrapper(self, parameters, messages):
        try:
            return execute(self, parameters, messages)
        except _ToolFailed:
            return None
        except _REPORTABLE as exc:
            arcpy.AddError(_report(exc))
            return None

    return wrapper


def _sleep(seconds: int) -> None:
    """Sleep in one-second steps so Cancel takes effect quickly."""
    for _ in range(seconds):
        if arcpy.env.isCancelled:
            _fail("Cancelled.")
        time.sleep(1)


def _load_result(client: Client, job_id: str, endpoint: str | None, out_param) -> None:
    """Add a completed job's detections to the output feature class."""
    geojson = client.jobs.result(job_id)
    layers.geojson_to_feature_class(geojson, out_param.valueAsText, endpoint)
    out_param.symbology = layers.detection_renderer_json(endpoint)

    summary = layers.describe_result(geojson, endpoint)
    if result_stats.count_detections(geojson, endpoint) == 0:
        arcpy.AddWarning(summary)
    else:
        arcpy.AddMessage(summary)


# ===========================================================================
# Toolbox
# ===========================================================================


class Toolbox:
    def __init__(self) -> None:
        self.label = "SateAIs"
        # The alias is the namespace for calling these from Python:
        #   arcpy.sateais.RunDetection(...)
        self.alias = "sateais"
        self.description = f"Satellite SAR detection from ArcGIS Pro (v{__version__})"
        self.tools = [
            ConfigureAccount,
            RunDetection,
            DownloadResult,
            ListJobs,
        ]


# ===========================================================================
# 1 Setup
# ===========================================================================


class ConfigureAccount:
    def __init__(self) -> None:
        self.label = "Configure Account"
        self.description = (
            "Store your SateAIs API key. The key goes into the operating system's "
            "credential store, not a plain-text file. Create a key in the console "
            "(console.spcsft.com) under Settings > API Keys."
        )
        self.category = "1 Setup"

    def getParameterInfo(self):
        # Show the current state first. The input below is always blank, so
        # without this the key looks unset and gets re-entered every time.
        # Disabled, because it is a label rather than an input.
        status = arcpy.Parameter(
            displayName="Current key",
            name="status",
            datatype="GPString",
            parameterType="Optional",
            direction="Input",
        )
        status.enabled = False
        status.value = self._status_text()

        api_key = arcpy.Parameter(
            displayName="New API key (leave blank to keep the current one)",
            name="api_key",
            datatype="GPStringHidden",
            parameterType="Optional",
            direction="Input",
        )
        clear = arcpy.Parameter(
            displayName="Remove the stored key",
            name="clear",
            datatype="GPBoolean",
            parameterType="Optional",
            direction="Input",
        )
        clear.value = False
        return [status, api_key, clear]

    @staticmethod
    def _status_text() -> str:
        """One line describing the key in use.

        Parameters cannot carry an icon, so the marker is part of the text.

        Makes no network call: this runs on every change, and the dialog has to
        stay responsive. Connectivity is checked when the tool runs.
        """
        key = settings.resolve_api_key()
        if not key:
            return "✕  NOT CONFIGURED - paste a key below"
        return (
            f"✓  AUTHENTICATED - {settings.mask_api_key(key)} (from {settings.describe_source()})"
        )

    def updateParameters(self, parameters):
        # Keep the status line in step with reality.
        p = by_name(parameters)
        p["status"].value = self._status_text()
        # Entering a new key while removing the stored one is contradictory.
        p["api_key"].enabled = not bool(p["clear"].value)

    def updateMessages(self, parameters):
        # Warn only when nothing is configured; a healthy state gets no icon.
        p = by_name(parameters)
        if not settings.resolve_api_key():
            p["status"].setWarningMessage(
                "No API key is stored yet. Paste one below and run this tool."
            )

        # A warning, not an error: the server is the authority on what a key is.
        key = p["api_key"].valueAsText
        if key and not key.startswith("sk_"):
            p["api_key"].setWarningMessage(
                "SateAIs API keys start with sk_. Check that the whole key was pasted."
            )

    @guarded
    def execute(self, parameters, messages):
        p = by_name(parameters)
        if p["clear"].value:
            settings.clear_api_key()
            arcpy.AddMessage("Removed the stored API key.")
            return

        key = p["api_key"].valueAsText
        if key:
            settings.set_api_key(key)
            arcpy.AddMessage(f"Stored the API key: {settings.mask_api_key(key)}")

        resolved = settings.resolve_api_key()
        arcpy.AddMessage(f"Key in use: {settings.mask_api_key(resolved)}")
        arcpy.AddMessage(f"Read from: {settings.describe_source()}")

        if resolved:
            try:
                me = _client().me()
                # The balance lives under `credits`.
                credits = me.get("credits") or {}
                balance = credits.get("balance")
                if isinstance(balance, (int, float)):
                    arcpy.AddMessage(
                        f"Connected as {me.get('display_name') or me.get('email') or 'you'}. "
                        f"Credit balance: {wording.format_credits(float(balance))}"
                    )
                else:
                    arcpy.AddMessage("Connected.")
            except Exception as exc:  # noqa: BLE001
                arcpy.AddWarning(f"Could not reach the API: {_report(exc)}")


# ===========================================================================
# 2 Analysis
# ===========================================================================


class _AoiToolBase:
    """Shared area-of-interest and date parameters.

    `updateParameters` is the only place a geoprocessing dialog can change
    shape, so the date fields are shown and hidden from there.
    """

    def _aoi_parameters(self):
        detection = arcpy.Parameter(
            displayName="Detection type",
            name="detection_type",
            datatype="GPString",
            parameterType="Required",
            direction="Input",
        )
        detection.filter.type = "ValueList"
        detection.filter.list = DETECTION_CHOICES
        detection.value = DETECTION_CHOICES[0]

        # Rectangles only. A feature-set parameter would offer points, lines
        # and circles that the analysis cannot use; `GPExtent` cannot.
        aoi = arcpy.Parameter(
            displayName="Area of interest (rectangle)",
            name="aoi",
            datatype="GPExtent",
            parameterType="Required",
            direction="Input",
        )

        date = arcpy.Parameter(
            displayName="Reference date",
            name="date",
            datatype="GPDate",
            parameterType="Optional",
            direction="Input",
        )
        date_direction = arcpy.Parameter(
            displayName="Scene selection",
            name="date_direction",
            datatype="GPString",
            parameterType="Optional",
            direction="Input",
        )
        date_direction.filter.type = "ValueList"
        date_direction.filter.list = list(DATE_DIRECTION_LABELS)
        date_direction.value = "Nearest"

        date_start = arcpy.Parameter(
            displayName="Start date",
            name="date_start",
            datatype="GPDate",
            parameterType="Optional",
            direction="Input",
        )
        date_end = arcpy.Parameter(
            displayName="End date",
            name="date_end",
            datatype="GPDate",
            parameterType="Optional",
            direction="Input",
        )

        orbit = arcpy.Parameter(
            displayName="Orbit direction",
            name="orbit_direction",
            datatype="GPString",
            parameterType="Optional",
            direction="Input",
        )
        orbit.filter.type = "ValueList"
        orbit.filter.list = list(ORBIT_LABELS)
        orbit.value = "Automatic"

        return [detection, aoi, date, date_direction, date_start, date_end, orbit]

    @staticmethod
    def _apply_date_visibility(parameters) -> None:
        """Show the date fields the chosen detection actually uses."""
        p = by_name(parameters)
        label = p["detection_type"].valueAsText
        endpoint = _endpoint_of(label) if label else None
        single = endpoint in SINGLE_DATE_TYPES
        p["date"].enabled = single
        p["date_direction"].enabled = single
        p["date_start"].enabled = not single
        p["date_end"].enabled = not single

    @staticmethod
    def _normalize_aoi_to_wgs84(parameters) -> None:
        """Rewrite the extent in EPSG:4326, in place.

        Left in the map's coordinate system, the numbers on screen would differ
        from the numbers sent. Rewriting the field makes them the same.

        Already-4326 values are left alone, so this cannot loop. Validation runs
        on half-typed input, so a failed conversion is ignored here and reported
        properly when the tool runs.
        """
        p = by_name(parameters)
        extent = p["aoi"].value
        if extent is None:
            return
        sr = getattr(extent, "spatialReference", None)
        if sr is None or sr.factoryCode == 4326:
            return
        with contextlib.suppress(Exception):
            p["aoi"].value = geometry.extent_to_wgs84(extent)

    @staticmethod
    def _validate_dates(parameters) -> None:
        p = by_name(parameters)
        label = p["detection_type"].valueAsText
        endpoint = _endpoint_of(label) if label else None
        if endpoint is None:
            return
        if endpoint in SINGLE_DATE_TYPES:
            if not p["date"].value:
                p["date"].setIDMessage("ERROR", 735, "Reference date")
        else:
            if not p["date_start"].value:
                p["date_start"].setIDMessage("ERROR", 735, "Start date")
            if not p["date_end"].value:
                p["date_end"].setIDMessage("ERROR", 735, "End date")
            if (
                p["date_start"].value
                and p["date_end"].value
                and p["date_start"].value > p["date_end"].value
            ):
                p["date_end"].setErrorMessage("The end date must be on or after the start date.")

    @staticmethod
    def _prepare_aoi(parameters) -> geometry.PreparedPolygon:
        return geometry.prepare_extent(by_name(parameters)["aoi"].value)

    @staticmethod
    def _request_kwargs(parameters, prepared: geometry.PreparedPolygon) -> dict:
        p = by_name(parameters)
        endpoint = _endpoint_of(p["detection_type"].valueAsText)
        kwargs: dict = {"polygon": prepared.wkt}

        orbit_label = p["orbit_direction"].valueAsText
        if orbit_label:
            kwargs["orbit_direction"] = ORBIT_LABELS[orbit_label]

        if endpoint in SINGLE_DATE_TYPES:
            kwargs["date"] = p["date"].value.strftime("%Y-%m-%d")
            direction_label = p["date_direction"].valueAsText
            if direction_label:
                kwargs["date_direction"] = DATE_DIRECTION_LABELS[direction_label]
        else:
            kwargs["date_start"] = p["date_start"].value.strftime("%Y-%m-%d")
            kwargs["date_end"] = p["date_end"].value.strftime("%Y-%m-%d")
        return kwargs

    @staticmethod
    def _report_area(prepared: geometry.PreparedPolygon) -> None:
        """Report the area being sent. This is what gets charged."""
        arcpy.AddMessage(f"Area of interest: {wording.area_km2_label(prepared.area_sqkm)}")
        arcpy.AddMessage(f"  {prepared.describe_bounds()}")


PREVIEW_ONLY = "Preview only (no credits are spent)"
SUBMIT_JOB = "Submit the job (credits are spent)"


class RunDetection(_AoiToolBase):
    def __init__(self) -> None:
        self.label = "Run Detection"
        self.description = (
            "Check which part of your area will actually be analysed and what it "
            "will cost, then optionally submit the job. The preview runs either "
            "way and spends no credits; only 'Submit the job' does."
        )
        self.category = "2 Analysis"

    def getParameterInfo(self):
        params = self._aoi_parameters()

        # A list, not a checkbox: parameter values persist between runs, and
        # the labels say outright which option spends credits.
        mode = arcpy.Parameter(
            displayName="What to do",
            name="mode",
            datatype="GPString",
            parameterType="Required",
            direction="Input",
        )
        mode.filter.type = "ValueList"
        mode.filter.list = [PREVIEW_ONLY, SUBMIT_JOB]
        mode.value = PREVIEW_ONLY

        wait = arcpy.Parameter(
            displayName="Wait for the job to finish",
            name="wait",
            datatype="GPBoolean",
            parameterType="Optional",
            direction="Input",
        )
        wait.value = True

        # Drawn in both modes - better than discovering poor coverage after
        # the job has been submitted.
        out_coverage = arcpy.Parameter(
            displayName="Coverage comparison",
            name="out_coverage",
            datatype="DEFeatureClass",
            parameterType="Optional",
            direction="Output",
        )
        out_coverage.value = r"memory\sateais_coverage"

        out_fc = arcpy.Parameter(
            displayName="Detections",
            name="out_features",
            datatype="DEFeatureClass",
            parameterType="Optional",
            direction="Output",
        )
        out_aoi = arcpy.Parameter(
            displayName="Area of interest outline",
            name="out_aoi",
            datatype="DEFeatureClass",
            parameterType="Optional",
            direction="Output",
        )
        job_id = arcpy.Parameter(
            displayName="Job ID",
            name="job_id",
            datatype="GPString",
            parameterType="Derived",
            direction="Output",
        )
        return [*params, mode, wait, out_coverage, out_fc, out_aoi, job_id]

    def updateParameters(self, parameters):
        self._apply_date_visibility(parameters)
        self._normalize_aoi_to_wgs84(parameters)
        p = by_name(parameters)
        submitting = p["mode"].valueAsText == SUBMIT_JOB
        waiting = submitting and bool(p["wait"].value)
        p["wait"].enabled = submitting
        p["out_features"].enabled = waiting
        p["out_aoi"].enabled = waiting

    def updateMessages(self, parameters):
        self._validate_dates(parameters)
        p = by_name(parameters)
        if p["mode"].valueAsText == SUBMIT_JOB:
            # Say so before the run, not after.
            p["mode"].setWarningMessage(
                "This will submit a job and spend credits. Switch to preview "
                "if you only want to check the coverage."
            )
            if p["wait"].value and not p["out_features"].valueAsText:
                p["out_features"].setIDMessage("ERROR", 735, "Detections")

    @guarded
    def execute(self, parameters, messages):
        arcpy.env.overwriteOutput = True

        p = by_name(parameters)
        submitting = p["mode"].valueAsText == SUBMIT_JOB

        prepared = self._prepare_aoi(parameters)
        self._report_area(prepared)

        endpoint = _endpoint_of(p["detection_type"].valueAsText)
        kwargs = self._request_kwargs(parameters, prepared)
        client = _client()

        # Preview first even when submitting, so coverage and estimated cost
        # are in the run log.
        preview = self._preview(client, endpoint, kwargs, prepared, p)

        if not submitting:
            arcpy.AddMessage("Preview only - no job was submitted and no credits were spent.")
            return

        if preview is not None and preview.credits is not None:
            arcpy.AddMessage(f"Submitting. {wording.credits_label(preview.credits.estimated)}")

        job = getattr(client.analyze, endpoint)(**kwargs)
        arcpy.AddMessage(f"Submitted job {job.job_id}")
        p["job_id"].value = job.job_id

        if not p["wait"].value:
            arcpy.AddMessage(
                "Not waiting for the job. Pass this job ID to "
                "'3 Results > Download Result' once it completes."
            )
            return

        job = self._wait(client, job.job_id)
        if job.is_failed:
            _fail(f"The job failed. {_describe_job_failure(job)}")

        _load_result(client, job.job_id, endpoint, p["out_features"])

        out_aoi = p["out_aoi"].valueAsText
        if out_aoi:
            layers.aoi_to_feature_class(prepared.wkt, out_aoi)

    @staticmethod
    def _preview(client, endpoint, kwargs, prepared, p):
        """Report coverage and estimated cost, and draw the comparison layer.

        A failed preview must not block the submission, so it warns and
        returns None.
        """
        try:
            preview = client.analyze.preview(endpoint, **kwargs)
        except _REPORTABLE as exc:
            arcpy.AddWarning(f"Could not preview the coverage: {_report(exc)}")
            return None

        if preview.area_sqkm is not None:
            arcpy.AddMessage(f"Area to be analysed: {wording.area_km2_label(preview.area_sqkm)}")
        if preview.credits is not None:
            arcpy.AddMessage(wording.credits_label(preview.credits.estimated))
            note = wording.balance_note(preview.credits.sufficient, preview.credits.balance)
            if note:
                arcpy.AddWarning(f"Your balance may not cover this: {note}")
        for line in wording.warning_messages(preview.warnings):
            arcpy.AddWarning(line)

        coverage = preview.coverage
        if coverage is None:
            arcpy.AddWarning(
                "The analysed area cannot be determined before the run for this "
                "input. Check the measured value once the job completes."
            )
        elif wording.coverage_is_partial(coverage.ratio):
            arcpy.AddWarning(
                wording.coverage_label(coverage.ratio)
                + " Widening the date range or splitting the area may help."
            )

        out_path = p["out_coverage"].valueAsText
        if out_path:
            covered_wkt = coverage.polygon if coverage else None
            _, areas = layers.coverage_comparison_to_feature_class(
                prepared.wkt, covered_wkt, out_path
            )
            p["out_coverage"].symbology = layers.coverage_renderer_json()
            for kind, km2 in areas.items():
                arcpy.AddMessage(f"  {kind}: {wording.area_km2_label(km2)}")
            if covered_wkt is None:
                arcpy.AddMessage(
                    "  Only the requested area is drawn - the analysed area is "
                    "not known before the run."
                )
        return preview

    @staticmethod
    def _wait(client: Client, job_id: str):
        """Poll until the job finishes.

        Geoprocessing runs on its own thread, so the map stays usable while
        waiting, and cancellation is honoured. A few failed status checks are
        tolerated; a job the server no longer knows is not retried.
        """
        arcpy.SetProgressor("default", "Waiting for the job to finish...")
        elapsed = 0
        errors = 0
        while elapsed < MAX_WAIT_SECONDS:
            try:
                job = client.jobs.status(job_id)
            except api_errors.NotFoundError:
                raise
            except api_errors.APIError:
                errors += 1
                if errors >= POLL_ERROR_BUDGET:
                    raise
                arcpy.AddWarning(
                    f"Could not check the job status ({errors} of {POLL_ERROR_BUDGET}). Retrying."
                )
            else:
                errors = 0
                if job.is_terminal:
                    return job
                arcpy.SetProgressorLabel(
                    f"Running for {elapsed // 60} min (status: {job.status.value})"
                )
            _sleep(POLL_INTERVAL_SECONDS)
            elapsed += POLL_INTERVAL_SECONDS
        _fail(
            "Gave up waiting after 24 hours. Pass the job ID to "
            "'3 Results > Download Result' once it completes."
        )


# ===========================================================================
# 3 Results
# ===========================================================================


class DownloadResult:
    def __init__(self) -> None:
        self.label = "Download Result"
        self.description = (
            "Add the detections from a completed job to the map. Results expire "
            "a while after the job completes, so save anything you need to keep."
        )
        self.category = "3 Results"

    def getParameterInfo(self):
        job_id = arcpy.Parameter(
            displayName="Job ID",
            name="job_id",
            datatype="GPString",
            parameterType="Required",
            direction="Input",
        )
        out_fc = arcpy.Parameter(
            displayName="Detections",
            name="out_features",
            datatype="DEFeatureClass",
            parameterType="Required",
            direction="Output",
        )
        return [job_id, out_fc]

    @guarded
    def execute(self, parameters, messages):
        arcpy.env.overwriteOutput = True

        p = by_name(parameters)
        client = _client()
        job = client.jobs.status(p["job_id"].valueAsText.strip())

        if not job.is_terminal:
            _fail(JOB_NOT_COMPLETED)
        if job.is_failed:
            _fail(f"The job failed. {_describe_job_failure(job)}")

        _load_result(client, job.job_id, job.endpoint_id, p["out_features"])


class ListJobs:
    def __init__(self) -> None:
        self.label = "List Jobs"
        self.description = "List your most recent jobs."
        self.category = "3 Results"

    def getParameterInfo(self):
        status = arcpy.Parameter(
            displayName="Status",
            name="status",
            datatype="GPString",
            parameterType="Optional",
            direction="Input",
        )
        status.filter.type = "ValueList"
        status.filter.list = ["All", "Running", "Completed", "Failed"]
        status.value = "All"

        limit = arcpy.Parameter(
            displayName="Number of jobs",
            name="limit",
            datatype="GPLong",
            parameterType="Optional",
            direction="Input",
        )
        limit.value = 20
        return [status, limit]

    @guarded
    def execute(self, parameters, messages):
        status_map = {
            "Running": ["pending", "processing"],
            "Completed": ["completed"],
            "Failed": ["failed"],
        }
        p = by_name(parameters)
        statuses = status_map.get(p["status"].valueAsText)
        jobs = _client().jobs.list(statuses=statuses, limit=p["limit"].value)

        if not jobs:
            arcpy.AddMessage("No jobs match.")
            return

        arcpy.AddMessage(f"{len(jobs)} job(s)")
        for job in jobs:
            arcpy.AddMessage(
                f"  {job.job_id}  "
                f"{job_summary.format_analysis_label(job.endpoint_id or ''):<32}"
                f"{job.status.value:<12}"
                f"{job_summary.format_submitted_short(job.created_at or '')}"
            )
