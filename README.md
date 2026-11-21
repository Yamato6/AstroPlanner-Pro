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
> [AstroPlanner v1](https://github.com/Yamato6/AstroPlanner), whose full history it keeps.
> Milestones M0 (English, .NET 10, new architecture), M1 (data foundation: the full NGC/IC
> catalog, saved sites, refined events, v1 import), M2 (the live 2D/3D sky) and M3 (equipment,
> light-pollution-aware sky quality, the capture-feasibility model and the session planner) are
> done. The rest of the roadmap (online enrichment, comets and satellites) is in the
> [specification](ASTROPLANNER_PRO_SPEC.md).

![Tonight: the active rig (Canon T7 + Rokinon 16 mm on a tripod), the site's sky quality from the light-pollution atlas and the best targets for that gear, each with its verdict, score and suggested capture](docs/screenshots/tonight.png)

![Why this verdict? The feasibility breakdown of the Orion Nebula: sky at the target, signal-to-noise, usable hours after clouds, Moon, framing, sub length and sampling](docs/screenshots/feasibility.png)

![Object detail: the framing preview of M42 with the T7 on the 650 mm telescope, from the DSS colour survey, with the nebula's catalog outline, next to its Wikipedia description and image with credits](docs/screenshots/framing.png)

![The equipment inventory: rigs made of a camera, a lens or telescope and a mount, with the field, image scale, sampling and longest sub derived live](docs/screenshots/equipment.png)

![My plan: six targets scheduled across the night with the T7 + 16 mm, each block on the timeline coloured by verdict over its usable window, with the subs, ISO and integration per target and the session checklist](docs/screenshots/session-plan.png)

![The planner: the NGC/IC catalog with sizes, tonight's maximum altitude and best window, the dark night, Moon, clouds and the altitude of the selected object](docs/screenshots/planner.png)

![The live sky: looking north from Mexico City, with constellations, Messier objects, the Milky Way outline and the frame-rate overlay](docs/screenshots/sky.png)

![The all-sky chart: the whole sky above the horizon, zenith in the middle, north up and east left, with the camera frame on M42](docs/screenshots/sky-allsky.png)

![A finder chart of M42: gnomonic, north up, east left, star magnitudes, the 650 mm camera frame and a scale bar](docs/screenshots/sky-finder.png)

![The 3D sky with the site's horizon profile (trees and buildings) drawn as a silhouette](docs/screenshots/sky-horizon.png)

## What it does today

- **What can I photograph tonight, with my gear?** Every object gets an honest, explainable
  estimate for the active rig: a verdict (Excellent → Not possible), a score and the reasons. They
  cover usable dark time above your horizon, sky brightness at the target (light pollution,
  moonlight with Krisciunas & Schaefer, twilight), framing, the longest sub (NPF rule or your mount)
  and the signal-to-noise of the whole stack. The estimate comes with a suggested capture (subs,
  ISO/gain, total time, best window). The *Tonight* tab ranks the best targets. See
  [how it works](docs/feasibility-model.md).
- **Equipment**: cameras, lenses and telescopes, mounts and rigs, with the field of view, image
  scale, sampling and maximum exposure computed live. The active rig drives the verdicts and the
  camera frame on the sky.
- **Light-pollution aware**: the zenith sky quality of any site comes from the World Atlas 2015
  (Falchi et al. 2016), shipped as a compact offline grid (Mexico at 30″, the world at 5′). An SQM
  reading typed for the site takes precedence.
- 🔭 **13,371 deep-sky objects and the 7 planets**: the whole NGC/IC catalog (OpenNGC), with all
  110 Messier and 109 Caldwell objects, sizes, surface brightness, common names and cross-IDs.
  Search by any identifier (`M31`, `NGC 224`, `C14`, `IC 434`, `Sh2-155`, `PGC 2557`) or name, and
  filter by catalog, type, constellation, magnitude, size and visibility, in milliseconds.
- 🌌 **Tonight for every object**: maximum altitude and the best window in astronomical darkness
  above your minimum altitude and your local horizon, computed for the whole catalog in the
  background.
- 🌙 **The real night**: sunset and sunrise, astronomical darkness, Moon phase and a warning when its glare gets in the way.
- ☁️ **Tonight's conditions**: hourly clouds (Open-Meteo), seeing and transparency (7Timer!) and
  warnings for dew (air close to its dew point) and wind (gusts too strong for your mount). The
  forecast seeing and haze refine the verdicts. Forecasts are kept on disk, so offline you see the
  last one, marked with its time.
- 🖼️ **Framing preview**: the object as your camera would frame it, at the rig's exact field and
  aspect, with a camera-angle slider and the object's outline, drawn from DSS2 or Pan-STARRS survey
  tiles (HiPS). Wide lenses use a single all-sky image; tiles are saved for offline use.
- 📖 **About the object**: the Wikipedia introduction and image, with the image's author and
  license, and links to both pages.
- 🔎 **Find anything**: a sky search that finds nothing in the catalog asks CDS Sesame (SIMBAD,
  NED, VizieR), so "Barnard 33" or "Sh2-240" still centres the view.
- 🌠 **Live sky**: a GPU-rendered sky dome (three.js in WebView2) with 83k stars, constellations,
  the Milky Way, 13k deep-sky objects and the Sun, Moon (with its phase) and planets, all
  positioned by the same tested astronomy core as the tables. Play time at up to ×3600, step by
  minutes to days, find any object, click to select it and open its details.
- 🗺️ **All-sky and finder charts**: the whole sky as on v1's chart (north up, east left), and a
  finder chart of any object (gnomonic, star magnitudes, scale bar) exported as PNG.
- 📷 **Camera frame**: your sensor and focal length drawn on the sky at its true size; drag it,
  rotate it, it follows the selection.
- 🌆 **Sky brightness**: twilight, moonlight and your site's light pollution (SQM) tint the sky
  and hide the faint stars, so you see how washed-out the night will be.
- 📍 **Saved sites**: home, dark sites… each with its IANA and Windows time zone, a horizon
  profile (trees, buildings) edited by azimuth, and its measured sky brightness (SQM).
- 📋 **Session planner**: your targets scheduled across the night with your gear, each in the
  stretch where it gathers the most signal (Moon, clouds and horizon included). For each one you
  get the start and end, the subs, ISO/gain and total integration, and the meridian flip on
  equatorial mounts. It shows a timeline and lets you set priorities and lock blocks. A checklist
  covers batteries, dew heater, focus and calibration frames. Export it as PDF, CSV or `.ics`, or
  print it.
- 📓 **Logbook**: record what you observe and track your Messier marathon and Caldwell progress.
- ↪️ **Import from AstroPlanner v1**: on first run, your v1 settings, plan and logbook are offered
  for import (the v1 files are only read).
- ⏱️ **Live mode**: follows the clock and refreshes everything every minute.

It works offline; the internet is only used for the forecasts and the new-version notice, each of
which can be turned off.

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
| `F5` | Current time | `Ctrl+E` | Export the plan to CSV (PDF and `.ics` in the *My plan* tab) |
| `Ctrl+,` | Settings | `F1` | About |

Your data (settings, sites, plan, logbook) is stored in `%LocalAppData%\AstroPlannerPro\app.db`
(SQLite). AstroPlanner v1's data is only read when you import it, never changed, and both apps
can be installed side by side.

## Privacy

AstroPlanner Pro has no accounts and collects no data. It only connects to:

- **api.open-meteo.com**, for clouds, dew point and wind, sending your location **rounded to
  ~1 km**;
- **www.7timer.info**, for seeing and transparency, also sending your location **rounded to
  ~1 km**;
- **alasky.cds.unistra.fr** and its mirror **skies.esac.esa.int** (ESA ESASky), for the survey
  images of the framing preview, receiving only **sky coordinates** (tile numbers);
- **en.wikipedia.org** and **commons.wikimedia.org**, for descriptions and image licenses,
  receiving the **object's name**;
- **cds.unistra.fr** (or its mirrors vizier.cfa.harvard.edu and vizier.idia.ac.za), only when a
  sky search finds nothing in the catalog, receiving the **name you typed**;
- **api.github.com**, to check for a new version (nothing about you is sent).

Each one can be turned off in **Settings → Internet connection**, where the saved answers (kept in
`%LocalAppData%\AstroPlannerPro\cache` for offline use) can be cleared. The dot in the top bar
shows whether the services are online, offline (showing saved data) or off.

## For developers

```bash
dotnet build AstroPlanner.slnx -warnaserror
dotnet run --project src/AstroPlanner.UI
dotnet test AstroPlanner.slnx          # ~600 tests, no network
cd src/sky-renderer && npm ci && npm test   # renderer: ~150 Vitest tests (the UI build runs npm itself)
dotnet run --project tools/AstroPlanner.DataBuilder -- all   # rebuild the catalogs (downloads the sources)
```

Requires the .NET 10 SDK and Node.js 22.12+ (the UI build compiles the sky renderer; see
[docs/sky-renderer.md](docs/sky-renderer.md)). Every push runs CI (build with warnings as errors, tests, coverage with an
85 % line-coverage gate on Core, a check of the committed catalog database); a `v*` tag publishes the `.exe`, the installer and the checksums to a
draft release. Contributor notes are in the docs.

<details>
<summary><b>Architecture</b></summary>

Clean architecture + strict MVVM. `AstroPlanner.Core` knows nothing about IO, HTTP or the UI; the
rules are enforced by architecture tests.

```
src/
  AstroPlanner.Core/     Domain: models, astronomy algorithms (Meeus, event finder, IAU constellations,
                         air mass), night ephemerides, AstronomyService, interfaces
  AstroPlanner.Data/     SQLite: embedded catalogs.sqlite, app.db (migrations, settings, sites,
                         plan, logbook), v1 importer, cities                           → Core
  AstroPlanner.Online/   HTTP clients (Open-Meteo)                                     → Core
  AstroPlanner.SkyBridge/ WPF ⇄ WebView2 protocol: DTOs, validating codec              → Core
  sky-renderer/          TypeScript + three.js sky (WebView2); draws only, astronomy comes from Core
  AstroPlanner.UI/       WPF app and composition root
    ViewModels/            Shell, Tonight, Targets, Sky, ObjectDetail, SessionPlan, Logbook, Locations, Settings
    Services/              IObserverContext (shared observing state), WeatherState, JSON persistence,
                           CSV/print, logging, update checker, safe links, single instance
    Resources/Strings.resx All user-facing text (localisable)
    Themes/Dark.xaml       Palette and styles (also used by the charts)
tools/
  AstroPlanner.DataBuilder    Builds data/build/catalogs.sqlite and the sky data (docs/data-pipeline.md)
data/                     SOURCES.md, curated/ overlays, build/ generated files (raw/ is git-ignored)
tests/
  AstroPlanner.Core.Tests     Meeus worked examples, JPL Horizons fixtures, Roman (1987) test positions
  AstroPlanner.Data.Tests     Catalog database, app.db, migrations, v1 import
  AstroPlanner.DataBuilder.Tests  Catalog build from small excerpts of the sources
  AstroPlanner.Online.Tests   HTTP clients with stubbed responses
  AstroPlanner.SkyBridge.Tests  Protocol contract and projection cross-check (shared with Vitest)
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
- **Event times:** bracketed every 10 minutes in UTC, then refined by bisection (rise, set,
  thresholds, twilight) and golden-section search (culmination) to about a second. Checked
  against JPL Horizons: sunset, sunrise and twilight in Aguascalientes, Madrid and Oslo, and
  Jupiter and Saturn rise, culmination and set, all **within one minute**.
- **Night ephemerides of the catalog:** fixed objects are precessed once per night and sampled
  every 5 minutes with a shared sidereal time (transit solved analytically); planets go through
  the full model. ~13k objects take well under a second, in the background.
- **Constellations:** the IAU boundaries (Roman 1987, CDS VI/42) after precessing to B1875; they
  agree with OpenNGC for all 13,371 objects. Planets get their real constellation for the date.
- **Air mass and extinction:** Kasten & Young (1989) and Δm = k·X with k = 0.25 mag (used by the
  feasibility model from M3).
- **Precision:** precession J2000 → date (~0.36° in 2026) and standard refraction (~34′ at the
  horizon). No nutation or aberration (< 30″); UT instead of TT (ΔT ≈ 70 s). Elevation is stored but
  not used yet.
- **Moon:** low-precision Astronomical Almanac formulae (~0.3°) with parallax and refraction. It
  interferes when above the horizon, ≥ 25 % lit and less than 30° from the object.
- **Planets:** JPL Keplerian elements (1800-2050) with light-time correction; ~1′ error for the inner
  planets and up to ~10′ for Jupiter and Saturn. Meeus magnitudes (ch. 41) including Saturn's rings (ch. 45).
- **Wall-clock time → UTC:** sites store both the IANA and the Windows time zone id and convert
  with `TimeZoneInfo`; times skipped by daylight saving time move forward one hour.

**Application**
- **Filtering never recalculates:** search and filters only filter; positions are recalculated only
  when the time, the location or the minimum altitude change.
- **Shared state through services:** view models read `IObserverContext` and `WeatherState` instead of
  referencing each other.
- **Live mode with `TimeProvider`:** tests use a manual clock; the timer goes back to the UI thread
  through the captured `SynchronizationContext`.
- **Invalid input:** `ObservableValidator` with `[Range]`; an out-of-range value shows a warning and
  the last valid calculation is kept.
- **Catalog:** `catalogs.sqlite` is built by a deterministic tool, embedded in the `.exe`, copied
  once to the data folder and loaded into memory; search keys are precomputed, so filtering
  ~13k objects takes milliseconds and never recalculates positions.
- **Data:** SQLite `app.db` in `%LocalAppData%\AstroPlannerPro`, with versioned migrations applied
  in a transaction after a backup copy. A damaged file is set aside as `app.corrupt-<date>.db`
  instead of being overwritten.
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
| Data, DataBuilder | Microsoft.Data.Sqlite | 11.0.0 |
| UI | CommunityToolkit.Mvvm | 8.4.2 |
| UI | Microsoft.Extensions.DependencyInjection | 11.0.0 |
| UI | ScottPlot.WPF | 5.1.59 |
| Tests | xunit · xunit.runner.visualstudio | 2.9.3 · 4.0.1 |
| Tests | FluentAssertions | 8.11.0 |
| Tests | Microsoft.NET.Test.Sdk · coverlet.collector | 18.12.0 · 10.1.1 |

</details>

## Credits and license

- Deep-sky catalog: [OpenNGC](https://github.com/mattiaverga/OpenNGC) by Mattia Verga
  ([CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)).
- Stars: [HYG database](https://codeberg.org/astronexus/hyg) v4.4 by David Nash / astronexus.net (CC BY-SA 4.0).
- Constellation boundaries: Roman (1987), CDS VI/42, as distributed with
  [astropy](https://github.com/astropy/astropy) (BSD-3-Clause).
- Light pollution: grids derived from the World Atlas 2015 of artificial night sky brightness, Falchi et al. (2016),
  *Science Advances* 2, e1600377; data: GFZ Data Services, [doi:10.5880/GFZ.1.4.2016.001](https://doi.org/10.5880/GFZ.1.4.2016.001)
  ([CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/)).
- Moonlight: Krisciunas & Schaefer (1991), *PASP* 103, 1033.
- Cloud forecast: [Open-Meteo.com](https://open-meteo.com/) (data under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)).
- Algorithms: Jean Meeus, *Astronomical Algorithms* (2nd ed.), and approximate orbital elements from JPL (NASA).
- Charts: [ScottPlot](https://scottplot.net/).
- Based on [AstroPlanner](https://github.com/Yamato6/AstroPlanner) v1.0.0 by the same author.

Sources, versions and checksums: [data/SOURCES.md](data/SOURCES.md); license texts:
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The code is MIT; the catalogs keep their own
licenses. **AstroPlanner Pro is and will stay free:** the light-pollution atlas it uses
(Falchi et al. 2016) is licensed for non-commercial use only.

© 2026 Yamato6 · [MIT License](LICENSE)
