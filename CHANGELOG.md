# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-11-17

First public release.

### Added

- **ArcGIS Pro add-in** (`SateAIs.AddIn.esriAddinX`, ArcGIS Pro 3.3 or later).
  A SateAIs tab on the ribbon opens a panel where you choose a detection,
  draw the area on the map, check the estimated cost and coverage, and submit.
  The Jobs tab follows each job, and a completed result loads as map layers.
- **Python toolbox** (`SateAIs.pyt`, ArcGIS Pro 3.0 or later) for ModelBuilder,
  batch runs and the Python window: Configure Account, Run Detection,
  Download Result and List Jobs.
- Detections: ship, oil slick, new building, disappeared building and
  time-series change.
- The API key is kept in the Windows Credential Manager. The `SATEAIS_API_KEY`
  environment variable and the `sateais` CLI's credentials file also work.

[0.1.0]: https://github.com/spaceshiftinc/sateais-arcgis/releases/tag/v0.1.0
