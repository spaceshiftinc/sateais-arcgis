"""Unit tests for core.job_summary (pure Python, so these run in CI)."""

from __future__ import annotations

from datetime import datetime, timedelta, timezone

import pytest

from sateais_arcgis.core import job_summary


class TestFormatAnalysisLabel:
    def test_known_types(self):
        # The names match the public documentation headings, so the tool and
        # the docs call each detection the same thing.
        assert job_summary.format_analysis_label("timeseries") == "Time-series change"
        assert (
            job_summary.format_analysis_label("disappearbuilding")
            == "Disappeared building detection"
        )

    def test_unknown_type_falls_back_to_the_raw_value(self):
        # A job listed from the server can carry a type this version has never heard of.
        assert job_summary.format_analysis_label("something-new") == "something-new"

    def test_empty(self):
        assert job_summary.format_analysis_label("") == ""


class TestFormatDetectionSummary:
    def test_singular_and_plural(self):
        assert job_summary.format_detection_summary("ship", 1) == "1 ship"
        assert job_summary.format_detection_summary("ship", 23) == "23 ships"
        assert job_summary.format_detection_summary("timeseries", 7) == "7 changes"

    def test_unknown_type_uses_a_generic_noun(self):
        assert job_summary.format_detection_summary("something-new", 2) == "2 detections"


class TestParseIso8601:
    def test_accepts_the_z_suffix_the_jobs_list_returns(self):
        # datetime.fromisoformat only learned "Z" in 3.11, and ArcGIS Pro 3.0
        # ships Python 3.9 — without this, every listed job's date fails to parse.
        parsed = job_summary.parse_iso8601("2026-07-03T03:39:21.506327Z")
        assert parsed is not None
        assert parsed.tzinfo is not None
        assert parsed.utcoffset() == timedelta(0)

    def test_accepts_an_explicit_offset(self):
        assert job_summary.parse_iso8601("2026-07-30T05:12:00+00:00") is not None

    def test_naive_timestamps_are_treated_as_utc(self):
        parsed = job_summary.parse_iso8601("2026-07-30T05:12:00")
        assert parsed is not None and parsed.utcoffset() == timedelta(0)

    def test_unparseable_input_returns_none(self):
        for value in ("", "not a date", None, 123):
            assert job_summary.parse_iso8601(value) is None


class TestFormatSubmittedAt:
    def test_absolute_and_relative(self):
        now = datetime(2026, 7, 30, 23, 12, tzinfo=timezone.utc)
        rendered = job_summary.format_submitted_at("2026-07-30T05:12:00+00:00", now=now)
        assert rendered.endswith("(18h ago)")

    def test_z_suffixed_timestamps_render(self):
        now = datetime(2026, 7, 30, 23, 12, tzinfo=timezone.utc)
        rendered = job_summary.format_submitted_at("2026-07-30T05:12:00Z", now=now)
        assert rendered.endswith("(18h ago)")
        assert "Z" not in rendered

    def test_naive_timestamps_are_treated_as_utc(self):
        now = datetime(2026, 7, 30, 5, 42, tzinfo=timezone.utc)
        rendered = job_summary.format_submitted_at("2026-07-30T05:12:00", now=now)
        assert rendered.endswith("(30m ago)")

    def test_unparseable_input_is_returned_as_is(self):
        assert job_summary.format_submitted_at("not a timestamp") == "not a timestamp"

    def test_empty(self):
        assert job_summary.format_submitted_at("") == ""

    def test_future_timestamps_read_as_just_now(self):
        now = datetime(2026, 7, 30, 0, 0, tzinfo=timezone.utc)
        rendered = job_summary.format_submitted_at("2026-07-30T05:12:00+00:00", now=now)
        assert rendered.endswith("(just now)")


class TestFormatRelative:
    def test_buckets(self):
        assert job_summary.format_relative(timedelta(seconds=5)) == "5s ago"
        assert job_summary.format_relative(timedelta(minutes=30)) == "30m ago"
        assert job_summary.format_relative(timedelta(hours=18)) == "18h ago"
        assert job_summary.format_relative(timedelta(days=3)) == "3d ago"


class TestDetectionOutcome:
    """A count alone does not say what was counted, so the noun is spelled out."""

    def test_names_what_was_counted(self):
        assert job_summary.format_detection_outcome("newbuilding", 138) == (
            "138 new buildings found"
        )
        assert job_summary.format_detection_outcome("ship", 1) == "1 ship found"

    def test_states_the_empty_case_explicitly(self):
        # "No ships found", not "0": a finished run that found nothing must not
        # look like a result that has not been read yet.
        assert job_summary.format_detection_outcome("ship", 0) == "No ships found"

    def test_groups_thousands(self):
        assert job_summary.format_detection_outcome("ship", 12345) == "12,345 ships found"

    def test_unknown_type_falls_back_to_a_neutral_noun(self):
        assert job_summary.format_detection_outcome("something-new", 2) == "2 detections found"


class TestDetectionCount:
    """The short form that sits beside the type name; the noun is not repeated."""

    def test_states_the_count_with_the_verb_only(self):
        assert job_summary.format_detection_count(138) == "138 found"
        assert job_summary.format_detection_count(1) == "1 found"

    def test_zero_is_a_word_not_a_digit(self):
        # "0" can read as a value not yet filled in.
        assert job_summary.format_detection_count(0) == "None found"

    def test_groups_thousands(self):
        assert job_summary.format_detection_count(12345) == "12,345 found"


class TestVariablePrecisionTimestamps:
    """The server returns fractional seconds of varying length; Python 3.9 cannot read them.

    ``datetime.fromisoformat`` accepts exactly 3 or 6 digits before 3.11, and the
    server drops trailing zeros, so 5 digits arrive routinely. Left unhandled,
    the submitted time shows as the raw ISO string, the run time is blank and
    the job sorts as if it had no date.
    """

    @pytest.mark.parametrize(
        "stamp",
        [
            "2026-09-01T04:22:52.74747Z",  # 5 digits, the common case
            "2026-09-01T04:22:52.7Z",  # 1 digit
            "2026-09-01T04:22:52.74Z",  # 2 digits
            "2026-09-01T04:22:52.747Z",  # 3 digits
            "2026-09-01T04:22:52.7474Z",  # 4 digits
            "2026-09-01T04:22:52.747470Z",  # 6 digits
            "2026-09-01T04:22:52.7474701Z",  # 7 digits, truncated
            "2026-09-01T04:22:52Z",  # none
            "2026-09-01T04:22:52.74747+00:00",  # explicit offset
        ],
    )
    def test_every_precision_parses(self, stamp):
        parsed = job_summary.parse_iso8601(stamp)
        assert parsed is not None, stamp
        assert (parsed.year, parsed.month, parsed.day) == (2026, 9, 1)
        assert (parsed.hour, parsed.minute, parsed.second) == (4, 22, 52)

    def test_the_short_form_shows_a_date_not_the_raw_string(self):
        shown = job_summary.format_submitted_short("2026-09-01T04:22:52.74747Z")
        assert "T" not in shown and "Z" not in shown, shown

    def test_still_returns_none_for_things_that_are_not_timestamps(self):
        for junk in ("", "not a date", "2026-13-45T99:99:99Z", None):
            assert job_summary.parse_iso8601(junk) is None, junk
