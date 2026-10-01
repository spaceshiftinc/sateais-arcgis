"""Load detection results into the map, and draw what was covered.

ArcGIS Pro cannot add a GeoJSON file as a layer, so results go through
`JSONToFeatures`. Doing that conversion is part of what this toolbox is for.

Symbology is defined as CIM JSON in code rather than shipped as `.lyrx` files,
so colours are reviewable in a diff.
"""

from __future__ import annotations

import json
import tempfile
from pathlib import Path

import arcpy

from . import job_summary, result_stats
from .geometry import wgs84


def geojson_to_feature_class(
    geojson: dict,
    out_feature_class: str,
    analysis_type: str | None,
) -> tuple[str, int]:
    """Convert result GeoJSON to a feature class.

    Returns ``(path, number of bulk properties dropped)``.

    Bulk chart properties are stripped first; left in, they flatten into huge
    text fields and make the attribute table unusable.

    A result with no features becomes an empty feature class. The run
    succeeded and found nothing, which should look like a result, not an error.
    """
    slim, removed = result_stats.strip_heavy_properties(geojson, analysis_type)

    features = slim.get("features") if isinstance(slim, dict) else None
    if not features:
        _create_empty(out_feature_class)
        return out_feature_class, 0

    # JSONToFeatures only reads from a file.
    tmp = (
        Path(tempfile.gettempdir())
        / f"sateais_{analysis_type}_{abs(hash(out_feature_class))}.geojson"
    )
    tmp.write_text(json.dumps(slim, ensure_ascii=False), encoding="utf-8")
    try:
        arcpy.conversion.JSONToFeatures(
            in_json_file=str(tmp),
            out_features=out_feature_class,
            geometry_type="POLYGON",
        )
    finally:
        tmp.unlink(missing_ok=True)

    return out_feature_class, removed


def _create_empty(out_feature_class: str) -> None:
    arcpy.management.CreateFeatureclass(
        out_path=str(Path(out_feature_class).parent),
        out_name=Path(out_feature_class).name,
        geometry_type="POLYGON",
        spatial_reference=wgs84(),
    )


def aoi_to_feature_class(wkt: str, out_feature_class: str) -> str:
    """Write the requested area as its own layer.

    Without it, an empty area is ambiguous: nothing found, or not analysed?
    """
    geometry = arcpy.FromWKT(wkt, wgs84())
    arcpy.management.CopyFeatures([geometry], out_feature_class)
    return out_feature_class


# Requested / Covered / Not covered. Labels and colours match the QGIS plugin
# so the same situation looks the same in every client.
COVERAGE_REQUESTED = "Requested"
COVERAGE_COVERED = "Covered"
COVERAGE_NOT_COVERED = "Not covered"

#: Field the symbology switches on.
COVERAGE_FIELD = "kind"

#: Below this the gap is rounding noise, not a real hole.
_NEGLIGIBLE_RATIO = 0.01


def coverage_comparison_to_feature_class(
    requested_wkt: str,
    covered_wkt: str | None,
    out_feature_class: str,
) -> tuple[str, dict[str, float]]:
    """Write requested, covered and uncovered areas as one layer.

    One layer keyed on `kind` beats three outputs: a single add to the map, and
    the areas are readable in the attribute table.

    When `covered_wkt` is None the coverage is unknown, so only the requested
    area is drawn. Unknown must not be drawn as fully covered.

    Returns:
        ``(path, {kind: area in km²})``
    """
    sr = wgs84()
    requested = arcpy.FromWKT(requested_wkt, sr)

    # Insertion order is draw order. Requested is an outline only, so it goes
    # last to sit on top of the two filled areas.
    rows: list[tuple[str, arcpy.Geometry]] = []
    if covered_wkt:
        covered = arcpy.FromWKT(covered_wkt, sr)
        rows.append((COVERAGE_COVERED, covered))
        gap = requested.difference(covered)
        if gap and gap.area > requested.area * _NEGLIGIBLE_RATIO:
            rows.append((COVERAGE_NOT_COVERED, gap))
    rows.append((COVERAGE_REQUESTED, requested))

    # Without this ArcGIS appends a number and the old areas pile up in the map.
    reset_output(out_feature_class)

    arcpy.management.CreateFeatureclass(
        out_path=str(Path(out_feature_class).parent),
        out_name=Path(out_feature_class).name,
        geometry_type="POLYGON",
        spatial_reference=sr,
    )
    arcpy.management.AddField(out_feature_class, COVERAGE_FIELD, "TEXT", field_length=20)
    arcpy.management.AddField(out_feature_class, "area_sqkm", "DOUBLE")

    areas: dict[str, float] = {}
    with arcpy.da.InsertCursor(out_feature_class, ["SHAPE@", COVERAGE_FIELD, "area_sqkm"]) as cur:
        for kind, geom in rows:
            km2 = float(geom.getArea("GEODESIC", "SQUAREKILOMETERS"))
            areas[kind] = km2
            cur.insertRow([geom, kind, km2])

    return out_feature_class, areas


def reset_output(out_feature_class: str) -> None:
    """Remove a previous output and any map layer pointing at it.

    A preview answers the current inputs, so the previous one should not linger.
    Removing from the map only applies inside ArcGIS Pro; elsewhere it is skipped.
    """
    try:
        project = arcpy.mp.ArcGISProject("CURRENT")
    except Exception:  # noqa: BLE001
        project = None

    if project is not None:
        target = str(out_feature_class).lower()
        for map_ in project.listMaps():
            for layer in list(map_.listLayers()):
                try:
                    if layer.supports("DATASOURCE") and str(layer.dataSource).lower() == target:
                        map_.removeLayer(layer)
                except Exception:  # noqa: BLE001
                    continue  # broken or unreachable layer; leave it alone

    if arcpy.Exists(out_feature_class):
        arcpy.management.Delete(out_feature_class)


# --- symbology --------------------------------------------------------------

# Per-detection colour and fill opacity (0-255). Same values as the QGIS plugin
# so a result looks the same in every client.
DETECTION_STYLE: dict[str, tuple[tuple[int, int, int], int]] = {
    "ship": ((0, 212, 255), 100),
    "oilslick": ((168, 85, 247), 100),
    "newbuilding": ((16, 185, 129), 90),
    "disappearbuilding": ((239, 68, 68), 90),
    "timeseries": ((245, 158, 11), 90),
}
_DEFAULT_DETECTION_STYLE = ((0, 212, 255), 90)


def _rgb(r: int, g: int, b: int, a: int) -> dict:
    # CIM alpha is 0-100, not 0-255.
    return {"type": "CIMRGBColor", "values": [r, g, b, a]}


def _alpha_percent(alpha_255: int) -> int:
    return round(alpha_255 * 100 / 255)


def _stroke(color: dict, width: float, dashed: bool = False) -> dict:
    layer = {"type": "CIMSolidStroke", "width": width, "color": color}
    if dashed:
        layer["effects"] = [
            {
                "type": "CIMGeometricEffectDashes",
                "dashTemplate": [8, 5],
                "lineDashEnding": "NoConstraint",
            }
        ]
    return layer


def _symbol_reference(symbol: dict) -> dict:
    return {"type": "CIMSymbolReference", "symbol": symbol}


def _renderer_json(renderer: dict) -> str:
    return "JSONRENDERER=" + json.dumps(renderer)


def detection_renderer_json(analysis_type: str | None) -> str:
    """CIM renderer for a detection layer, for `Parameter.symbology`."""
    (r, g, b), fill_alpha = DETECTION_STYLE.get(analysis_type or "", _DEFAULT_DETECTION_STYLE)
    symbol = {
        "type": "CIMPolygonSymbol",
        "symbolLayers": [
            _stroke(_rgb(r, g, b, 100), 1.5),
            {"type": "CIMSolidFill", "color": _rgb(r, g, b, _alpha_percent(fill_alpha))},
        ],
    }
    return _renderer_json({"type": "CIMSimpleRenderer", "symbol": _symbol_reference(symbol)})


def coverage_renderer_json() -> str:
    """CIM renderer keyed on `kind`, for `Parameter.symbology`.

    Colours match the QGIS plugin.
    """
    # Outline only, and darker than the QGIS value so it stays readable on top
    # of the grey hatch.
    requested = {
        "type": "CIMPolygonSymbol",
        "symbolLayers": [_stroke(_rgb(58, 72, 84, 100), 2.5, dashed=True)],
    }

    # Thin, faint stroke so it does not compete with the dashed outline.
    covered = {
        "type": "CIMPolygonSymbol",
        "symbolLayers": [
            _stroke(_rgb(0, 159, 232, 55), 1),
            {"type": "CIMSolidFill", "color": _rgb(0, 159, 232, 22)},
        ],
    }

    # Hatched rather than a flat grey: it reads as "no data" at a glance and
    # survives printing and greyscale.
    not_covered = {
        "type": "CIMPolygonSymbol",
        "symbolLayers": [
            _stroke(_rgb(124, 138, 148, 60), 1),
            {
                "type": "CIMHatchFill",
                "rotation": 45,
                "separation": 5,
                "lineSymbol": {
                    "type": "CIMLineSymbol",
                    "symbolLayers": [_stroke(_rgb(124, 138, 148, 70), 1)],
                },
            },
        ],
    }

    groups = [
        (COVERAGE_REQUESTED, requested),
        (COVERAGE_COVERED, covered),
        (COVERAGE_NOT_COVERED, not_covered),
    ]
    return _renderer_json(
        {
            "type": "CIMUniqueValueRenderer",
            "fields": [COVERAGE_FIELD],
            "groups": [
                {
                    "type": "CIMUniqueValueGroup",
                    "classes": [
                        {
                            "type": "CIMUniqueValueClass",
                            "label": label,
                            "values": [{"type": "CIMUniqueValue", "fieldValues": [label]}],
                            "symbol": _symbol_reference(symbol),
                        }
                        for label, symbol in groups
                    ],
                }
            ],
        }
    )


def describe_result(geojson: dict, analysis_type: str | None) -> str:
    """One line for the run log, e.g. ``138 new buildings found``.

    A time-series result has one feature per grid cell, so the cell count is
    added for scale: ``7 changes found across 2,409 grid cells``.
    """
    count = result_stats.count_detections(geojson, analysis_type)
    text = job_summary.format_detection_outcome(analysis_type or "", count)
    if analysis_type == "timeseries":
        total = len(geojson.get("features", []))
        return f"{text} across {total:,} grid cells"
    return text
