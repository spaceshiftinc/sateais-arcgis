# SateAIs API for ArcGIS Pro

Run SateAIs satellite SAR detection from ArcGIS Pro — ship, oil slick, new
building, disappeared building and time-series change — and get the results
back as map layers.

The add-in (`SateAIs.AddIn.esriAddinX`) adds a SateAIs panel to ArcGIS Pro:
choose a detection, draw the area on the map, check the estimated cost,
submit, and load the result from the Jobs tab — the same workflow as the
[QGIS plugin](https://github.com/spaceshiftinc/sateais-qgis).

## Requirements

- ArcGIS Pro 3.3 or later (Windows)
- A SateAIs API key — create one in the [console](https://console.spcsft.com)
  under **Settings > API Keys**

## Install

1. Download `SateAIs.AddIn.esriAddinX` from the
   [latest release](https://github.com/spaceshiftinc/sateais-arcgis/releases/latest).
2. Double-click it. The ArcGIS Add-In Installation Utility opens; choose
   **Install Add-In**. It installs for your user only, so no administrator
   rights are needed.
3. Start ArcGIS Pro. Open the **SateAIs** tab on the ribbon, press
   **Settings**, paste your API key and press **Test Connection**.
4. Press **SateAIs** on the same tab to open the panel.

The add-in is not code-signed, so the installer says the file is not digitally
signed. ArcGIS Pro loads unsigned add-ins with its default settings. If your
organization allows only signed add-ins, ask your administrator.

**To update**, close ArcGIS Pro, double-click the new file and start Pro again.
**To remove it**, open **Project > Add-In Manager**, select SateAIs and delete it.

## Notes

- Results are saved to the project's default geodatabase, so they work with
  the attribute table and the geoprocessing tools like any other layer.
- Results expire a while after the job completes. Save anything you need to
  keep.
- The API key is resolved in this order: the Windows Credential Manager,
  `SATEAIS_API_KEY`, then `~/.sateais/credentials` (shared with the `sateais`
  CLI).

## Development

The repository also contains a Python toolbox (`sateais_arcgis/`, `SateAIs.pyt`).
It is not part of the release.

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
