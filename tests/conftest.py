"""Shared test setup.

`arcpy` only exists inside ArcGIS Pro. A minimal stand-in is installed here so
the modules that import it can be loaded and their pure parts tested. Anything
that actually draws or converts data is checked on a licensed machine.
"""

from __future__ import annotations

import sys
import types


def _install_arcpy_stub() -> None:
    try:
        import arcpy  # noqa: F401

        return
    except ImportError:
        pass

    stub = types.ModuleType("arcpy")

    class ExecuteError(Exception):
        pass

    class SpatialReference:
        def __init__(self, code: int = 0) -> None:
            self.factoryCode = code
            self.name = f"EPSG:{code}"

    class Extent:
        def __init__(self, xmin, ymin, xmax, ymax, spatial_reference=None) -> None:
            self.XMin, self.YMin, self.XMax, self.YMax = xmin, ymin, xmax, ymax
            self.spatialReference = spatial_reference

    def no_op(*_args, **_kwargs) -> None:
        return None

    stub.ExecuteError = ExecuteError
    stub.SpatialReference = SpatialReference
    stub.Extent = Extent
    stub.AddMessage = no_op
    stub.AddWarning = no_op
    stub.AddError = no_op
    stub.SetProgressor = no_op
    stub.SetProgressorLabel = no_op
    stub.env = types.SimpleNamespace(isCancelled=False, overwriteOutput=False)
    for name in ("management", "conversion", "da", "mp"):
        setattr(stub, name, types.SimpleNamespace())
    sys.modules["arcpy"] = stub


_install_arcpy_stub()
