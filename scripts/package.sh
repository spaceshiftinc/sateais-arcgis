#!/usr/bin/env bash
# Build the distribution zip. Extract it and add the .pyt from the Catalog pane.
set -euo pipefail

cd "$(dirname "$0")/.."
VERSION=$(python3 -c "import re,pathlib; print(re.search(r'__version__ = \"([^\"]+)\"', pathlib.Path('sateais_arcgis/_version.py').read_text()).group(1))")
OUT="dist/sateais-arcgis-${VERSION}.zip"

rm -rf dist && mkdir -p dist
zip -r "$OUT" sateais_arcgis README.md LICENSE \
  -x '*__pycache__*' '*.pyc' '*.DS_Store' >/dev/null

# Last check that nothing from development slipped into the zip.
if unzip -l "$OUT" | grep -qE '__pycache__|\.pyc|tests/'; then
  echo "ERROR: the zip contains files that should not be shipped" >&2
  unzip -l "$OUT" | grep -E '__pycache__|\.pyc|tests/' >&2
  exit 1
fi

echo "$OUT"
unzip -l "$OUT" | tail -1
