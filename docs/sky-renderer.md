# Sky renderer

The **Sky** tab is a TypeScript + [three.js](https://threejs.org/) engine (`src/web/packages/sky-engine`)
hosted in WebView2 (spec §4.2, §5.3, §5.4). It draws; it does not compute astronomy. Every
position comes from `AstroPlanner.Core`, so the sky, the tables and the charts always agree.

```
AstroPlanner.Core        SkyFrame (J2000 → horizontal matrix), MovingBodyCalculator (Sun, Moon, planets),
                         SkyAppearanceCalculator + SkyQuality/SkyBrightness, Imaging/OpticsMath (camera field)
AstroPlanner.SkyBridge   protocol DTOs, SkyMessageCodec (encode / decode + validate), SkyBridgeMapper
AstroPlanner.UI          SkyViewModel (state, clock, selection), SkyHost (WebView2 + security), SkyAssets
src/web                  npm workspace: packages/sky-engine (three.js scene, shaders, overlay,
                         picking; speaks only the protocol) and apps/planetarium (the pages)
data/build/sky           static data built by the DataBuilder (`build-sky`)
```

## The transform (§5.3)

Static objects are sent once, as binary buffers of **J2000 unit vectors**. Every frame the
GPU applies one 3×3 matrix built from what C# sends (`frameTransform`):

```
H(t) = L(φ) · Rz(−LST(t)) · P        LST(t) = lstDegrees + lstRateDegreesPerSecond · (t − epochUtc)
```

- `P` — precession J2000 → mean equator of date (Meeus 21, the same angles as `Precession.Precess`).
- `LST` — local mean sidereal time; the renderer only advances it linearly at the rate C# gives
  (the Earth's rotation), so animation needs no messages per frame. C# sends a new frame when the
  sky time moves more than 10 minutes from the last one (precession drifts ≪ 0.01° in that time).
- `L(φ)` — hour-angle coordinates → (north, east, up) for the observer's latitude.
- Refraction is then applied to the altitude in the shader with the Core coefficients
  (Saemundsson, sent in `init` and again in `setObserver`, scaled by the pressure of the site's
  elevation: `RefractionModel.ForElevation`, the same scale as Core `Refraction.PressureScale`).

This is exactly the Core model (`Precession.FromJ2000` + `CoordinateTransformer.ToHorizontal` +
`Refraction`): no nutation or aberration, as everywhere else in the app.

**Cross-check.** `tests/fixtures/bridge/projection.json` holds 80 cases (5 stars × 4 sites
× 2 epochs × 2 instants, the second one 5 h after the frame epoch) with the apparent altitude
and azimuth from Core's `GetHorizontal`. The C# test (`AstroPlanner.SkyBridge.Tests`) and the
Vitest test (`test/projection.test.ts`) both reproduce them **within 0.01°**.

**Moving bodies.** The Sun, the Moon and the planets are sampled by Core every 10 minutes over
±12 h around the sky time; each sample's topocentric position (Moon parallax included, no
refraction) is rotated back with the inverse of `H(t)` into the J2000 frame and sent as a track.
The renderer interpolates linearly, normalizes and applies `H(t)`: positions are exact at the
samples, and the tracks are regenerated when the sky time leaves them. The Moon's phase uses the
phase angle and the position angle of the bright limb (Meeus 48, tested against example 48.a).

## Views (§4.2)

One scene, three projections chosen by a uniform in the vertex shader (`skyClip` in
`render/shaders.ts`, mirrored for the overlay and picking by `render/projection.ts`):

| Mode | Projection | Interaction |
|---|---|---|
| `dome` (key `1`) | zenithal around the view direction, r(θ) = k·tan(θ/k): perspective (k = 1) up to a 60° field, rising smoothly to stereographic (k = 2) at 120° and beyond; ground and cardinal points | drag to look around (a fast drag coasts), scroll = field of view 0.25°–180° |
| `allSky` (key `2`) | zenithal equidistant: r = (90° − alt)/90° · R, north up, **east left** (v1) | click to select |
| `finder` | gnomonic around a J2000 direction, celestial north up, east left (homogeneous w = cos distance, so the GPU clips the far side) | drag to pan, scroll = 0.5°–60° |

**Dome projection (R2a).** A pinhole (perspective) camera cannot open to 180°, so the dome
uses r(θ) = k·tan(θ/k) around the view direction (`domeProject`/`domeInverse` in
`render/projection.ts`, `domeClip` in the shaders). Up to a 60° field k = 1 and it is exactly the
former three.js perspective camera (tested to 0.01 px); from 60° to 120° k rises with a
smoothstep to 2 (stereographic, conformal, finite to 180°). Directions further than
min(k·90°, 180°) − 1.1° from the center are culled (`vCull`: the vertex goes past the far plane
and the fragments of its segment or triangle are discarded); the screen never reaches them.
Segment tables are split into ≤ 2° pieces along their great circles so lines curve with the
projection. The center scale is the same for every k (r'(0) = 1), so point sizes and picking
radii use `domePixelsPerDegree`. Above a 110° field only the brightest star names are drawn and
Messier, comet and asteroid labels are left out (selected and planned objects keep theirs); bright
stars grow up to ×1.5 as the field narrows below 60°.

**Camera motion (R2a, `render/camera.ts`).** `centerOn` flies instead of jumping: the direction
follows the great circle and the field changes on a log scale, with ease-in-out, 300 ms to 1.5 s
by distance; on a long turn the field opens a little in the middle. The target is recomputed every
frame (the sky turns while flying); `viewChanged` is sent when the flight arrives. *Tracking*
(`T`) keeps the selection centred in the dome while time runs (stars and deep-sky objects use
their fixed J2000 vector, moving bodies their track); a manual pan stops it, and so do another
selection, another mode or a lost position (`skyEvents` `tracking`). A fast drag leaves inertia
(decay ≈ 250 ms, off with Settings › Sky › *Inertia when dragging*, `ui.setSkyInertia`). `Home`
flies back to south, 30° up, 70° field, which is also the first-run view. In the v2 Sky tab the
engine handles `T` and `Home` itself; the v3 page drives them through `skyControl`.

The finder is equatorial (no horizon, no refraction, no alt-az lines); it labels named stars and
magnitudes (recomputed only when the chart moves), and draws a compass, a scale bar and the center
coordinates. *Finder chart* (Sky panel, object detail, planner) centres it on the selection with
three times the camera field (10° without the overlay). *Export PNG* saves what WebView2 shows
(`CapturePreviewAsync`). The mode is remembered; `viewChanged` is only sent from the dome.

**Camera field of view** (key `F`). The sensor rectangle is a flat tan(w/2) × tan(h/2) rectangle
in the tangent plane at its center, its top at position angle *rotation* from north through east,
with w = 2·atan(W/2f) from Core `OpticsMath` (spec §8.2). Its edges are sampled so they curve
correctly in the dome and the all-sky chart. It follows the selection; dragging moves it (the sky
point under the pointer stays under it), Shift + scroll rotates it (1°, Ctrl 5°); the renderer
reports the result with `fovChanged`, which C# keeps without echoing it back. Until equipment
profiles arrive (M3) the sensor and focal length are typed in the Sky panel.

**Sky brightness (R2b, spec §8.2).** The sky is the feasibility engine's sky. Core
(`SkyAppearanceCalculator`) samples `SkyBackground.At` at the zenith, like the body tracks (±12 h,
every 10 min): the total, the naked-eye limit NELM = 7.93 − 5·log10(10^(4.316 − SQM/5) + 1), and
its parts — twilight (`SkyBrightness.Twilight`: 4.0 with the Sun ≥ 10°, 7.0 at 0°, 12.0 at −6°,
17.5 at −12°, nothing below −18°) and Krisciunas & Schaefer moonlight — with the site's SQM
(manual, else the atlas, else 22.0), the site's extinction k and the site-glow brightening per
air mass (the feasibility constants, the user's calibration included). `setSkyAppearance`
carries them (an absent part is 99). The renderer only spreads them over the sky with the same
laws (`render/photometry.ts`, mirrored in GLSL; `test/photometry.test.ts` rebuilds
`SkyBackground.At` from the zenith levels within 0.05 mag, `tests/fixtures/bridge/photometry.json`):

- site glow × (1 + a·(X − 1)), X the Kasten & Young air mass capped at 5.6 (≈ 10°);
- moonlight × the K&S direction terms f(ρ)·(1 − 10^(−0.4·k·X(Z))) relative to the zenith, plus a
  small glare halo around the Moon;
- twilight × its *shape*: single scattering of sunlight (Rayleigh + Mie + ozone, `render/atmosphere.ts`,
  after Nishita et al. 1993 and the MIT glsl-atmosphere/three.js `Sky`) evaluated on the GPU into a
  table (view altitude × azimuth from the Sun) whenever the Sun moves 0.05°, normalised to the
  zenith; frozen below a Sun at −6° (the Earth's shadow leaves the zenith unlit) and with 15 % of
  the zenith light added everywhere as a stand-in for multiple scattering.

A full-screen pass (the inverse of the dome or all-sky projection per pixel) turns the total into
the display curve (the luminance of the former colour stops, so a dark sky still looks as before)
coloured by what lights it: navy for the natural sky, the light-pollution tint (sodium, LED or
neutral; Settings › Sky) for the artificial part, blue-grey for moonlight, the scattering colour for
twilight and daylight (a display white balance turns the high sky blue; the horizon keeps its
colours). Stars and deep-sky objects are dimmed by k·X (X ≤ 40) and reddened, and each is limited
by the sky around it: stars to that NELM + 2 (plus the zoom bonus), deep-sky objects by the
magnitudes lost against a dark sky. The HUD shows the limit at the middle of the view less its
extinction; picking, labels and the finder use the zenith limit (the darkest sky). The ground
follows the light at the horizon. Without the media folder the Milky Way is its outline, with its
rule (full at SQM ≥ 21, gone at ≤ 19); with it, the panorama below. The *Sky brightness* layer (key
`A`) turns all of it off: a dark natural sky, no extinction.

**Quality (Settings › Sky › Atmosphere quality).** Low 32×16 table, 8 + 4 integration samples;
Medium (default) 64×32, 16 + 6; High 128×64, 32 + 8, and twinkling stars may be turned on
(stronger toward the horizon). The table is redrawn only when the Sun moves, so every level costs
about the same per frame; Low is for integrated GPUs (not measurable on the reference PC, which has
none). If the p95 frame time stays over 20 ms for 5 s the sky drops one level by itself and the
page says so (spec §7.4). Quality, tint and twinkling are page preferences (`ui.setSkyQuality`,
`ui.setLightPollutionTint`, `ui.setScintillation`) applied through `skyControl`; the v2 Sky tab
uses Medium, LED and no twinkling.

**Horizon profile.** The site's profile (Core `HorizonMask`, edited in Locations) comes with
`setObserver`; the renderer interpolates it exactly like `HorizonMask.AltitudeAt` and draws it as
an opaque silhouette (dome and all-sky) after the sky layers, and hides the labels, bodies and
picks behind it.

**Milky Way panorama and landscapes (R2c, spec §8.3, §8.5).** The media folder next to the exe
(`data/`, from `data/build/media`, built by the DataBuilder's `build-media`:
[data-pipeline.md](data-pipeline.md)) is served as `https://media.astroplanner/` when it exists;
`init.mediaBaseUrl` says where (null without it) and the engine reads its `manifest.json`
(`render/media.ts`, zod) after the first frame. Images are decoded off the main thread
(`createImageBitmap`), uploaded with mipmaps at most 4096 px wide (an 8192 px landscape costs
~280 MB more in WebView2's GPU process, past the 1.2 GB budget) and released from the page.

- *Panorama* — "The Milky Way panorama", ESO/S. Brunier (eso0932a, CC BY 4.0): stars removed,
  background black, reprojected to J2000 equirectangular (u = RA/360°, v = (90° − Dec)/180°). The
  sky pass samples it per pixel (the inverse of `uFrame`, a mipmap level set per frame from the
  pixel scale, so the RA seam never shows) and adds it to the sky's colour, scaled by its
  *visibility*: the natural sky's share of the sky's light at that pixel (1 under a dark sky; a
  few per cent in town, moonlight or twilight), dimmed by the air like the stars, and faded out
  below a 20° field (5° gone). It replaces the outline Milky Way; the *Milky Way* layer turns it
  off. Its credit is in About and in the HUD while it shows (zenith visibility ≥ 10 %, SQM ≈ 19.5).
- *Landscapes* — four 360° photos from Poly Haven (CC0): open field, mountains, desert, edge of
  town. Columns are azimuth 0°–360° from north, rows run from the image's top altitude down to −90°,
  the sky is transparent, and the manifest gives each one's horizon (the ground's altitude per
  degree). A second full-screen pass draws it over the stars and lines (alpha blended), lit by the
  sky at the horizon: the daylight photo by day, a dim bluish, nearly grey shape at night
  (moonlight and the towns' glow raise it). Its horizon cuts the labels and picks, and its outline
  is drawn as the silhouette until the image is loaded. The *Ground* layer (dock, `Shift+G`)
  turns it off.
- *Per site* — `Location.LandscapeId` (migration `0007_location_landscape.sql`): a landscape id,
  `flat` (no landscape, no profile) or null (the site's horizon profile, the default). C# sends it
  in `setObserver.landscape` (`MediaLibrary.Resolve`: an id the folder lacks becomes null).
  Settings › Landscape (`landscape.get` / `landscape.set`, [bridge.md](bridge.md)) chooses it for
  the active saved site; *Use the landscape horizon for calculations* (`landscape.useHorizon`,
  after a confirmation) copies the landscape's horizon into the site's horizon mask — one point
  every 2°, the highest ground of those degrees — so that visibility and planning follow it.
- *Without the folder* the sky works as before: outline Milky Way, horizon profile, no credits.

## Protocol (§5.4)

JSON messages `{"v": 1, "type": "…", "payload": {…}}` over `PostWebMessageAsJson` /
`window.chrome.webview.postMessage`. C# types: `src/AstroPlanner.SkyBridge/Payloads.cs`;
TypeScript (zod) schemas: `src/web/packages/sky-engine/src/bridge/schema.ts`. The v3 UI's app
channel (`v: 2`: requests, responses, events) shares the pipe; the engine ignores it
([bridge.md](bridge.md)).

| C# → renderer | |
|---|---|
| `init` | data URL, refraction model, layers, view mode, theme, stats overlay; the media folder's URL or null (R2c) |
| `setObserver` | site, minimum altitude, horizon mask, refraction model scaled for the site (M3); the site's landscape: an id, `flat` or null (R2c) |
| `setTime` / `setTimeRate` | sky time and rate (0 = paused, 1 = real time, 3600 = 1 h/s) |
| `frameTransform` | the matrix parameters above |
| `movingBodies` | Sun, Moon, planets, comets, asteroids (M5a) and satellites (M5b): magnitude, size, phase, bright limb, track; satellites carry their own short track (±15 min every 10 s) and a `sunlit` flag per sample (null for the others) |
| `setLayers` | stars, constellation lines/names/boundaries, Milky Way, deep sky, bodies, satellites, labels, grids, ecliptic, meridian, ground; star and DSO limits |
| `setHighlights` | ids of the session plan |
| `selectObject` | selection (also renderer → C#) |
| `centerOn` | turn the view to a J2000 direction (optionally change the FOV) |
| `setViewMode`, `setTheme` | dome / all-sky / finder; dark / night (an SVG colour matrix in `index.html` keeps each pixel's luminance in the red channel only) |
| `setFov` | camera frame: visible, width and height (degrees), rotation, center (J2000, or null = middle of the view), label |
| `setSkyAppearance` | sampled zenith brightness, limiting magnitude, twilight and moonlight (start, step, four arrays), the site's SQM, extinction and brightening per air mass |

| Renderer → C# | |
|---|---|
| `ready` | renderer version, WebGL version; C# then sends the whole state |
| `firstFrame` | ms from navigation start to the first frame (logged) |
| `selectObject`, `hover` | picks; DSO and planet picks select the object in Targets (kept in the table even if filtered out) |
| `viewChanged` | dome camera, saved so that the next session opens where you left it |
| `fovChanged` | the camera frame was dragged or rotated: center (unit vector) and rotation in [0°, 360°) |
| `stats` | fps, p95 frame time, size, star count (overlay, benchmark) |
| `shortcut` | Space, `1`, `2`, `F`, `/` pressed while the renderer has the focus |
| `error` | uncaught renderer errors (logged) |

**Contract tests.** `tests/fixtures/bridge/outgoing/*.json` (one golden sample per C# message,
generated by the C# tests — regenerate with `UPDATE_BRIDGE_FIXTURES=1 dotnet test`) must pass the
zod schemas; `incoming/*.json` must be accepted and `invalid/*.json` rejected by **both** the C#
decoder and zod. Change a message on both sides and regenerate the samples in the same commit.

## Security

- The renderer is loaded from `https://app.astroplanner/`, its data from
  `https://data.astroplanner/` and, when the folder exists, the sky's images from
  `https://media.astroplanner/` (the read-only `data\` next to the exe)
  (`SetVirtualHostNameToFolderMapping`); nothing else can load:
  `NavigationStarting` / `FrameNavigationStarting` cancel any other URL and `NewWindowRequested`
  is always handled (links on the `SafeLinks` allow-list open in the browser).
- Strict CSP in `index.html`: `default-src 'none'; script-src 'self'; connect-src 'self'
  https://data.astroplanner https://media.astroplanner; img-src 'self' data:
  https://media.astroplanner` — no inline or remote scripts, no `eval` (ESLint forbids it; zod runs
  in `jitless` mode so it never probes `Function`). The media manifest is validated (zod) and its
  file names are limited to `folder/name.webp` (letters, digits, hyphens), so it cannot point
  outside the folder.
- Dev tools (Release), context menus, script dialogs, browser shortcuts, zoom, autofill, password
  saving and host objects are disabled.
- Messages are accepted only from the app origin, are size-limited (16 KB) and validated
  field by field (`SkyMessageCodec.Decode`: unknown types, extra or missing fields, out-of-range
  numbers → dropped). The renderer validates what it receives and what it sends with zod.

**Security review (M2b).** What M2b adds to the attack surface, and how it is held:

- `fovChanged` is the only new message the renderer sends: zod checks it before sending, and
  `SkyMessageCodec.Decode` rejects anything but a finite unit vector (±0.001) and a rotation in
  [0°, 360°) (golden `invalid/fov-*.json` on both sides).
- The new page parameters (`res`, `mode`) come only from the command line, through
  `SkyLaunchOptions`, which lets through known switches with well-formed values only (tested);
  the renderer parses them again (`WxH` up to 7680×4320, three mode names).
- *Export PNG* writes only to the path the user picks in the save dialog; nothing is read back.
- No new hosts, scripts, fonts or remote resources: the CSP, the navigation filter
  (`SkyHost.IsAllowedNavigation`, tested for other hosts, schemes, ports, `file:` and
  `javascript:`) and the `NewWindowRequested` handler (always handled; only `SafeLinks` URLs
  open, in the browser) are unchanged.

## Static data (`build-sky`)

`dotnet run --project tools/AstroPlanner.DataBuilder -- build-sky` reads `catalogs.sqlite` and
the d3-celestial GeoJSON and writes (deterministically) into `data/build/sky/`:

| File | Content |
|---|---|
| `stars.bin` | 83,476 HYG stars ≤ mag 9, sorted by magnitude: x, y, z, mag, B−V (float32), HYG id, HIP (uint32) — 28 B each |
| `star-names.json` | proper names, Bayer (α CMa) and Flamsteed (61 Cyg) labels by HYG id |
| `dsos.bin` / `dsos.json` | 13,371 OpenNGC objects: x, y, z, mag, axes, PA, type; ids and names in the same order |
| `constellation-lines.bin`, `constellation-bounds.bin`, `milkyway.bin` | segments (x1…z2, group); boundaries and Milky Way split into ≤ 2° great-circle pieces |
| `constellation-labels.json` | 88 constellations (Serpens twice), names and label positions |

Binary files start with a 16-byte header (`APSB`, format version, count, record size). Proper
motion is ignored (J2000 epoch: < 1′ for all but a few stars).

## Packaging

The UI build runs `npm ci` (first time) and `npm run build` in `src/web` when the web sources
change, then zips `src/web/apps/planetarium/dist` + the sky data into `obj/sky-assets.zip`, embedded in the exe
(~2.8 MB; the single-file exe grew from ~75 to ~79 MB). At run time `SkyAssets` extracts it
once to `%LocalAppData%\AstroPlannerPro\sky\<hash>\{app,data}` and removes older copies, like the
catalog database. Building without Node: `-p:SkipSkyRendererBuild=true` (the Sky tab then shows
the 2D chart unless a previous `dist/` exists).

The sky's images are not in the exe: `data/build/media` is copied next to it as `data\` (output,
publish, the installer's `{app}\data`, the portable zip; ~5.5 MB), outside the single file, and the
app works without it. `ReleaseTests` and the release job check it is there.

WebView2 starts the first time the Sky tab is opened. Without the WebView2 Runtime (some
Windows 10 machines) the tab falls back to the 2D chart of v1 with a notice; the installer
checks for the runtime in M6.

## Performance

The renderer draws stars as one `Points` object and lines as `LineSegments`, all positioned by
the vertex shader, so the CPU work per frame is one matrix plus the label overlay.

Benchmark: `AstroPlannerPro.exe --sky-benchmark [--sky-stars=N] [--sky-resolution=WxH]
[--sky-mode=dome|allSky|finder] [--sky-quality=low|medium|high] [--sky-landscape=ID|flat]` (add
`--ui=web` for the planetarium, with the windows its saved layout opens) opens the Sky tab, sweeps the view 360° for 10 s with vsync and
the frame cap off, and logs the average fps and the 95th-percentile frame time. `--sky-stars=2`
draws the catalogue twice (~167k stars, the stress test); `--sky-resolution` renders at that size
whatever the window (the canvas is scaled to fit).

The v3 baseline (method, interleaved runs, startup and memory) is in
[performance.md](performance.md). The table below was measured during M5/M6.

Development desktop: Ryzen 7 5700 (no integrated GPU) + Radeon RX 6600, WebView2 157, sky
brightness and labels on:

| Mode | Stars | Resolution | fps | p95 frame | First frame |
|---|---|---|---|---|---|
| dome | 83,476 | 1920×1080 | 1,517 | 0.9 ms | 0.13 s |
| all-sky | 83,476 | 1920×1080 | 1,287 | 1.0 ms | 0.11 s |
| finder | 83,476 | 1920×1080 | 1,536 | 1.0 ms | 0.13 s |
| dome | 166,952 | 1920×1080 | 1,526 | 0.9 ms | 0.12 s |
| dome | 166,952 | 2560×1440 | 1,554 | 0.9 ms | 0.11 s |
| all-sky | 166,952 | 2560×1440 | 1,294 | 1.0 ms | 0.19 s |

The budget (spec §4.2: ≥ 60 fps with 100k stars at 1080p on an integrated GPU, ≤ 2 s to the first
frame) is met here with a wide margin, but **it has not been measured on an integrated GPU**: no
such machine was available (the owner's decision for M2). The work per frame is one matrix, the
label overlay and two draw calls for the stars and lines, so integrated GPUs are expected to be
limited by fill rate, not by the star count; anyone with one can run the switches above and add a
row.

## Development

```bash
cd src/web
npm ci
npm run dev      # browser at http://localhost:5173: replays the golden C# messages (no WPF needed)
npm test         # Vitest: projection cross-check, contract, buffers, math, picking, clock
npm run lint     # ESLint (strict, type-checked) + Prettier
npm run build    # tsc + Vite → apps/planetarium/dist/
```

## Web workspace (v3, in development)

`src/web` is an npm workspace (one lockfile, shared tooling in the root `package.json`,
`eslint.config.js` and `tsconfig.base.json`):

```
packages/sky-engine   the renderer: mountSky(container) builds the canvases and connects the bridge
packages/bridge-client  the app channel (bridge v2): typed requests and events, contracts
                      generated from C# (docs/bridge.md)
apps/planetarium      the pages WebView2 loads from https://app.astroplanner/
  index.html            the v2 Sky tab: the sky alone (src/classic.ts)
  planetarium.html      the v3 UI (src/planetarium, React 19), opened with --ui=web
```

Both pages share one `sky-engine` chunk. `AstroPlannerPro.exe --ui=web` opens `WebShellWindow`:
one WebView2 filling the window, driven by the same `ShellViewModel` as the v2 window. The v2
window stays the default until v3.0.0. TSX is compiled by Vite itself, without the React plugin,
because its fast-refresh preamble is an inline script that the CSP forbids.

**CSP and UI libraries.** Both pages keep `style-src 'self'`: no `<style>` elements and no
`style=""` attributes in the markup. Styles set from script (`element.style`, React's `style`
prop) are allowed, so most libraries work. Checked with each library rendered under the pages'
CSP in Edge (2026-11-21):

| Library | Result |
|---|---|
| React Aria Components 1.22 | One injected `<style>`: `usePress` adds a `touch-action` rule. Fix: the page declares an element with id `react-aria-pressable-style` (React Aria then skips the injection) and our CSS carries the same rule. `usePreventScroll` injects only on iOS. With the fix: no violations. |
| cmdk 1.1 | `Command` works. `Command.Dialog` (Radix Dialog → react-remove-scroll) injects a `<style>`: put `Command` inside a React Aria `Modal` instead. |
| uPlot 1.6 | Works (its CSS is a file; everything else goes through `element.style`). |

The engine reports every CSP violation to the host log (`securitypolicyviolation` → `error`
message), so a library that injects styles shows up in `logs/` at once.
