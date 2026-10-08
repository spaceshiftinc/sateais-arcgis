"""The toolbox and the add-in are released together, so they carry one version."""

from __future__ import annotations

import re
from pathlib import Path

from sateais_arcgis import __version__

ROOT = Path(__file__).resolve().parents[1]


def test_python_and_dotnet_versions_agree():
    props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    declared = re.search(r"<Version>([^<]+)</Version>", props).group(1)
    assert declared == __version__
