"""Pure-Python helpers over result GeoJSON: finding counts and payload slimming."""

from __future__ import annotations

from typing import Any

# A timeseries grid cell only counts as a change once its deviation clears this
# floor. Same value the web result viewer uses to classify a cell, so the plugin
# and the console agree on what "changed" means.
SIGNIFICANT_DEVIATION = 0.05
# Beyond this the change is drawn at the saturated end of the ramp.
STRONG_DEVIATION = 0.50

_DEVIATION_KEY = "deviation"

# Chart series that dominate the payload but mean nothing on a map. Left in,
# they flatten into huge text fields and make the attribute table unusable.
# They are still available from the API and the web viewer.
HEAVY_PROPERTY_KEYS: dict[str, frozenset] = {
    "timeseries": frozenset({"upper_chart", "lower_chart"}),
}


def count_detections(geojson: Any, analysis_type: str) -> int:
    """Count the *findings* in a result, which is not always the feature count.

    A time-series result has one feature per grid cell across the whole area,
    most of which did not change, so the feature count would overstate the
    result by orders of magnitude. Significant cells are counted instead.
    Every other detection emits one feature per detection.
    """
    features = _features(geojson)
    if analysis_type != "timeseries":
        return len(features)

    deviations = [_deviation(feature) for feature in features]
    if not any(value is not None for value in deviations):
        # Unknown or changed schema: fall back to the feature count rather than
        # silently reporting "No detections".
        return len(features)
    return sum(
        1 for value in deviations if value is not None and abs(value) >= SIGNIFICANT_DEVIATION
    )


def strip_heavy_properties(geojson: Any, analysis_type: str) -> tuple[Any, int]:
    """Drop render-irrelevant bulk properties before the GeoJSON becomes features.

    Returns ``(geojson, removed_property_count)``. The input is never mutated, so
    the caller keeps the full payload and only the map copy is slimmed down.

    Uses a denylist rather than a field allowlist on purpose — an allowlist
    would silently discard fields the server adds later.
    """
    heavy = HEAVY_PROPERTY_KEYS.get(analysis_type)
    if not heavy:
        return geojson, 0

    features = _features(geojson)
    if not features:
        return geojson, 0

    slimmed = []
    removed = 0
    for feature in features:
        properties = feature.get("properties") if isinstance(feature, dict) else None
        if not isinstance(properties, dict) or heavy.isdisjoint(properties):
            slimmed.append(feature)
            continue
        kept = {key: value for key, value in properties.items() if key not in heavy}
        removed += len(properties) - len(kept)
        trimmed = dict(feature)
        trimmed["properties"] = kept
        slimmed.append(trimmed)

    if removed == 0:
        return geojson, 0

    result = dict(geojson)
    result["features"] = slimmed
    return result, removed


def _features(geojson: Any) -> list:
    """Extract the feature list from a FeatureCollection, defensively."""
    if isinstance(geojson, dict):
        features = geojson.get("features")
        if isinstance(features, list):
            return features
    return []


def _deviation(feature: Any) -> float | None:
    """Read a feature's numeric ``deviation``, or None when absent/not a number."""
    if not isinstance(feature, dict):
        return None
    properties = feature.get("properties")
    if not isinstance(properties, dict):
        return None
    value = properties.get(_DEVIATION_KEY)
    # bool is an int subclass; a True/False deviation is data corruption, not 1.
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    return float(value)


__all__ = [
    "SIGNIFICANT_DEVIATION",
    "STRONG_DEVIATION",
    "HEAVY_PROPERTY_KEYS",
    "count_detections",
    "strip_heavy_properties",
]
