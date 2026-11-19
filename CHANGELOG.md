# Changelog

All notable changes to AstroPlanner Pro. Format based on [Keep a Changelog](https://keepachangelog.com/);
versions follow [Semantic Versioning](https://semver.org/). Versions up to 1.0.0 are the original
[AstroPlanner](https://github.com/Yamato6/AstroPlanner) (in Spanish), translated here.

## [Unreleased] — 2.0.0

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
