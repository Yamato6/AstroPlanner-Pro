# Performance baseline

This is the reference for the v3 performance budgets: the v2 UI measured on the reference
machine, before the Application layer (R0c) and the planetarium shell (R1). Later milestones
compare against these numbers using the same method.

## Reference machine

| Part | Value |
|---|---|
| CPU | AMD Ryzen 7 5700 (8 cores, no integrated GPU) |
| GPU | AMD Radeon RX 6600, driver 32.0.21045.29003 |
| Memory | 32 GB |
| OS | Windows 11 IoT Enterprise LTSC 2024 (10.0.26100) |
| WebView2 | 157.0.4341.60 |
| Display | 1920×1080 at 100 Hz (does not affect the benchmark, which renders at a fixed size) |

## Method

- **Builds:** self-contained single-file exes, published exactly as the release job does.
  - The v2.0.0 build is the `v2.0.0` tag with the renderer built first. The published 2.0.0 exe
    lacks the renderer; see the CHANGELOG.
  - `main` is `b24d4f4`, which includes R0a and R0b.
- **Runs:**
  - `AstroPlannerPro.exe --sky-benchmark --sky-resolution=1920x1080 --sky-mode=dome|allSky`.
  - The same with `--ui=web`, for the planetarium page.
  - Three rounds, with the builds interleaved so that background load affects them equally. The
    numbers come from the app's own log.
- **Startup:** time from process start to the window's first frame (log line `Window shown … ms`).
  The first run of each build is cold, because it extracts the bundle and the sky assets, so it
  is not counted.
- **Sky:**
  - First frame: the log line `first frame after … ms`, measured from WebView2 ready to the first
    rendered frame.
  - Frame rate: a 10 s camera sweep with vsync and the frame cap off (log line
    `Sky benchmark: … fps · p95 … ms`).
- **Memory:** private bytes of the app plus its WebView2 processes, 17 s after launch, during the
  sweep.
- **Noise:** the machine was in normal use with other apps open, so single runs vary by up to
  ±25 %. The tables give the median and the range.

## Baseline (2026-11-22)

Setup: 83,476 stars, sky brightness and labels on, 1920×1080. The fps columns show the median,
the p95 frame time in parentheses, and the range.

| Build | Window | Startup | Sky first frame | Dome fps | All-sky fps | Memory |
|---|---|---|---|---|---|---|
| v2.0.0 | classic, Sky tab | 1.51 s | 122 ms | 1,332 (1.0 ms) · 1,226–1,351 | 998 (1.4 ms) · 992–1,020 | ~750 MB |
| `main` (R0a + R0b) | classic, Sky tab | 1.53 s | 121 ms | 1,351 (1.0 ms) · 1,302–1,365 | 949 (1.4 ms) · 672–1,013 | ~730 MB |
| `main` | `--ui=web` (planetarium) | 1.19 s | 129 ms | 1,337 (1.0 ms) · 939–1,368 | 765 (1.8 ms) · 726–974 | ~680 MB |

- **R0a and R0b have no measurable cost:** the classic window performs as in v2.0.0.
- **The planetarium page starts faster** because its window has no Tonight view to build. Its
  all-sky numbers vary the most and should be measured again on an idle machine before R1
  closes.
- **The exe grew** from 81.6 MB (v2.0.0) to 84.9 MB, from React and the second page.
- **WebView2 is about 450 MB of the memory:** without its sky (the published 2.0.0 exe), the app
  uses about 280 MB.

## v3 budgets (spec §11)

| Metric | Budget | Baseline |
|---|---|---|
| Launch to first sky frame | ≤ 2.5 s warm, ≤ 4 s cold | ~1.7 s warm (window + sky first frame) |
| Dome, 83k stars, atmosphere Medium, landscape on, 3 windows open | ≥ 60 fps, p95 ≤ 20 ms | 1,350 fps with no shell, no atmosphere, no landscape |
| Memory (whole app) | ≤ 1.2 GB with the survey cache warm | ~730 MB |

From R1 on, each milestone adds a row to the baseline table, measured with the same method.
`--sky-benchmark` will be extended to open windows and the survey layer.
