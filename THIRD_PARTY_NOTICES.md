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

## Data used from M3

### World Atlas 2015 of artificial night-sky brightness — CC BY-NC 4.0

Falchi, F., Cinzano, P., Duriscoe, D., Kyba, C. C. M., Elvidge, C. D., Baugh, K., Portnov,
B. A., Rybnikova, N. A. and Furgoni, R. (2016): The new world atlas of artificial night sky
brightness. *Science Advances* 2, e1600377. Data: GFZ Data Services,
https://doi.org/10.5880/GFZ.1.4.2016.001, licensed under the Creative Commons
Attribution-NonCommercial 4.0 International license. **Because of this NonCommercial license,
AstroPlanner Pro is and must remain free of charge.** Only derived grids will be distributed.

## Software libraries

| Library | License |
|---|---|
| .NET, WPF, Microsoft.Extensions.DependencyInjection, Microsoft.Data.Sqlite | MIT |
| SQLitePCLRaw (bundled SQLite) | Apache-2.0; SQLite itself is public domain |
| CommunityToolkit.Mvvm | MIT |
| ScottPlot, SkiaSharp | MIT |
| Microsoft.Web.WebView2 (SDK) | BSD-3-Clause (Microsoft); the WebView2 Runtime is part of Windows / installed separately |
| three.js (sky renderer) | MIT — Copyright © 2010-2026 three.js authors |
| zod (sky renderer) | MIT — Copyright © 2025 Colin McDonnell |

Build- and test-only packages of the renderer (not shipped): TypeScript (Apache-2.0), Vite,
Vitest, ESLint, typescript-eslint, Prettier (MIT).

Test-only packages (not shipped): xUnit (Apache-2.0), FluentAssertions 8 (Xceed Community
License, free for non-commercial use), coverlet (MIT), Microsoft.NET.Test.Sdk (MIT).
