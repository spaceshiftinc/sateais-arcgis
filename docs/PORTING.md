# Porting the QGIS plugin to an ArcGIS Pro add-in

The QGIS plugin ([sateais-qgis](https://github.com/spaceshiftinc/sateais-qgis)) is the
**specification**. This add-in reproduces it; it does not reinterpret it.

When the two disagree, the QGIS plugin is right and this one is wrong.

## Why an add-in and not the Python toolbox

The Python toolbox (`sateais_arcgis/esri/toolboxes/SateAIs.pyt`) works and is
verified against the live API, but a geoprocessing tool can only ever be a form
with one Run button. Reaching a result takes more steps than the QGIS dock, and
nothing in the geoprocessing framework can close that gap — no dockpane, no live
preview as you type, no job list, no map overlay that updates with the inputs.

The toolbox is **kept** for what it is genuinely better at: ModelBuilder, batch
runs over many areas, scheduled execution, and the Python window. The QGIS plugin
has no equivalent of those.

So the product has two surfaces:

| Surface | For | Built from |
|---|---|---|
| Dockpane (`.esriAddInX`) | interactive use — the QGIS experience | C# / WPF |
| Python toolbox (`.pyt`) | automation | Python, already built |

## What the dockpane must reproduce

Structure, from `sateais_qgis/gui/`:

```
Dock widget
├── Brand bar            brand.py          SPACESHIFT wordmark + spark + "SateAIs"
└── Tabs
    ├── Analysis         analysis_panel.py
    │   ├── Welcome page (shown until an API key exists)
    │   │   └── "Create a free account" -> console register page
    │   └── Submit page
    │       ├── SetupCard      setup_card.py    3 collapsible steps
    │       │   ① Detection type   5 buttons with icons
    │       │   ② Period           date fields + presets + segmented controls
    │       │   ③ Area             draw on map, or paste WKT
    │       ├── EstimateCard   estimate_card.py coverage + credits, 600 ms debounce
    │       ├── Status + progress
    │       ├── Submit button
    │       └── BalanceFooter  balance_footer.py
    └── Jobs             jobs_panel.py
        ├── Refresh + search box
        ├── JobCard list  job_card.py        one card per job
        └── EmptyState    empty_state.py     starfield + call to action
```

On the map, `coverage_band.py` draws three bands: Requested (dashed outline),
Covered (SateAIs blue), Not covered (grey).

## Design tokens

From `gui/styles.py` (the COSMIC design system). **Use these values; do not pick
new ones.**

| Token | Value | Used for |
|---|---|---|
| Background | `#0B131A` | dock background |
| Surface | `#121A21` | cards |
| Surface raised | `#1A242C` | rows, inputs |
| Border | `#22303A` | card and row borders |
| Text | `#E3EBF1` | body text |
| Text muted | `#8695A2` | subtitles, hints |
| Text dim | `#67757F` / `#62717C` | disabled, secondary |
| Accent | `#009FE8` | SateAIs blue — primary action, Covered band |
| Accent bright | `#5BC2F2` | hover, highlights |
| Success | `#6CC39A` | completed |
| Warning | `#E3AB63` | partial coverage |
| Error | `#E08B80` | failed |

Font stack: `IBM Plex Sans`, then the platform UI font. Base size 13 px.

Detection colours (`core/layer_loader.py`) are the single source for result
symbology and must match:

| Detection | Colour |
|---|---|
| ship | `#00d4ff` |
| oilslick | `#a855f7` |
| newbuilding | `#10b981` |
| disappearbuilding | `#ef4444` |
| timeseries | `#f59e0b` (overridden by the diverging ramp) |

Icons: `gui/icons.py` holds the SVG path data the MCP widget uses. Reuse the same
strings so the icons look the same in every client.

## Logic that must not be rewritten

These modules started as copies from the QGIS plugin. **Their behaviour is kept
in step, not their text** — the add-in must produce the same sentences and the
same rounding, but the files are not byte-identical:

| Module | Responsibility |
|---|---|
| `core/wording.py` | every user-facing sentence about coverage, credits, failures |
| `core/job_summary.py` | dates, labels, search haystack |
| `core/result_stats.py` | detection counts, payload slimming |
| `core/api/` | HTTP, types, errors |

`wording.py` has to agree with the MCP widget's `format.ts`. The add-in is the
third consumer of that contract: change a sentence in all of them, or in none.

**How the C# side reaches this logic — decided.** The add-in is an independent
API client written in C#. It does not call the toolbox: a geoprocessing call
lands in the history and serialises on the GP thread, which is too heavy for a
600 ms debounced preview, and the two surfaces have to version independently.

That makes the add-in another implementation of the wording rules, which is what
`wording.py`'s header warns against. The guard is a shared fixture:
`contract/wording-cases.json` (added together with the add-in) lists inputs and
the expected sentences, and both the Python and the C# tests read it, so the two
cannot drift without a test failing.

## Behaviour that is not visible in screenshots

Carried over from the QGIS plugin's hard-won details:

- Polling every 30 s, give up after 5 consecutive errors or 24 h, and surface why
  (`errors` / `expired` / `gone`)
- 404/410 means the server does not know the job: drop it from the list
- The estimate request is debounced 600 ms and tagged with a generation number so
  a late response cannot overwrite a newer one
- Never show the server's own error text; map the code to a sentence
- `timeseries` feature count is not the detection count — see `result_stats.py`
- Strip `upper_chart` / `lower_chart` before loading a timeseries result
- Results expire; tell the user to save what they need

## Environment

Add-in development needs Windows; the Python toolbox does not.

| | |
|---|---|
| ArcGIS Pro | 3.3 or later (built against the 3.3 SDK on .NET 8; Esri keeps 3.x add-ins loading on later 3.x releases). Verified on 3.7; 3.3–3.6 are expected to work but are not tested |
| .NET | 10 |
| Visual Studio | not required. The .NET SDK (8 or later) plus the `Esri.ArcGISPro.Extensions30` NuGet build and package the add-in; CI does the same on a plain Windows runner |
| Edge WebView2 | required by the Pro SDK |
