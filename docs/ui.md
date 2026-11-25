# Planetarium UI (v3, in development)

`AstroPlannerPro.exe --ui=web` opens the v3 interface: the live sky fills the window and the
interface floats over it, in one WebView2 page (`src/web/apps/planetarium/planetarium.html`).
The classic WPF window stays the default until v3.0.0. The page talks to the same view models as
the classic window through the app channel ([bridge.md](bridge.md)).

## Code

| Folder | Content |
|---|---|
| `packages/ui-kit` | Design tokens (`tokens.css`), Inter (`fonts.css`), accessible components on React Aria (`IconButton`, `ToggleIconButton`, `Button`, `Switch`, `Panel`), the window manager (`windows/`) |
| `apps/planetarium/src/planetarium/app` | Host queries (TanStack Query), the shortcut registry, i18n |
| `apps/planetarium/src/planetarium/shell` | Top bar, left rail (later: dock, HUD, object card, context menu, palette) |
| `apps/planetarium/src/planetarium/windows` | The window registry, the window layer, layout persistence, Settings, Help |
| `apps/planetarium/src/planetarium/state` | Page-only state (zustand): immersive mode, the window layout |
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
R1b ships Settings and Help; the others show an empty state until R4 brings them.

## Keyboard

One registry (`app/shortcuts.ts`) declares every shortcut, so Help and the command palette can
list them; no shortcut fires while a text field has focus. The sky's own keys are off on this
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
the production build (`vite preview`) and checks the shell flows, the window manager (drag, snap, resize, keyboard, restore after restart), axe (WCAG 2.2 AA, both themes, windows open)
and that no request breaks its contract.
