# SateAIs API for ArcGIS Pro

Run SateAIs satellite SAR detection from ArcGIS Pro — ship, oil slick, new
building, disappeared building and time-series change — as ordinary
geoprocessing tools.

Two surfaces share this repository:

- a **Python toolbox** (`sateais_arcgis/`, `.pyt`) whose tools appear in the
  Geoprocessing pane, chain in ModelBuilder, run as a batch over many areas and
  can be called from the Python window — ready to use;
- a **.NET add-in** (`src/`, `.esriAddinX`) with a dockpane that reproduces the
  QGIS plugin's interactive workflow — in development, see
  [docs/PORTING.md](docs/PORTING.md).

## Requirements

- ArcGIS Pro 3.x (Windows)
- A SateAIs API key — create one in the [console](https://console.spcsft.com)
  under **Settings > API Keys**

**No extra Python packages are needed.** ArcGIS Pro's default `arcgispro-py3`
environment is read-only, so the toolbox uses only the standard library and
`arcpy`. You never have to clone the conda environment.

## Install

1. Download the latest zip from
   [Releases](https://github.com/spaceshiftinc/sateais-arcgis/releases) and
   extract it anywhere.
2. In ArcGIS Pro, open the **Catalog** pane, right-click **Toolboxes** >
   **Add Toolbox**, and pick `sateais_arcgis/esri/toolboxes/SateAIs.pyt`.
3. Right-click the toolbox > **Add To New Projects** so it is there next time.

Then run **1 Setup > Configure Account** once to store your API key.

To update, extract the new zip over the old folder and **restart ArcGIS Pro**;
it keeps the previous version's modules loaded until then.

## Tools

| Category | Tool | What it does |
|---|---|---|
| 1 Setup | Configure Account | Store the API key in the OS credential store and check the connection |
| 2 Analysis | Run Detection | Preview the area that will be analysed and the cost; optionally submit the job, wait, and add the result to the map |
| 3 Results | Download Result | Add the detections from a completed job |
| 3 Results | List Jobs | List your recent jobs |

`Run Detection` previews by default and spends no credits. Choose **Submit the
job** to run it. Clear **Wait for the job to finish** to get the job ID back
immediately — that is the mode to use from ModelBuilder or a batch run over
many areas; load the result later with `Download Result`.

## Notes

- The area of interest is a rectangle, entered or drawn as an extent. It is
  shown and sent in longitude/latitude (EPSG:4326) whatever the map's
  coordinate system.
- The toolbox talks to the production API at `https://api.spcsft.com`. There is
  no setting for this.
- Results expire a while after the job completes. Save anything you need to
  keep.
- The API key is resolved in this order: tool parameter, OS credential store,
  `SATEAIS_API_KEY`, then `~/.sateais/credentials` (shared with the `sateais`
  CLI).

## Development

```bash
# Python tests (no ArcGIS Pro required — arcpy is stubbed in tests/conftest.py)
uv run --no-project --with pytest python -m pytest tests -q

# .NET: the Core library and its tests build anywhere; the add-in builds on Windows
dotnet test tests/SateAIs.Core.Tests
dotnet build src/SateAIs.AddIn -c Release   # produces bin/Release/SateAIs.AddIn.esriAddinX

# lint / format, same pin as CI
uv run --no-project --with ruff==0.15.21 ruff check .
uv run --no-project --with ruff==0.15.21 ruff format --check .
```

`sateais_arcgis/core/api/`, `wording.py`, `job_summary.py` and
`result_stats.py` started as copies from
[sateais-qgis](https://github.com/spaceshiftinc/sateais-qgis); the C# in
`src/SateAIs.Core` reproduces them. `contract/wording-cases.json` pins every
user-facing sentence and rounding rule, and both the Python and the C# tests
read it.

**Their behaviour is kept in step; the files are not kept byte-identical.** A
fix to what one of them *does* belongs in both. Comments and docstrings may
differ. `wording.py` matters most here: the same situation has to produce the
same sentence in every SateAIs client, so change its wording in all of them or
in none.

Anything that draws or converts data through `arcpy` can only be exercised on
a licensed ArcGIS Pro machine.

## License

MIT
