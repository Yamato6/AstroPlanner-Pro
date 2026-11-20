# Changelog

All notable changes to AstroPlanner Pro. Format based on [Keep a Changelog](https://keepachangelog.com/);
versions follow [Semantic Versioning](https://semver.org/). Versions up to 1.0.0 are the original
[AstroPlanner](https://github.com/Yamato6/AstroPlanner) (in Spanish), translated here.

## [Unreleased] — 2.0.0

### M2 — Live sky (part 2: 2D charts, camera frame, sky brightness, horizon editor)

#### Added
- **All-sky chart** (key `2`): zenithal equidistant projection, north up, east left (the v1
  convention), with the horizon circle, the minimum altitude and the cardinal points.
- **Finder chart**: gnomonic, centred on the selected object, north up, east left, star names and
  magnitudes, compass, scale bar and center coordinates; drag to pan, scroll to zoom (0.5°–60°).
  *Finder chart* in the Sky panel, the object detail and next to the planner's altitude chart.
- **Export PNG** of the current view (finder charts to print or take to the field).
- **Camera field-of-view overlay** (key `F`): the sensor rectangle at its true angular size (Core
  `OpticsMath`: 2·atan(W/2f)), drawn in every view, following the selection; drag it, rotate it
  with Shift + scroll; sensor presets or typed sizes, focal length and rotation (until equipment
  profiles arrive in M3). Default: the owner's Canon APS-C on the 650 mm telescope.
- **Sky brightness rendering**: Core `SkyBrightness` (SQM ↔ luminance, naked-eye limiting
  magnitude, Bortle for display, a documented twilight and moonlight heuristic) and
  `SkyAppearanceCalculator` sample the zenith brightness like the body tracks; the renderer tints
  the sky (brighter toward the horizon), hides faint stars and deep-sky objects and fades the Milky
  Way. A layer turns it off. The Sky panel shows the zenith brightness now and the site's SQM.
- **Horizon profile editor** in Locations: points by azimuth with a live preview chart, validated
  (range, duplicates), saved with the site; the profile is drawn in the sky as a ground silhouette
  and hides what is behind it.
- **Measured sky brightness (SQM) per site**, typed in Locations (shown with an approximate Bortle
  class); migration `0002_location_sqm` adds it to `app.db`. The atlas fills it in from M3.
- Protocol: `setFov`, `setSkyAppearance`, `fovChanged`; `setViewMode` now switches the projection;
  layer `skyBrightness`. Golden samples regenerated; `fovChanged` is validated on both sides.
- Benchmark switches `--sky-resolution=WxH` and `--sky-mode=dome|allSky|finder` (`SkyLaunchOptions`,
  only well-formed values reach the URL). Results on the development desktop (RX 6600, no vsync):
  ≥ 1,280 fps at 1080p and 1440p in every mode, with 83k and 167k stars; first frame ≤ 0.2 s.
  Not measured on an integrated GPU (see `docs/sky-renderer.md`).

#### Changed
- The keys `1`, `2` and `F` work in the Sky tab, also while the renderer has the focus.
- Locations show the SQM column; the hint no longer promises the editor for later.
### M2 — Live sky (part 1: renderer, bridge and 3D dome)

#### Added
- **Live 3D sky** in the *Sky* tab: a TypeScript + three.js renderer (`src/sky-renderer`) in
  WebView2 with the 83,476 HYG stars (size and color from magnitude and B−V, limit adjustable,
  fainter stars as you zoom), constellation lines, names and boundaries, the Milky Way outline,
  13,371 deep-sky markers scaled and oriented by size and position angle, the Sun, the Moon with
  its phase and bright limb, the planets, alt-az and equatorial grids, ecliptic, meridian,
  ground and cardinal points. Drag to look around, scroll to zoom (1°–120°), hover for names,
  click to select.
- **Sky time**: play at ×1/×60/×600/×3600, pause, step ±1 min/10 min/1 h/1 day, Now. Only the
  renderer animates; pausing or stepping moves the observation time, so Targets recalculates once.
- **Selection both ways**: picking a deep-sky object or planet selects it in Targets and the detail
  (kept in the table even if the filters hide it); selecting in Targets highlights it in the sky;
  *Center on selection*, *Open details*, *Find* (`m31`, `ngc 7000`, `Orion Nebula`, `Jupiter`).
  Session-plan objects are highlighted. Shortcuts: Space, `/`.
- Layers, star limit and the last camera are remembered (`app.db`, key `sky`).
- **Core**: `Matrix3`/`Vector3d`, precession as a matrix (Meeus 21.b), `SkyFrame` (J2000 →
  horizontal matrix reproducing `GetHorizontal`), `MovingBodyCalculator` (Sun, Moon, planets and
  their tracks), Moon phase angle and bright-limb position angle (Meeus 48.a).
- **`AstroPlanner.SkyBridge`**: the WPF ⇄ WebView2 protocol (DTOs, source-generated JSON, strict
  validation of every incoming message) with contract tests and the projection cross-check shared
  with Vitest (≤ 0.01° against Core).
- **DataBuilder `build-sky`**: binary J2000 buffers of stars, deep-sky objects, constellation lines
  and boundaries and the Milky Way, plus label files (`data/build/sky`), deterministic and verified
  in CI.
- Benchmark switches `--sky-benchmark` and `--sky-stars=N`; 1,530 fps (p95 0.9 ms) with 83k stars
  on the development desktop (Radeon RX 6600); see `docs/sky-renderer.md`.
- Docs: `docs/sky-renderer.md` (transform, protocol, security, packaging, performance).

#### Changed
- Without the WebView2 Runtime the Sky tab falls back to the 2D chart of v1, with a notice.
- CI sets up Node (`.nvmrc`), lints, builds and tests the renderer before the .NET build; the
  release job builds it too. Dependabot watches npm.
- The renderer and its data are embedded in the exe (~2.8 MB zip); the single-file exe is ~79 MB.

### M1 — Data foundation & catalogs

#### Added
- **The whole NGC/IC catalog**: 13,371 objects from OpenNGC (v20260501), with all 110 Messier
  and 109 Caldwell objects, sizes, position angles, surface brightness, common and alternate
  names and cross-IDs (M, NGC, IC, Caldwell, PGC, UGC, ESO, LBN, Sh2, Mel, Cl…). Messier objects
  keep their ids and English names; M102 is NGC 5866, as in v1. 83,476 HYG v4.4 stars to
  magnitude 9 are stored for the sky renderer (M2).
- **`tools/AstroPlanner.DataBuilder`**: `download` (pinned URLs, SHA-256 checked), `build-catalogs`
  (deterministic `catalogs.sqlite` + report) and `verify`. Sources and licenses in
  `data/SOURCES.md`, license texts in `THIRD_PARTY_NOTICES.md`, details in `docs/data-pipeline.md`.
- **Targets table**: search by any identifier or name; catalog, minimum size and "Observable
  tonight" filters; new columns *Also known as*, *Size*, *Surface brightness*, *Max. alt.* tonight
  and *Best window* (longest stretch of darkness above the minimum altitude and the horizon).
  Filtering 15k objects takes < 200 ms (test).
- **Night ephemerides** of the whole catalog, in the background and cancellable (< 1 s for 15k
  objects, test), recalculated only when the night, the site, the horizon or the minimum
  altitude change.
- **IAU constellation lookup** (Roman 1987, CDS VI/42): planets now show their real constellation
  instead of "Solar System"; the constellation filter lists the 88 constellations.
- **Air mass and extinction** (Kasten & Young 1989, Δm = k·X) in Core, for the feasibility model.
- **Saved sites** (*Locations* tab): save the site in use, switch, delete; each site has IANA and
  Windows time-zone ids and a horizon profile, already used for visibility and the best window
  (its editor comes in M2). The cities list gains IANA ids; Hermosillo now uses
  America/Hermosillo.
- **Import from AstroPlanner v1** on first run: settings (as a saved site), plan and logbook;
  Spanish planet ids are mapped (Marte → Mars), names re-resolved, pre-0.6 plans understood, v1
  files only read.
- Logbook: **Caldwell progress** next to the Messier marathon.
- Object detail: cross-IDs, size, surface brightness, real constellation and tonight's best window.
- About window: catalog credits.

#### Changed
- **Event times are refined** to about a second (bisection for rise/set/thresholds/twilight,
  golden-section search for culmination) and computed in UTC; checked against JPL Horizons
  fixtures (Sun at three sites, Jupiter, Saturn) within one minute. Before, culmination was the
  highest 10-minute sample.
- **User data moves to SQLite** (`app.db`) with versioned migrations, a backup before migrating
  and damaged files set aside; the M0 JSON files are no longer used.
- Persistence contracts (`ISettingsService`, `IObservationPlanService`, `IObservationLogService`,
  `ILocationRepository`) move to Core; Data implements them.
- Objects of unknown magnitude: nebulae and associations always pass the magnitude filter; other
  objects without a magnitude are hidden by it.
- The 2D sky chart draws at most the 300 brightest objects (plan, selection and planets first)
  and labels 60, until the live sky of M2.
- The single-file `.exe` stays at ~75 MB (the catalog adds ~4.8 MB compressed).

#### Project
- Architecture tests: the DataBuilder may use only Core and Data and nothing may use it; only
  Data talks to SQLite.
- CI verifies the committed catalog database.
- Tests: 364 (Core 128, Data 36, DataBuilder 38, Online 6, UI 156); Core line coverage ~89 %.

### M0 — Bootstrap the new repository

Behaviour is unchanged from AstroPlanner 1.0.0; the app now runs in English on .NET 10.

#### Changed
- **Renamed to AstroPlanner Pro:** `AstroPlannerPro.exe`, new installer AppId, data folder
  `%LocalAppData%\AstroPlannerPro` and single-instance names, so it can be installed next to v1
  (whose data is never touched). Update checks and links point to `Yamato6/AstroPlanner-Pro`.
- **English everywhere:** UI, identifiers, comments, docs. UI strings live in `Resources/Strings.resx`
  (localisable); Messier common names, cities and planet ids are in English. Percentages are written
  `20%` and dates in the plan summary and logbook as `yyyy-MM-dd`.
- **.NET 10 (LTS) and C# 14** — .NET 8 reached end of support on 2026-11-10. Packages updated to the
  latest stable versions and pinned centrally (`Directory.Packages.props`).
- **New layout** (`src/`, `tests/`): new `AstroPlanner.Online` project for HTTP clients (Open-Meteo moves
  there) and one test project per layer.
- **`MainViewModel` split** into `ShellViewModel`, `TonightViewModel`, `TargetsViewModel`,
  `ObjectDetailViewModel`, `SessionPlanViewModel` and `SkyViewModel`, sharing state through the
  `IObserverContext` and `WeatherState` services. The Moon interference rule moves to Core.
- Changing the location in Settings now recalculates (and fetches the forecast) once instead of once
  per field.
- The sunset/sunrise line of the "Dark night" card shows its full text in a tooltip.

#### Added
- Architecture tests for the dependency rules, a cloud-cover wording test, a resource completeness test.
- CI: coverage report and a **85 % line-coverage gate for Core** (currently ~95 %).
- Dependabot (NuGet and GitHub Actions), `.gitattributes`, contributor notes.
- Build settings for every project: warnings as errors, nullable, `AnalysisLevel=latest-recommended`.

## [1.0.0] - 2026-10-23

First stable release. Polishes what existed instead of adding new features.

### Installation
- **Windows installer** (Inno Setup): per-user install without administrator rights; Start menu entry,
  optional desktop shortcut and uninstaller; future versions install on top; uninstalling keeps your data.
- **Portable zip**, now with the license and the README.
- **SHA-256 checksums** (`SHA256SUMS.txt`) for every release.
- The executable is **`AstroPlanner.exe`**, has its own icon and weighs **~76 MB instead of ~175 MB**.

### Look
- New **icon** and **dark title bar** in every window.
- **Unified theme:** Settings and Log observation use the same dark controls as the main window.
- Scroll bars, chart legend, check boxes and buttons match the theme.
- Long names in tables end with "…" and can be read in full on hover.
- Sharp rendering on screens with different scaling (per monitor).

### Spanish texts
- Object types are shown in Spanish in the table, detail, filters and CSV.
- The constellation filter has an **"All"** option.

### Added
- **About** window (F1 or "?"): version, credits, license and a button to open the data folder.
- **Keyboard shortcuts:** Ctrl+F search; F5 current time; Ctrl+, settings; Ctrl+P print the plan;
  Ctrl+E export the plan.
- The window **remembers its size and position**; if it ended up off-screen (e.g. a monitor was
  disconnected), it is centered.
- **Single instance:** opening the app again brings the existing window to the front.
- Open-Meteo attribution next to the forecast, as its license requires.

### Fixed
- Logging an observation of a planet no longer repeats the name in the title.
- The update notice understands release candidates (`1.0.0-rc.1`); users of a stable version only get
  notices about stable versions.
- "Showing N objects" is readable on the dark background.

### Project
- **MIT** license; the repository becomes public.
- README rewritten and this CHANGELOG added.
- CI builds with warnings as errors, uses Node 24 actions and builds the installer.

## [0.8.0] - 2026-10-23

### Added
- **Cloud forecast** (Open-Meteo): "Clouds tonight" card with the dark-night average and the clearest
  hour; grey hourly bands in the chart; clouds at culmination in the detail and in My plan.
- **New version notice**, checking the GitHub releases.
- **"Internet connection"** section in Settings to turn both off. The location is sent rounded to ~1 km.

## [0.7.0] - 2026-10-23

### Added
- **Planets:** Mercury to Neptune in the table, sky chart, My plan and search, with magnitude, distance,
  apparent size, phase and distance from the Sun.
- **Observation logbook:** date, place, equipment, seeing, transparency, rating and notes; **Messier
  marathon** progress; CSV export; ✓ on objects already observed.

## [0.6.0] - 2026-10-23

### Added
- **Search** by id, name or constellation (accent-insensitive) and a **maximum magnitude filter**.
- **Live mode**, refreshing everything every minute. "Now" uses the observer's local time.
- **My plan:** ordered by culmination time; notes per object; CSV export; print or save as PDF.
- **Sky chart** at the chosen time; click an object to select it.

## [0.5.0] - 2026-10-22

Also includes the changes of 0.4, which was never released on its own.

### Added
- **Real night:** from sunset to sunrise, with the dark night (astronomical twilight).
- **Moon:** phase, illumination and altitude; distance to every object; warning when its glare interferes.
- **More precision:** precession, refraction and, for the Moon, parallax.
- "Dark night" and "Moon" cards; day/twilight warning at the chosen time.
- In the chart: shaded twilight, the Moon's curve and the chosen time.

### Robustness (0.4)
- Unexpected errors no longer close the app: a warning is shown and a log is written.
- Input validation with warnings instead of emptying the table.
- Catalogs are embedded in the `.exe`: the `SeedData` folder is no longer needed.
- Continuous integration on GitHub Actions.

## [0.3] - 2026-10-22

### Added
- **Object detail** tab (rise, threshold, culmination and set) and **My plan**, saved between sessions.
- Date and time picker with calendar, lists and ±1 h buttons.
- Dark-themed altitude chart with legend and minimum-altitude line.
- Draggable splitter between table and chart; "Not visible" in red.

### Fixed
- A small-hours time shows the night in progress, not the next one.
- Cancelling the settings discards the changes.
- "Reset" goes back to the configured altitude.
- Selected rows are no longer shown in white.

## [0.2] - 2026-10-18

- First Windows executable: table of visible Messier objects and altitude chart.

[Unreleased]: https://github.com/Yamato6/AstroPlanner-Pro/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Yamato6/AstroPlanner/compare/v0.8.0...v1.0.0
[0.8.0]: https://github.com/Yamato6/AstroPlanner/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/Yamato6/AstroPlanner/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/Yamato6/AstroPlanner/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/Yamato6/AstroPlanner/compare/Pre_0.3...v0.5.0
[0.3]: https://github.com/Yamato6/AstroPlanner/releases/tag/Pre_0.3
[0.2]: https://github.com/Yamato6/AstroPlanner/releases/tag/PreRelease
