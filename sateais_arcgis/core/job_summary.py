"""Human-readable summaries of a job: labels, counts and dates.

Pure Python, so it is testable without ArcGIS Pro. The display names live here
rather than in the toolbox so every message agrees on what a detection is called.
"""

from __future__ import annotations

import re
from datetime import datetime, timedelta, timezone

# Display names for the detection types. The same names head the public API
# documentation, so the toolbox and the docs agree.
ANALYSIS_LABELS: dict[str, str] = {
    "ship": "Ship detection",
    "oilslick": "Oil slick detection",
    "newbuilding": "New building detection",
    "disappearbuilding": "Disappeared building detection",
    "timeseries": "Time-series change",
}

# (singular, plural) nouns so a completed job reads "23 ships" / "1 change"
# rather than a bare count.
DETECTION_NOUNS: dict[str, tuple] = {
    "ship": ("ship", "ships"),
    "oilslick": ("oil slick", "oil slicks"),
    "newbuilding": ("new building", "new buildings"),
    "disappearbuilding": ("removed building", "removed buildings"),
    "timeseries": ("change", "changes"),
}


def format_analysis_label(analysis_type: str) -> str:
    """Display name for a detection type, falling back to the raw value.

    A job listed from the server can carry a type this toolbox does not know
    about, and showing that verbatim beats showing nothing.
    """
    if not analysis_type:
        return ""
    return ANALYSIS_LABELS.get(analysis_type, analysis_type)


def format_detection_summary(analysis_type: str, count: int) -> str:
    """Return e.g. ``"23 ships"`` / ``"1 change"`` / ``"12 detections"``."""
    singular, plural = DETECTION_NOUNS.get(analysis_type, ("detection", "detections"))
    return f"{count} {singular if count == 1 else plural}"


def format_detection_outcome(analysis_type: str, count: int) -> str:
    """Return e.g. ``"138 new buildings found"`` / ``"No ships found"``.

    A bare ``138`` beside a name does not say what was counted. Naming the thing
    makes the number self-describing, and stating the empty case explicitly
    ("No ships found") distinguishes a finished run that found nothing from a
    run whose result has not been read yet.
    """
    singular, plural = DETECTION_NOUNS.get(analysis_type, ("detection", "detections"))
    if not count:
        return f"No {plural} found"
    return f"{count:,} {singular if count == 1 else plural} found"


def format_detection_count(count: int) -> str:
    """The count as it sits beside the type name: ``"138 found"`` / ``"None found"``.

    Zero is stated as a word — "0" beside a name reads as a value not yet filled in.
    """
    return f"{count:,} found" if count else "None found"


# Fractional seconds arrive with anywhere from 1 to 7 digits.
_FRACTIONAL_SECONDS_RE = re.compile(r"(?<=:\d\d)\.(\d+)")


def parse_iso8601(value: str) -> datetime | None:
    """Parse an ISO 8601 timestamp, or return None when it cannot be read.

    Two things Python 3.9 (ArcGIS Pro 3.0) cannot do with the timestamps this
    API returns:

    ``Z`` suffix — ``fromisoformat`` only learned it in 3.11, while every
    timestamp from the jobs endpoints ends in one.

    **Variable-precision fractional seconds** — ``fromisoformat`` accepts
    *exactly* 3 or 6 digits before 3.11, but the server drops trailing zeros, so
    5 digits arrive routinely. Padding to 6 digits (truncating anything longer,
    which is below microsecond resolution) makes every precision readable.

    Naive timestamps are treated as UTC, which is what the API emits.
    """
    if not value or not isinstance(value, str):
        return None
    normalised = value.replace("Z", "+00:00")
    match = _FRACTIONAL_SECONDS_RE.search(normalised)
    if match:
        digits = match.group(1)[:6].ljust(6, "0")
        normalised = normalised[: match.start()] + "." + digits + normalised[match.end() :]
    try:
        parsed = datetime.fromisoformat(normalised)
    except ValueError:
        return None
    return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)


def format_submitted_short(iso_ts: str) -> str:
    """Just ``2026-07-30 14:12`` in local time."""
    if not iso_ts:
        return ""
    ts = parse_iso8601(iso_ts)
    if ts is None:
        return iso_ts
    return ts.astimezone().strftime("%Y-%m-%d %H:%M")


def format_submitted_at(iso_ts: str, now: datetime | None = None) -> str:
    """Render a timestamp as ``2026-07-30 14:12 (18h ago)`` in local time.

    ``now`` is injectable so the relative part is testable.
    """
    if not iso_ts:
        return ""
    ts = parse_iso8601(iso_ts)
    if ts is None:
        return iso_ts
    reference = now or datetime.now(timezone.utc)
    absolute = ts.astimezone().strftime("%Y-%m-%d %H:%M")
    return f"{absolute} ({format_relative(reference - ts)})"


def format_relative(delta: timedelta) -> str:
    """Coarse "N ago" wording; anything in the future reads as ``just now``."""
    seconds = int(delta.total_seconds())
    if seconds < 0:
        return "just now"
    if seconds < 60:
        return f"{seconds}s ago"
    minutes = seconds // 60
    if minutes < 60:
        return f"{minutes}m ago"
    hours = minutes // 60
    if hours < 24:
        return f"{hours}h ago"
    return f"{hours // 24}d ago"


__all__ = [
    "ANALYSIS_LABELS",
    "DETECTION_NOUNS",
    "parse_iso8601",
    "format_analysis_label",
    "format_detection_summary",
    "format_detection_outcome",
    "format_detection_count",
    "format_submitted_at",
    "format_submitted_short",
    "format_relative",
]
