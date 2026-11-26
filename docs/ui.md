# Planetarium UI (v3, in development)

`AstroPlannerPro.exe --ui=web` opens the v3 interface: the live sky fills the window and the
interface floats over it, in one WebView2 page (`src/web/apps/planetarium/planetarium.html`).
The classic WPF window stays the default until v3.0.0. The page talks to the same view models as
the classic window through the app channel ([bridge.md](bridge.md)).

## Code

| Folder | Content |
|---|---|
| `packages/ui-kit` | Design tokens (`tokens.css`), Inter (`fonts.css`), accessible components on React Aria (`IconButton`, `ToggleIconButton`, `Button`, `Switch`, `Panel`), the window manager (`windows/`) |
| `apps/planetarium/src/planetarium/app` | Host queries (TanStack Query), the shortcut registry, idle toolbars, formatting, i18n |
| `apps/planetarium/src/planetarium/shell` | Top bar, left rail, dock and darkness bar, HUD, object card, context menu, command palette, toasts |
| `apps/planetarium/src/planetarium/windows` | The window registry, the window layer, layout persistence, Settings, Help |
| `apps/planetarium/src/planetarium/state` | Page-only state (zustand): immersive mode, the palette, the window layout, toasts |
| `apps/planetarium/src/planetarium/i18n/en.json` | UI chrome strings; domain text comes formatted from C# |
| `apps/planetarium/e2e` | Playwright tests against a mock host |

## Visual language: "observatory instrument"

Calm and dense, so the sky stays the subject:

- Glass panels (`--surface-glass` + blur) with hairline borders over the sky; *reduce
  transparency* (`<html data-transparency="reduced">`) makes them opaque.
- v2's blue-slate palette; the cyan accent (`--accent`) marks selection and focus only, and the
  verdict colours are the only other saturated hues.
- Inter with tabular numerals for times and numbers.
- Night vision (`<html data-theme="night">`, from `ui.get`) redefines every token in
  red. Both palettes come from `Themes/Dark.xaml` and `Themes/Night.xaml`.
- Motion: 150–220 ms ease-out, none with `prefers-reduced-motion`.

## State

- **Host state** is a snapshot per area (`observer.get`, `sky.get`) held by TanStack Query,
  never polled: the area's change event (`observer.changed`, `sky.changed`) refetches it when
  its revision moved. Commands (`sky.play`, `sky.setLayer`…) answer with the new snapshot, which
  replaces the cached one (`useSkyCommand`).
- **Time** runs on the page: the sky snapshot holds the clock and the rate, and the top bar
  advances it locally at 4 Hz, so playing time costs no messages.
- **Page state** (immersive mode, the window layout) is in zustand.
- **Interface state the host keeps** (`ui.get`): night vision (the same saved theme as the
  classic window), reduce transparency (saved) and full screen (the window's). The page turns it
  into `<html data-theme>` and `<html data-transparency>`.

## Windows

Every screen is a non-modal window over the sky (`role="dialog"`, `aria-modal="false"`). The
manager is in `packages/ui-kit/src/windows`: `layout.ts` holds the state and the geometry as pure
functions (open, focus, minimise, the rail's press, snap zones, magnet, resize, clamping), unit
tested; `FloatingWindow` draws one window and turns pointer and keyboard gestures into them.

- Move by the title bar; resize by any edge or corner; edges stick to the area and to other
  windows within 10 px.
- Drag to the left or right edge: a half-width side sheet (its inner edge resizes it); to the
  top: maximised, 24 px of sky left around it. Double-click the title bar to maximise or restore.
- The rail opens a window, brings it to the front, or minimises it when it already is in front;
  a minimised window keeps a dot in the rail.
- Keyboard, with the window focused (it takes the focus when opened): arrows move it,
  Shift+arrows resize it, Ctrl+←/→ dock it, Ctrl+↑ maximises, Ctrl+↓ floats, Esc closes it.
- The layout (open, minimised, arrangement, floating rectangle, dock width, stacking order) is
  saved 300 ms after a change (`layout.save`, table `window_layouts` in `app.db`) and restored
  at start (`layout.load`). Settings › Reset forgets it.
- Windows only move a composited layer: the sky is never re-rendered for them.

The registry (`windows/registry.ts`) lists every window in rail order with its icon and size.
R1b ships Settings and Help; the others show an empty state until R4 brings them. Settings ›
Landscape (R2c, `LandscapeSection`) is a radio list (React Aria `RadioField`): the site's horizon
profile, a flat ground or one of the media folder's landscapes, each with its credit; *Use the
landscape horizon for calculations* asks for a confirmation in place before it replaces the site's
horizon. It needs a saved active site and the media folder, and says so when either is missing.
Settings › Survey (R3a, `SurveySection`) chooses the survey shown when zooming in (DSS2 colour,
Pan-STARRS DR1 colour, or none, each with its credit), its contrast (*Realistic* or *Survey*) and
the tile cache's size (256 MB to 10 GB, with the space in use), and clears it; with online images
off it says that only downloaded tiles show. Its segmented choices are `SegmentedChoice`.

## Shell

- **Dock** (bottom): the darkness bar, then the time controls (steps of 10 min; Shift 1 h, Ctrl
  1 day, Alt 1 min; play/pause; the speed; Now; the date and time in the site's zone, applied
  with Enter or on leaving the field), the layer switches and the view mode.
- **Darkness bar** (the signature detail): tonight from an hour before sunset to an hour after
  sunrise, sampled by C# every 10 min of the site's clock (`night.get`, from Core's
  `NightSkySamples`): daylight, civil, nautical and astronomical twilight and night as bands, the
  Moon above the horizon as a light strip at the top (brighter for a fuller Moon) and the cloud
  cover at the bottom when there is a forecast. It is a React Aria slider: dragging or the arrow
  keys (10 min) move a preview, and the sky time is set when it is released (`sky.setTime`).
- **Object card** (top left): the selection from `selection.get`, every text formatted by C#;
  "Why?" opens the feasibility reasons; Center, Frame with the camera, Add to / Remove from my
  plan. The Moon, the Sun or a satellite show their name only.
- **HUD** (bottom left): field of view, J2000 RA/Dec and Alt/Az under the pointer, zenith SQM and
  the star limit, and below them the credits of the images on screen (the Milky Way panorama, CC
  BY: shown whenever it is visible; the landscape). It follows the engine's page events
  (`skyEvents`: `view`, `cursor`), so the pointer costs no messages to the host.
- **Context menu**: a right click selects what is under the pointer (`mountSky(…, {
  contextMenu: true })`; the classic Sky tab keeps ignoring right clicks) and offers its actions,
  or the point's coordinates on empty sky.
- **Command palette** (`Ctrl+K`, `/`): catalog matches from `search.query` (exact id or name
  first, then the brightest), the windows and every command of the registry with its key;
  "Search online" runs the classic search (`search.resolve`, Sesame when allowed).
- **Toasts** (bottom right): `aria-live="polite"`, never focused; errors stay until closed.
- **Idle toolbars**: the top bar and the dock fade after 3 s without input and come back with the
  first movement or key, never while the focus is in them; Settings › Interface turns it off.

## Keyboard

One registry (`app/shortcuts.ts`) declares every shortcut, so Help and the command palette can
list them; no shortcut fires while a text field has focus, inside a menu, a list or a modal
dialog (React Aria's autocomplete re-sends keys to its list), or for a key something else
already handled. A symbol (`/`, `,`) matches with or without Shift (Spanish keyboards type `/`
with Shift+7). The sky's own keys are off on this
page (`mountSky(container, { keyboard: false })`). The window gives the page the keyboard focus
whenever it is activated.

| Key | Action | Key | Action |
|---|---|---|---|
| `Space` | play / pause time | `F5` | now |
| `J` / `L` | slower / faster time | `1` `2` `3` | dome / all-sky / finder |
| `C` / `B` / `N` | constellation lines / boundaries / names | `G` / `E` | alt-az / equatorial grid |
| `H` | hide the interface | `Ctrl+Shift+N` | night vision |
| `F11` | full screen | `F1` / `Ctrl+,` | Help / Settings |
| `Ctrl+W` | close the window in front | `Ctrl+Tab` / `Ctrl+Shift+Tab` | next / previous window |
| `Ctrl+K` / `/` | search and commands | `Esc` | close the window you are in, else clear the selection |
| `T` | track the selected object (a manual pan stops it, with a toast) | `Home` | back to south, 30° up, 70° field |
| `A` | atmosphere and sky brightness | `Shift+G` | ground and landscape |

## CSP

The page's policy allows only its own scripts, styles and fonts (`style-src 'self'`,
`font-src 'self'`). Libraries must not inject `<style>` elements: React Aria's one rule is
pre-empted by `<template id="react-aria-pressable-style">` in the page and carried by
`tokens.css`. The E2E tests fail on any `securitypolicyviolation`.

## Tests

```bash
cd src/web
npm test                                   # Vitest: ui-kit components, shortcut and time logic
npm run e2e -w @astroplanner/planetarium   # Playwright (installed Edge) against the mock host
```

The mock host (`e2e/mockHost.ts`) replaces `window.chrome.webview`: it validates every request
against the contracts generated from C# and answers from the C# golden files
(`tests/fixtures/bridge/app/outgoing`), so it cannot drift from the real host. The E2E suite runs
the production build (`vite preview`) and checks the shell flows, the window manager (drag, snap, resize, keyboard, restore after restart), the dock and darkness bar, the card, the palette, toasts and idle toolbars, axe (WCAG 2.2 AA, both themes, with windows, the card and the palette open)
and that no request breaks its contract.
