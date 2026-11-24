# Third-party notices

AstroPlanner Pro's own code is under the [MIT license](LICENSE). It includes or is built with
the following third-party data and software, each under its own license. Where each data file
comes from, its version and checksum are recorded in [data/SOURCES.md](data/SOURCES.md).

## Data shipped with the app

### OpenNGC — CC BY-SA 4.0

NGC/IC catalog by **Mattia Verga**, https://github.com/mattiaverga/OpenNGC (version
v20260501), licensed under the Creative Commons Attribution-ShareAlike 4.0 International
license, https://creativecommons.org/licenses/by-sa/4.0/. OpenNGC is built from NED (NASA/IPAC
Extragalactic Database, operated by JPL/Caltech under contract with NASA), HyperLEDA, SIMBAD
(CDS, Strasbourg), HEASARC and Harold Corwin's NGC/IC positions and notes.

### HYG database — CC BY-SA 4.0

Star database v4.4 by **David Nash / astronexus.net**, https://codeberg.org/astronexus/hyg,
licensed under the Creative Commons Attribution-ShareAlike 4.0 International license.

**ShareAlike.** The catalog database shipped with the app (`catalogs.sqlite`, built by
`tools/AstroPlanner.DataBuilder`) is an adaptation of OpenNGC and HYG: the objects were
re-identified, their constellations recalculated, survey identifiers dropped and English
Messier names added. That database is distributed under CC BY-SA 4.0 as well; it is generated
from the public sources by the open-source tool in this repository.

### Washington Double Star Catalog — public domain

Double-star pairs (separation, position angle, magnitudes) from the **Washington Double Star
Catalog** maintained by the U.S. Naval Observatory (Mason, B. D. et al. 2001, AJ 122, 3466),
summary file of 2026-10-29. A U.S. government work in the public domain, credited here and in
the app. `tests/fixtures/wds/sample.txt` is a short excerpt used by the tests.

### IAU constellation boundaries — Roman (1987), via astropy (BSD-3-Clause)

Table of Roman, N. G. (1987), "Identification of a constellation from a position", PASP 99,
695 (CDS catalogue VI/42), and the IAU constellation names, as distributed with astropy:

```
Copyright (c) 2011-2026, Astropy Developers

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.
* Redistributions in binary form must reproduce the above copyright notice, this
  list of conditions and the following disclaimer in the documentation and/or
  other materials provided with the distribution.
* Neither the name of the Astropy Team nor the names of its contributors may be
  used to endorse or promote products derived from this software without
  specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### Cloud forecast — Open-Meteo, CC BY 4.0

Weather data by [Open-Meteo.com](https://open-meteo.com/), under CC BY 4.0, fetched at run time.

### Seeing and transparency — 7Timer!

Astronomical forecast by [7Timer!](https://www.7timer.info/) (Ye, Q.-Z. 2011, *PASP* 123, 113),
fetched at run time. Its data is free to use and redistribute for non-commercial purposes;
AstroPlanner Pro is free.

### Survey images — DSS2 colour and Pan-STARRS1 (HiPS by CDS), fetched at run time

- **Digitized Sky Survey** (STScI/NASA): free for scientific, educational and non-profit use,
  under CC BY-NC-SA 4.0 ([terms](https://archive.stsci.edu/dss/copyright.html)). Plates
  © 1993-2003 California Institute of Technology (POSS-II), © 1976-1993 UK SERC/PPARC (ROE),
  © 1993-2004 Anglo-Australian Observatory Board, © 1984-2004 AURA. Coloured and made into a HiPS
  by CDS (Strasbourg); served by CDS alasky and its ESA ESASky mirror.
- **Pan-STARRS1 DR1** (STScI/MAST), HiPS by CDS.

The preview credits the survey and the mirror under every image. `docs/screenshots/framing.png`
contains DSS imagery (CC BY-NC-SA 4.0) and Wikipedia text (CC BY-SA 4.0); it is not covered by the
MIT license of the code.

### Descriptions — Wikipedia and Wikimedia Commons, fetched at run time

Text from the English Wikipedia, CC BY-SA 4.0, credited and linked under every description.
Images from Wikimedia Commons under each file's own license, shown with its author and license
name; images without a Commons license are not shown.

### Comet and asteroid elements — Minor Planet Center, fetched at run time

Orbital elements of comets (`CometEls.txt`) and of the year's bright asteroids
(`Ephemerides/Bright/{year}/Soft00Bright.txt`) from the IAU
[Minor Planet Center](https://minorplanetcenter.net/) (Smithsonian Astrophysical Observatory).
Credited in the app; the elements are only cached locally, not redistributed.
`tests/fixtures/online/mpc-*.txt` are short excerpts used by the tests.

### Satellite elements — CelesTrak, fetched at run time

GP element sets (OMM) of the `visual` and `stations` groups from
[CelesTrak](https://celestrak.org/) (T.S. Kelso), derived from the US Space Force's public data.
Only cached locally, not redistributed; `tests/fixtures/online/celestrak-*.json` are the two
answers of 2026-11-20 used by the tests.

### Satellite magnitudes — McCants' quicksat.mag

`src/AstroPlanner.Core/Algorithms/Data/satellite_magnitudes.dat` holds the intrinsic magnitudes
(1000 km, full phase) of the 2,688 satellites brighter than 5.0 in Mike McCants'
[quicksat.mag](https://www.mmccants.org/programs/qsmag.zip) (2026-09-14), freely distributed for
satellite observers. Only the catalog number and the magnitude are kept.

### SGP4 verification set — Vallado et al. (2006)

`tests/fixtures/satellites/` holds the near-Earth cases of `SGP4-VER.TLE` and `tcppver.out`
from D. Vallado, P. Crawford, R. Hujsak and T.S. Kelso, "Revisiting Spacetrack Report #3",
AIAA 2006-6753, as distributed with their SGP4 code (CelesTrak) and with python-sgp4 (MIT).
The SGP4 code in Core is our own implementation of the published algorithm.

### Name resolution — Sesame (CDS)

[Sesame](https://cds.unistra.fr/cgi-bin/Sesame) name resolver (SIMBAD, NED, VizieR) of the CDS,
Strasbourg, with its VizieR mirrors at CfA and IDIA. Used at run time; nothing is stored but the
answer in the local cache.

## Sky data (shipped since M2)

### d3-celestial — BSD-3-Clause

Constellation lines, boundaries, names and the Milky Way outline (`data/build/sky/`), by
Olaf Frohn, https://github.com/ofrohn/d3-celestial (v0.7.32):

```
Copyright (c) 2015, Olaf Frohn
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted
provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of
   conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice, this list of
   conditions and the following disclaimer in the documentation and/or other materials
   provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors may be used to
   endorse or promote products derived from this software without specific prior written
   permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR
OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```

## Light-pollution data (shipped since M3)

### World Atlas 2015 of artificial night-sky brightness — CC BY-NC 4.0

Falchi, F., Cinzano, P., Duriscoe, D., Kyba, C. C. M., Elvidge, C. D., Baugh, K., Portnov,
B. A., Rybnikova, N. A. and Furgoni, R. (2016): The new world atlas of artificial night sky
brightness. *Science Advances* 2, e1600377. Data: GFZ Data Services,
https://doi.org/10.5880/GFZ.1.4.2016.001, licensed under the Creative Commons
Attribution-NonCommercial 4.0 International license. **Because of this NonCommercial license,
AstroPlanner Pro is and must remain free of charge.** Only derived, quantised grids
(`data/build/lp`, Mexico at 30″ and the world at 5′) are distributed; the credit is also shown in
the About window.

## Software libraries

| Library | License |
|---|---|
| .NET, WPF, Microsoft.Extensions.DependencyInjection, Microsoft.Data.Sqlite | MIT |
| SQLitePCLRaw (bundled SQLite) | Apache-2.0; SQLite itself is public domain |
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Extensions.Http, Microsoft.Extensions.Http.Resilience | MIT |
| Polly (via Microsoft.Extensions.Http.Resilience) | BSD-3-Clause — Copyright © App vNext |
| ScottPlot, SkiaSharp | MIT |
| PDFsharp 6.2.4 (session plan PDF) | MIT — © empira Software GmbH (PDFsharp Team). Pinned on purpose: PDFsharp 6.5.0 and later are AGPL-3.0-only and are not used |
| System.Security.Cryptography.Pkcs, Microsoft.Extensions.Logging.Abstractions (PDFsharp dependencies) | MIT |
| Microsoft.Web.WebView2 (SDK) | BSD-3-Clause (Microsoft); the WebView2 Runtime is part of Windows / installed separately |
| three.js (sky renderer) | MIT — Copyright © 2010-2026 three.js authors |
| zod (sky renderer) | MIT — Copyright © 2025 Colin McDonnell |
| React, React DOM, scheduler (v3 planetarium UI) | MIT — Copyright © Meta Platforms, Inc. and affiliates |
| React Aria Components, React Aria, React Stately, @internationalized/* (v3 UI) | Apache-2.0 — Copyright © Adobe |
| @swc/helpers (React Aria dependency) | Apache-2.0 — Copyright © SWC contributors |
| TanStack Query (v3 UI) | MIT — Copyright © Tanner Linsley |
| Zustand (v3 UI) | MIT — Copyright © Paul Henschel |
| i18next, react-i18next, html-parse-stringify (v3 UI) | MIT — Copyright © i18next contributors |
| Lucide icons, lucide-react (v3 UI) | ISC — Copyright © Lucide Contributors (parts © Cole Bemis, MIT) |
| Inter typeface (v3 UI, bundled from @fontsource-variable/inter) | SIL Open Font License 1.1 — Copyright © The Inter Project Authors |
| use-sync-external-store, clsx, aria-hidden, client-only, tslib (small dependencies of the above) | MIT / 0BSD |

Build- and test-only packages of the web workspace (not shipped): TypeScript (Apache-2.0), Vite,
Vitest, ESLint, typescript-eslint, eslint-plugin-react-hooks, Prettier, Testing Library, jsdom
(MIT), Playwright (Apache-2.0), axe-core / @axe-core/playwright (MPL-2.0).

Test-only packages (not shipped): xUnit (Apache-2.0), FluentAssertions 8 (Xceed Community
License, free for non-commercial use), coverlet (MIT), Microsoft.NET.Test.Sdk (MIT).
