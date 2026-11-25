# Changelog

All notable changes to AstroPlanner Pro. Format based on [Keep a Changelog](https://keepachangelog.com/);
versions follow [Semantic Versioning](https://semver.org/). Versions up to 1.0.0 are the original
[AstroPlanner](https://github.com/Yamato6/AstroPlanner) (in Spanish), translated here.

## [Unreleased]

## [3.0.0-alpha.1] — 2026-11-24

First preview of the v3 planetarium UI. The classic window is still the default and works as in
2.0; `AstroPlannerPro.exe --ui=web` opens the new interface, where the sky fills the window and
everything floats over it. The planner's windows (Tonight, Targets, My plan…) arrive in later
previews; until then they say so and the classic window has them.

### Changed
- The sky renderer moved to an npm workspace, `src/web`: `packages/sky-engine` (the three.js
  renderer, unchanged, with its tests) and `apps/planetarium` (the pages). The Sky tab looks and
  behaves as before.
- The view models, shared state services, exports and UI strings moved to a new project,
  `AstroPlanner.Application` (no WPF: Core and SkyBridge only), with their tests in
  `AstroPlanner.Application.Tests` (CI requires ≥ 80 % line coverage). The WPF window uses them
  as before; the web UI will use the same ones.
- The view-model tests run their async continuations on one thread, as the app does; this fixes
  an intermittent failure where two background completions updated Tonight's alerts at once.

### Added
- `--ui=web` opens a preview of the v3 planetarium UI: the live sky filling the window
  (React 19). It is in development; the classic window stays the default.
- The sky page reports any Content Security Policy violation to the log.
- Bridge v2 for the planetarium UI: an app channel of requests, responses and events next to the
  unchanged sky protocol, with timeouts, cancellation, size limits and strict validation on both
  sides (`docs/bridge.md`). Its contracts are C# records; `tools/AstroPlanner.BridgeGen` generates
  the zod schemas and TypeScript types, and a test fails while they are stale. First endpoints:
  `app.info` and `theme.changed` (the page follows the night-vision theme).
- Endpoint modules for the web UI (`Application/Bridge`): each area answers `area.get` with a
  snapshot and a revision and announces changes with one coalesced `area.changed` event. First
  area: `observer.get` / `observer.changed` (site, time, night, Moon, sky quality).
- Planetarium UI (`--ui=web`), first shell pieces (`docs/ui.md`): a top bar over the sky with the
  site, tonight's astronomical night and the sky time (live while time plays), and the keyboard:
  `Space` play/pause, `F5` now, `J`/`L` slower/faster, `1`/`2`/`3` view modes, `C`/`B`/`N`
  constellations, `G`/`E` grids, `H` hides the interface. The page follows night vision.
- `sky.*` endpoints: time, layers, view mode and the selection for the web UI (`SkyEndpoints`).
- `packages/ui-kit`: design tokens (v2's dark and night palettes), Inter, accessible components
  on React Aria, Lucide icons.
- Playwright E2E tests of the planetarium page in Edge against a mock host that answers from the
  C# golden files, with axe (WCAG 2.2 AA) and CSP checks; they run in CI.
- Planetarium UI: floating windows over the sky. They move by the title bar, resize by any edge,
  stick to edges and to each other, snap to the left or right half (a side sheet) or maximise
  (leaving 24 px of sky) when dragged to an edge, minimise to the rail, and work from the
  keyboard (arrows move, Shift+arrows resize, Ctrl+arrows dock or maximise, `Esc`/`Ctrl+W`
  close, `Ctrl+Tab` cycles). The layout is kept in `app.db` (migration `0006_window_layouts`)
  and restored at start; Settings can reset it.
- Planetarium UI: the left rail (one button per window), Settings (night vision, reduce
  transparency, full screen, reset layout) and Help (keyboard map and About: version, credits,
  data folder, license). Windows not moved yet say so and point to the classic window.
- Planetarium UI: night vision from the top bar or `Ctrl+Shift+N` (saved, shared with the
  classic window), reduce transparency (opaque panels), full screen with `F11`, `F1` Help and
  `Ctrl+,` Settings.
- Planetarium UI: the dock. Its darkness bar shows tonight from an hour before sunset to an hour
  after sunrise (daylight, civil, nautical and astronomical twilight, night, the Moon above the
  horizon, cloud cover with a forecast) and is the time scrubber. Next to it: steps of 10 min
  (Shift 1 h, Ctrl 1 day, Alt 1 min), play/pause, the speed of time, Now, the date and time, the
  layers and the view mode.
- Planetarium UI: the selected object's card (type, constellation, magnitude, altitude and
  azimuth, transit, best window, coordinates; the feasibility verdict, score and recommended
  settings, and "Why?" with the reasons) with Center, Frame with the camera and Add to/Remove
  from my plan; `Esc` clears the selection.
- Planetarium UI: the HUD (field of view, RA/Dec and Alt/Az under the pointer, zenith SQM, star
  limit), a right-click menu on the sky (center, frame, finder chart, add to plan, copy
  coordinates), the command palette (`Ctrl+K` or `/`: catalog objects, a name searched online
  with Sesame, windows and every command with its key), notices at the bottom right, and
  toolbars that fade after 3 s without input (Settings can keep them).
- Endpoints for them (`docs/bridge.md`): `sky.setTime`, `night.get` / `night.changed`,
  `selection.*` / `selection.changed`, `search.*`, `ui.setAutoHide`.

## [2.0.1] — 2026-11-22

### Fixed
- The exe now contains the 3D sky. In 2.0.0 the release build never built the renderer
  (MSBuild checked for its output before building it, and only a clean checkout lacks it), so
  the Sky tab fell back to the 2D chart. Release builds now fail if the renderer is missing
  (`-p:RequireSkyRenderer=true`).

## [2.0.0] — 2026-11-21

AstroPlanner Pro: the v1 visual-observing planner rebuilt as an **astrophotography planner**. It
answers "what can I photograph tonight, from here, with my gear — and how?".

### Highlights
- **Capture feasibility** for every object with your rig: an explainable verdict and score (sky
  brightness from the light-pollution atlas, Moon, clouds, framing, sub length, sampling, SNR),
  and the suggested subs, ISO/gain and best window.
- **Session planner**: your targets scheduled across the night, with a timeline, locks,
  priorities and exports to PDF, CSV and calendar (`.ics`).
- **Live 3D/2D sky** (three.js in WebView2): stars to mag 9, deep-sky objects, planets, comets,
  asteroids and bright satellites, your camera frame and your site's horizon.
- **Catalogs**: the whole NGC/IC (13,000+ objects), Messier and Caldwell, 8,920 stars to mag 6.5
  and 2,975 double-star pairs; comets, asteroids and satellites updated online.
- **Online, optional and private**: forecasts (Open-Meteo, 7Timer!), survey images for framing,
  Wikipedia descriptions, name search — each with a toggle, an offline cache, and only rounded
  coordinates sent.
- **Logbook** with photographed sessions and *Predictions vs results*; **night-vision** theme;
  accessible to screen readers.

### Upgrading from AstroPlanner v1
AstroPlanner Pro is a separate app: it installs side by side with v1 and keeps its data in
`%LocalAppData%\AstroPlannerPro`. On first start it offers to import v1's settings, plan and
logbook (v1's files are only read). Uninstalling either app keeps the other's data.

### Requirements
Windows 10 (1809) or 11, x64. The 3D sky needs the Microsoft Edge WebView2 Runtime (built into
Windows 11); without it the sky uses 2D charts. The release is not code-signed yet, so SmartScreen
may ask for confirmation.

### M6 — Polish and release (part 3: release)

#### Added
- The installer checks for the WebView2 Runtime and, when it is missing, offers to open
  Microsoft's download page (it never downloads anything itself). It also installs
  `THIRD_PARTY_NOTICES.md`; it builds with Inno Setup 6 or 7.
- Release notes come from this CHANGELOG (`tools/release-notes.ps1`); the portable zip is checked
  for the exe, LICENSE, README and the third-party notices.
- Optional code signing through SignPath Foundation, skipped until it is configured
  ([docs/signing.md](docs/signing.md)).
- [docs/architecture.md](docs/architecture.md), [docs/external-services.md](docs/external-services.md),
  [CONTRIBUTING.md](CONTRIBUTING.md); a demo GIF and download instructions in the README.

### M6 — Polish and release (part 2: polish)

#### Added
- **Night-vision theme**: red on black for every window, the charts, the 3D sky (each pixel's
  luminance drawn in red only) and the title bar on Windows 11. Toggle in the top bar, in
  Settings › Display or with `Ctrl+Shift+N`; the choice is saved.
- **Photographed logbook entries**: kind (observed / photographed), rig, sub length, ISO, gain,
  number of subs and the image file (only its path is kept). Logging a target with a plan block
  starts as photographed with the block's subs; otherwise the model's recommendation is ready.
  Tonight's verdict, score and stack SNR are kept with the entry. The CSV export has the new
  columns, and the Messier and Caldwell progress count photographed objects separately.
- **Predictions vs results** in the Logbook: the model's score against your rating for each
  photographed entry (scatter chart), the mean difference per verdict and the correlation.
- **Settings › Advanced**: the feasibility model's tunable constants (seeing without a forecast,
  core brightening, overhead, longest sub, the SNR thresholds, pixels to split a double), with
  *Reset to defaults*. Only the changed values are stored.
- Startup, catalog load and feasibility times in the log.

#### Changed
- app.db migration `0005_logbook_capture.sql`.
- Every brush is used through `DynamicResource` (`Themes/Night.xaml` overrides `Themes/Dark.xaml`).
- Accessibility: names for every focusable control of every window (sky, settings, logbook entry,
  locations); a test walks the windows and another checks the WCAG AA contrast of both themes. The
  primary blue and the danger red of the dark theme are slightly adjusted to pass it.

### M6 — Polish and release (part 1: star catalog)

#### Added
- **Stars as catalog rows**: the 8,920 HYG stars to magnitude 6.5 join Targets (ids "HIP n",
  else "HD n"), with the HIP/HD/HR, Bayer and Flamsteed designations and searchable names
  ("α CMa", "Alpha Canis Majoris", "β Cyg", "Beta Cygni", "61 Cygni"). Catalog filter "Stars".
  Detail shows the spectral type, B−V, distance in light-years and variability.
- **Double stars** from the Washington Double Star Catalog (USNO, public domain): 2,975 pairs
  matched to 2,445 HYG stars (1,836 of them to mag 6.5, typed Double star). Detail lists each
  pair (separation, position angle, magnitudes, year), and feasibility adds a **Resolution**
  reason: whether the rig splits the main pair (Dawes, two pixels or the seeing).
- **Sky ⇄ Targets for stars**: picking a star in the sky selects its row, and a selected star is
  highlighted in the sky.
- Wikipedia titles for stars ("Castor (star)", "Alpha Geminorum", "61 Cygni").

#### Changed
- `catalogs.sqlite` schema 2: HYG spectral type, distance, variability and multiplicity columns,
  and the new `double_stars` table (the DataBuilder downloads the WDS summary).
- Star names (Greek Bayer letters, superscripts, constellation genitives) live in
  `Core/Sky/StarNames`, shared by the sky data and the catalog.
- Performance tests run on 25k objects (night ephemerides, filters) and 23k (feasibility).

#### Fixed
- A UI test could fail when the Milky Way core alert arrived before its assertion.

### M5 — Dynamic objects (part 2: satellites)

#### Added
- **Satellites** from CelesTrak: the `visual` (~160 brightest) and `stations` groups in OMM/JSON
  (six-digit catalog numbers), cached 12 h on disk and used offline. The client has no retries and
  pauses 2 h after any error, as CelesTrak asks; modules docked to a station count once.
- **SGP4 in Core** (`Core/Satellites/Sgp4`, near-Earth orbits): the verification set of Vallado et
  al. (2006) reproduced to under 1 m; decayed orbits are reported. Two-line element reader for the
  verification set. Deep-space orbits (period ≥ 225 min) are not supported (none in the two groups).
- **Visual passes** (`SatelliteCalculator`): topocentric alt/az, range and range rate, a conical
  Earth shadow, nautical-twilight darkness and McCants' magnitudes (embedded `satellite_magnitudes.dat`).
  ISS passes checked against JPL Horizons' own ISS trajectory (rise/set within 30 s).
- **Sky**: satellites above the horizon move with a fine track (±15 min every 10 s, re-sent as time
  moves), a one-minute trail and a dim dot in the Earth's shadow; a "Satellites" layer; picking a
  satellite names its next pass.
- **Tonight**: "Bright satellite passes tonight" (known magnitude up to 3) and an ISS pass alert.
- **Session plan**: the sunlit satellites crossing the active rig's field during each block
  (`FieldCrossings`, 0.1 s near the field), as a count with times in the plan table, red ticks on
  the timeline and a section in the PDF.
- **Settings → Internet**: a toggle for CelesTrak; the top-bar status covers it.
- `tools/fetch-horizons-fixtures.ps1 -SatellitesOnly` (the ISS fixture).

#### Changed
- Bridge: moving bodies may be `satellite`, with a per-sample `sunlit` array (null for the
  others); layers gain `satellites`.

### M5 — Dynamic objects (part 1: comets, asteroids, Milky Way core)

#### Added
- **Comets and bright asteroids** from the Minor Planet Center: `CometEls.txt` daily and the
  yearly bright-asteroid file (falling back to last year's in early January), cached on disk and
  used offline. Comets enter the catalog when brighter than magnitude 15 for the night; the bright
  asteroids all do. They get rows in Targets (types Comet and Asteroid, the "Solar system" filter),
  night ephemerides, feasibility (comets with a 5′ coma), the sky (comets with an anti-Sun tail,
  asteroids as dots; the 40 brightest up to mag 12) and details (distances, elements and epoch).
- **Orbits for any eccentricity** (`Core/Algorithms/OrbitCalculator`): elliptic, parabolic
  (Barker) and hyperbolic, safeguarded Newton, light time; comet g/k and asteroid H-G magnitudes.
  Golden tests against JPL Horizons (2P/Encke, C/2023 A3, C/2025 A6, (4) Vesta): 4–17″ near the
  elements' epoch, 44″ for Vesta a year later.
- **Milky Way core**: Sgr A* as a wide-field catalog target; `MilkyWayPlanner` (core above 10° in
  astronomical darkness, with the Moon down); a Tonight alert with tonight's window or the next good
  night, and a 12-month planner window with the best nights.
- **Comet of the night** alert in Tonight.
- **Settings → Internet**: a toggle for the MPC elements; the top-bar status covers them.
- Wikipedia titles for comets ("2P/Encke"), asteroids ("4 Vesta") and the core ("Galactic Center").

#### Changed
- Bridge: moving bodies may be `comet` or `asteroid`, up to 128.
- Timing tests get a ×1.5 margin on CI only (`tests/Shared/PerformanceBudget`); the spec's budgets
  still apply locally.

#### Fixed
- The Settings window's status line threw a binding error (`Run.Text` binds two-way by default).

### M4 — Online enrichment (part 2: framing preview, Wikipedia, Sesame)

#### Added
- **Framing preview** in the object detail: the active rig's field and aspect, rotated by a
  camera-angle slider, sampled from HiPS survey tiles (DSS2 colour; Pan-STARRS DR1 colour), with
  the object's catalog outline. Core gains HEALPix NESTED (`Core/Sky/Healpix`, checked against
  astropy-healpix) and the TAN projection and tile composition (`Core/Imaging/FramingPreview`). The
  tile layout was checked against stars in real DSS tiles. Wide fields use the order-3 Allsky
  image.
- **Tiles with mirrors**: CDS alasky first, then ESA ESASky for DSS (alasky's hips2fits and
  tiles have been unavailable since late October 2026); a failing mirror is skipped for 10
  minutes; tiles are cached on disk; at most 4 downloads at once.
- **About the object**: the Wikipedia lead (Messier/NGC/IC/Caldwell titles, planets, common
  names; disambiguation and non-astronomical pages are skipped) with its Commons image, author and
  license, and links. Cached for 30 days, "no article" included.
- **Sesame search**: a sky search with no catalog match resolves the name with CDS Sesame (with
  the CfA and IDIA mirrors) and centres the view on it.
- **Settings → Internet**: toggles for survey images, Wikipedia and Sesame, each saying what it
  sends; the top-bar status covers them too.

#### Changed
- `CachedFetch` takes several mirrors and can cache a "not found"; online errors share
  `OnlineServiceException`.
- The feasibility disclaimer no longer says the forecasts are missing.
- `tests/fixtures/online/README.md`: a corrupted character fixed.

### M4 — Online enrichment (part 1: forecasts, cache and toggles)

#### Added
- **Seeing and transparency** from 7Timer! ASTRO (3-hour steps, 3 days). The forecast seeing
  replaces the fixed 2.5″ of the feasibility model, and the haze over 7Timer!'s clearest class is
  added to the site's extinction. Both appear as reasons in "Why this verdict?" (see
  `docs/feasibility-model.md` §11).
- **Dew and wind**: Open-Meteo now also gives temperature, dew point, wind and gusts. *Tonight*
  has a card with the seeing, transparency, dew margin and gusts, plus warnings for dew (from what
  time) and wind (with a gust limit by mount: camera on a tripod or telescope).
- **Disk cache** of online answers (`%LocalAppData%\AstroPlannerPro\cache`, 200 MB, least
  recently used first out). A fresh answer avoids the request. When a service cannot be reached,
  the last answer is used whatever its age and marked "as of HH:mm, offline".
- **Online status** dot in the top bar (online, offline with saved data, off), with one line per
  service in its tooltip.
- **Settings → Internet**: a toggle for 7Timer!, a line under each service saying what it sends,
  and *Clear cache* with its size.
- One named HttpClient per service with the standard resilience pipeline: 2 retries with backoff,
  a circuit breaker, and 15 s in all.

#### Changed
- An offline answer from the cache is not retried before 5 minutes, like a failure.
- The top bar's location column trims its text instead of overlapping the rig selector.

### M3 — Equipment & feasibility (part 2: session planner)

#### Added
- **Session planner** in *My plan* (spec §4.6). With the active rig, each target gets a block of
  the night from a greedy scheduler (Core `Planning/SessionScheduler`):
  - locked blocks stay where they are;
  - the other targets, in priority order, take the free stretch where they gather the most
    signal-to-noise (moonlight, extinction, altitude, horizon mask and clouds included), up to a
    fair share of the dark time;
  - the blocks then grow into the free time next to them, so no usable dark time is left idle.

  Per block it gives the start and end, the sub length and ISO/gain, the number of subs, the total
  integration and, for equatorial mounts, the meridian-flip time. It also says when a target is not
  possible tonight or has no free time left.
- **Timeline** (ScottPlot): one row per target with its usable window, its block coloured by
  verdict (thick outline when locked) and the meridian flip, over the twilight and cloud bands.
- **Priorities**: ▲ ▼ per row, *Auto-prioritize* (the targets visible for the shortest time first,
  then the most feasible) and **lock** a block to keep it. Locks are saved (migration
  `0004_plan_schedule`) and released when the night changes.
- **Checklist**: batteries, dew heater, card space, polar alignment, focus, flats, darks and bias;
  ticks are saved until *Reset*.
- **Exports**:
  - **PDF** with the night summary, the timeline, the table and the checklist (PDFsharp 6.2.4);
  - **.ics** calendar, one event per block (RFC 5545: UTC times, escaping, folding; only the site
    name, never its coordinates);
  - **CSV** with start, end, subs, sub length, ISO/gain, integration, meridian flip and verdict;
  - the printed plan shows the schedule too.
- Core `FeasibilityCalculator.Profile`: the target sample by sample (altitude, hour angle, usable,
  SNR gain), sharing the geometry of `Evaluate`.

#### Changed
- *My plan* keeps the user's priority order instead of sorting by culmination; the exports and the
  printout list the targets in observing order (by start time).
- PDFsharp is pinned to **6.2.4**, its last MIT release: 6.5.0 and later are AGPL-3.0-only (spec
  §12). Dependabot ignores newer versions. `System.Security.Cryptography.Pkcs` is referenced
  directly at 11.0.0 to replace the vulnerable 8.0.1 that PDFsharp pulls in.

### M3 — Equipment & feasibility (part 1: equipment, sky quality and the feasibility model)

#### Added
- **Equipment** tab: cameras (sensor, resolution, pixel pitch, colour/mono, QE, read noise, dark
  current, ISO/gain), lenses and telescopes (focal length, aperture or f-number, transmission),
  mounts (fixed tripod, star tracker, alt-az, equatorial, guided) and **rigs** (camera + optic +
  mount + reducer/barlow). The derived values are shown live:
  - field of view and image scale;
  - sampling against a 2.5″ seeing;
  - NPF limit and 500 rule, or the mount's tracking limit.

  Migration `0003_equipment`. On first run the owner's gear is seeded (Canon EOS Rebel T7 with a
  Rokinon 16 mm f/2.0 on a fixed tripod, and the StarSense Explorer 130AZ), all editable.
- **Active rig** selector in the top bar. The Sky camera frame follows it.
- **Capture-feasibility model** (spec §8, `docs/feasibility-model.md`). For every target it gives:
  - a verdict (Excellent → Not possible) and a 0–100 score;
  - the reasons, each with its value and impact;
  - recommended settings: sub, ISO/gain, number of subs, total time and best window.

  It accounts for:
  - usable dark time above the minimum altitude and the horizon mask, weighted by the cloud
    forecast;
  - sky background (site SQM brightened toward the horizon, **Krisciunas & Schaefer** moonlight,
    twilight when allowed);
  - framing (too small / small / good / too large, with mosaic panels);
  - the longest sub (NPF with cos δ, mount limits, sky-limited sub);
  - a per-pixel stack signal-to-noise with extinction.

  The whole catalog takes about 70 ms in the background.
- **Targets**: *Verdict* and *Score* columns, a minimum-verdict filter and *Fits my field*.
- **Tonight** tab: the active rig, the site's sky quality and **"Best targets for my gear
  tonight"** (double-click for the reasons, one click to add to the plan).
- **Object detail**: "Why this verdict?" with every factor coloured by impact, and the suggested
  capture.
- **Light-pollution atlas**:
  - `build-lp-grid` turns the 3 GB World Atlas 2015 GeoTIFF (Falchi et al. 2016, CC BY-NC 4.0)
    into a Mexico grid at 30″ and a world grid at 5′, 3.1 MB in all and embedded in the app;
  - the zenith SQM of any site comes from it unless one is typed (Locations shows the atlas
    value);
  - the sky renderer's light dome uses it too;
  - it is credited in the About window and THIRD_PARTY_NOTICES.
- **Settings**: image during nautical twilight, and the extinction coefficient k.
- Atmospheric refraction scaled by the site's pressure (standard atmosphere from its elevation):
  about 20 % less at 1888 m. The renderer receives the scaled model in `setObserver`, and the
  projection golden file now carries the model per site.

#### Changed
- Planets are framed by their disc. Stars (no size) are a point of light and never better than
  *Unlikely*.
- The site's zenith SQM now triggers a recalculation (it feeds the feasibility model).

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

[Unreleased]: https://github.com/Yamato6/AstroPlanner-Pro/compare/v2.0.1...HEAD
[2.0.1]: https://github.com/Yamato6/AstroPlanner-Pro/compare/v2.0.0...v2.0.1
[2.0.0]: https://github.com/Yamato6/AstroPlanner-Pro/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/Yamato6/AstroPlanner/compare/v0.8.0...v1.0.0
[0.8.0]: https://github.com/Yamato6/AstroPlanner/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/Yamato6/AstroPlanner/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/Yamato6/AstroPlanner/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/Yamato6/AstroPlanner/compare/Pre_0.3...v0.5.0
[0.3]: https://github.com/Yamato6/AstroPlanner/releases/tag/Pre_0.3
[0.2]: https://github.com/Yamato6/AstroPlanner/releases/tag/PreRelease
