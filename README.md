<p align="center">
  <img src="src/AstroPlanner.UI/Assets/AstroPlanner.png" width="96" alt="AstroPlanner Pro icon">
</p>

<h1 align="center">AstroPlanner Pro</h1>

<p align="center">
  <b>What to photograph tonight, when, with which gear — and how likely it is to work.</b><br>
  An astrophotography planner for Windows.
</p>

<p align="center">
  <a href="https://github.com/Yamato6/AstroPlanner-Pro/actions/workflows/ci.yml"><img src="https://github.com/Yamato6/AstroPlanner-Pro/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT license"></a>
</p>

> **Status: work in progress (v2.0 under development).** AstroPlanner Pro is based on
> [AstroPlanner v1](https://github.com/Yamato6/AstroPlanner), whose full history it keeps. Milestone
> M0 is done: the v1 app now runs in English on .NET 10 with a new architecture. The roadmap
> (live 2D/3D sky, equipment and capture-feasibility model, online enrichment, comets and satellites)
> is in the [specification](ASTROPLANNER_PRO_SPEC.md).

![The planner: visible objects, dark night, Moon, clouds and the altitude of the selected object during the night](docs/screenshots/planner.png)

## What it does today

- 🔭 **110 Messier objects and the 7 planets**: altitude, azimuth and visibility at the time you choose, with search and filters.
- 🌙 **The real night**: sunset and sunrise, astronomical darkness, Moon phase and a warning when its glare gets in the way.
- ☁️ **Hourly clouds**: Open-Meteo forecast and the clearest hour of the night.
- 🗺️ **Sky chart**: what is above the horizon and where, as when looking up.
- 📋 **My plan**: tonight's list in the best observing order, with notes, as CSV or printed.
- 📓 **Logbook**: record what you observe and track your Messier marathon.
- ⏱️ **Live mode**: follows the clock and refreshes everything every minute.

It works offline; the internet is only used for the cloud forecast and the new-version notice, and both
can be turned off.

## Where it is going (v2.0)

AstroPlanner v1 answers "what can I *see* tonight?". AstroPlanner Pro will answer **"what can I
*photograph* tonight, from here, with my equipment — and how should I do it?"**: an equipment
inventory (field of view, image scale, maximum exposure), an explainable capture-feasibility score per
target, light-pollution-aware sky brightness, a live 2D/3D sky rendered with three.js in WebView2, and
survey imagery for framing. See the [specification](ASTROPLANNER_PRO_SPEC.md) and the
[CHANGELOG](CHANGELOG.md).

## Getting started

1. **Settings** → choose your city or enter your coordinates.
2. Choose the date and time, or press **Now**.
3. Select an object to see its curve for the night and, in **Object detail**, add it to **My plan**.

| Shortcut | Action | Shortcut | Action |
|---|---|---|---|
| `Ctrl+F` | Search | `Ctrl+P` | Print the plan |
| `F5` | Current time | `Ctrl+E` | Export the plan to CSV |
| `Ctrl+,` | Settings | `F1` | About |

Your data (settings, plan, logbook) is stored in `%LocalAppData%\AstroPlannerPro`. AstroPlanner v1's
data is left untouched, and both apps can be installed side by side.

## Privacy

AstroPlanner Pro has no accounts and collects no data. It only connects to:

- **open-meteo.com**, for the forecast, sending your location **rounded to ~1 km**;
- **api.github.com**, to check for a new version.

Both can be turned off in **Settings → Internet connection**.

## For developers

```bash
dotnet build AstroPlanner.slnx -warnaserror
dotnet run --project src/AstroPlanner.UI
dotnet test AstroPlanner.slnx          # ~200 tests, no network
```

Requires the .NET 10 SDK. Every push runs CI (build with warnings as errors, tests, coverage with an
85 % line-coverage gate on Core); a `v*` tag publishes the `.exe`, the installer and the checksums to a
draft release. Contributor notes are in the docs.

<details>
<summary><b>Architecture</b></summary>

Clean architecture + strict MVVM. `AstroPlanner.Core` knows nothing about IO, HTTP or the UI; the
rules are enforced by architecture tests.

```
src/
  AstroPlanner.Core/     Domain: models, astronomy algorithms (Meeus), AstronomyService, interfaces
  AstroPlanner.Data/     Embedded catalogs (Messier, cities) and repositories          → Core
  AstroPlanner.Online/   HTTP clients (Open-Meteo)                                     → Core
  AstroPlanner.UI/       WPF app and composition root
    ViewModels/            Shell, Tonight, Targets, Sky, ObjectDetail, SessionPlan, Logbook, Settings
    Services/              IObserverContext (shared observing state), WeatherState, JSON persistence,
                           CSV/print, logging, update checker, safe links, single instance
    Resources/Strings.resx All user-facing text (localisable)
    Themes/Dark.xaml       Palette and styles (also used by the charts)
tests/
  AstroPlanner.Core.Tests     Meeus worked examples and published ephemerides
  AstroPlanner.Data.Tests     Catalogs and repositories
  AstroPlanner.Online.Tests   HTTP clients with stubbed responses
  AstroPlanner.UI.Tests       View models, services and architecture rules
installer/                Inno Setup script
.github/workflows/        CI and releases
```

</details>

<details>
<summary><b>Design decisions</b></summary>

**Astronomy**
- **Observing night:** from sunset to sunrise (Sun's center at −0.833°); the dark night with the Sun
  below −18°. 02:00 belongs to the night that started the day before. Where the Sun does not set,
  18:00 → 06:00 is used.
- **Precision:** precession J2000 → date (~0.36° in 2026) and standard refraction (~34′ at the
  horizon). No nutation or aberration (< 30″); UT instead of TT (ΔT ≈ 70 s). Elevation is stored but
  not used yet.
- **Moon:** low-precision Astronomical Almanac formulae (~0.3°) with parallax and refraction. It
  interferes when above the horizon, ≥ 25 % lit and less than 30° from the object.
- **Planets:** JPL Keplerian elements (1800-2050) with light-time correction; ~1′ error for the inner
  planets and up to ~10′ for Jupiter and Saturn. Meeus magnitudes (ch. 41) including Saturn's rings (ch. 45).
- **Wall-clock time → UTC:** the Windows time zone id is stored and converted with `TimeZoneInfo`;
  times skipped by daylight saving time move forward one hour.

**Application**
- **Filtering never recalculates:** search and filters only filter; positions are recalculated only
  when the time, the location or the minimum altitude change.
- **Shared state through services:** view models read `IObserverContext` and `WeatherState` instead of
  referencing each other.
- **Live mode with `TimeProvider`:** tests use a manual clock; the timer goes back to the UI thread
  through the captured `SynchronizationContext`.
- **Invalid input:** `ObservableValidator` with `[Range]`; an out-of-range value shows a warning and
  the last valid calculation is kept.
- **Data:** JSON in `%LocalAppData%\AstroPlannerPro`, written atomically (temp file + rename). A
  damaged logbook is set aside as `logbook.corrupt-<date>.json` instead of being overwritten.
- **Errors:** unhandled exceptions are logged in `logs\` and reported without closing the app.
- **Single instance:** opening it twice brings the existing window to the front, so two processes never
  write the plan at the same time.
- **Optional internet:** one `HttpClient` (15 s). Forecast cached for 1 h per location, 5 min back-off
  after a failure. Pre-releases are only offered to users of a pre-release or a 0.x version.
- **Links:** only `https` addresses of github.com and open-meteo.com are opened.
- **CSV:** separator and numbers follow the system culture, UTF-8 with BOM, so that Excel opens it right.

**Build**
- **.NET 10 LTS**, C# 14, warnings as errors and `AnalysisLevel=latest-recommended` everywhere;
  package versions pinned in `Directory.Packages.props`.
- **One self-contained, compressed `.exe`** (~76 MB instead of ~175 MB, ~0.2 s slower to start). The
  catalogs are embedded.
- **NU1701 suppressed:** caused by `SkiaSharp.Views.WPF` (a ScottPlot dependency), which only declares
  .NET Framework but works on modern .NET.

</details>

<details>
<summary><b>Dependencies</b></summary>

| Project | Package | Version |
|---|---|---|
| UI | CommunityToolkit.Mvvm | 8.4.2 |
| UI | Microsoft.Extensions.DependencyInjection | 11.0.0 |
| UI | ScottPlot.WPF | 5.1.59 |
| Tests | xunit · xunit.runner.visualstudio | 2.9.3 · 4.0.1 |
| Tests | FluentAssertions | 8.11.0 |
| Tests | Microsoft.NET.Test.Sdk · coverlet.collector | 18.12.0 · 10.1.1 |

</details>

## Credits and license

- Cloud forecast: [Open-Meteo.com](https://open-meteo.com/) (data under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)).
- Algorithms: Jean Meeus, *Astronomical Algorithms* (2nd ed.), and approximate orbital elements from JPL (NASA).
- Charts: [ScottPlot](https://scottplot.net/).
- Based on [AstroPlanner](https://github.com/Yamato6/AstroPlanner) v1.0.0 by the same author.

© 2026 Yamato6 · [MIT License](LICENSE)
