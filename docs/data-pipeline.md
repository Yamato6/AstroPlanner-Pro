# Data pipeline

How the catalogs shipped with AstroPlanner Pro are produced. The sources, versions, checksums
and licenses are listed in [data/SOURCES.md](../data/SOURCES.md).

```
data/raw/        downloaded sources (git-ignored, verified by SHA-256)
data/curated/    hand-written overlays (committed)        ─┐
                                                           ├─ tools/AstroPlanner.DataBuilder
data/build/      generated files (committed, shipped)    ◄─┘
  catalogs.sqlite        deep-sky objects + stars (embedded in AstroPlanner.Data)
  catalogs-report.txt    counts and differences of the last build (review it in PRs)
  sky/*.json             d3-celestial GeoJSON (input of build-sky)
  sky/*.bin, sky/*.json  sky renderer data written by build-sky (docs/sky-renderer.md)
  lp/*.bin.gz            light-pollution grids written by build-lp-grid (embedded in AstroPlanner.Data)
  lp/lp-report.txt       sizes and bounds of the last grid build
```

## Commands

Run from anywhere inside the repository (or pass `--root <repo>`):

```bash
dotnet run --project tools/AstroPlanner.DataBuilder -- download        # fetch + verify the pinned sources
dotnet run --project tools/AstroPlanner.DataBuilder -- build-catalogs  # data/raw + data/curated → data/build
dotnet run --project tools/AstroPlanner.DataBuilder -- build-sky       # catalogs.sqlite + GeoJSON → data/build/sky (renderer data)
dotnet run --project tools/AstroPlanner.DataBuilder -- build-lp-grid   # data/raw/World_Atlas_2015.tif (by hand) → data/build/lp
dotnet run --project tools/AstroPlanner.DataBuilder -- verify          # sanity checks of catalogs.sqlite, data/build/sky and data/build/lp
dotnet run --project tools/AstroPlanner.DataBuilder -- all             # download, build-catalogs, build-sky, verify (not build-lp-grid)
```

`download` skips files that are already present with the right checksum and fails if a
download does not match the pinned SHA-256. To update a source, change its URL, version and
checksum in `tools/AstroPlanner.DataBuilder/DataSources.cs` and in `data/SOURCES.md`, rebuild,
and review the diff of `catalogs-report.txt`.

`build-sky` needs no download: it reads the committed `catalogs.sqlite` and GeoJSON, so it can
be rerun at any time (it is deterministic; the formats are described in
[sky-renderer.md](sky-renderer.md#static-data-build-sky)).

## Light-pollution grids (`build-lp-grid`)

The World Atlas 2015 (Falchi et al. 2016, CC BY-NC 4.0, see [SOURCES.md](../data/SOURCES.md)) is a
3 GB GeoTIFF of **artificial** zenith sky brightness in mcd/m². It is far too big to ship or to
download in CI, so:

1. **By hand, once:** download `World_Atlas_2015.zip` (653 MB) from GFZ and unzip
   `World_Atlas_2015.tif` into `data/raw/`. `build-lp-grid` checks its pinned SHA-256
   (`DataSources.WorldAtlas`). It is never committed.
2. **`build-lp-grid`** streams the file once, a row of 128 × 128 tiles at a time.
   - The file is a classic little-endian TIFF: 43 200 × 17 406 uncompressed float32 cells of 30″,
     north-west corner at (85.054° N, 180° W). `GeoTiffRaster` reads exactly that and rejects
     anything else, so no TIFF library is needed.
   - **Mexico, native 30″** (14–33° N, 86–118° W; 2281 × 3841 cells). This is the region of the
     spec's default coverage.
   - **World, 5′**: blocks of 10 × 10 cells averaged in linear units (1741 × 4320 cells, 60° S to
     85° N). No-data cells (−3.4·10³⁸) and NaN count as dark.
   - **Quantisation:** one byte per cell in log10 space. Byte 0 means below 10⁻⁴ mcd/m²
     (negligible next to the 0.171 natural sky). Byte c ≥ 1 means 10^(−4 + (c − 1)·6.5/254),
     a step of 0.0256 dex ≈ 0.064 mag, well below the atlas' own uncertainty.
   - **File format** (`LightPollutionGrid`): gzip of a 40-byte header ("APLP", version, rows,
     columns, north and west edges, cell size) followed by the bytes, north to south.
     Deterministic: the same TIFF gives the same bytes.
3. **Size:** `mexico-30s.bin.gz` 1.73 MB + `world-5m.bin.gz` 1.36 MB = **3.1 MB** (the target was
   ≤ 20 MB). Both are embedded in `AstroPlanner.Data` (`WithCulture="false"`: MSBuild would
   otherwise read ".bin" as the Bini culture and move them to a satellite assembly). Building them
   takes about 7 s.

**Lookup** (`LightPollutionAtlas`, implementing Core's `ILightPollutionAtlas`):

- Bilinear interpolation between cell centres, in linear units.
- The Mexico grid is used first, the world grid otherwise. Outside both (e.g. Antarctica) there is
  no value and the app assumes a natural 22.0 unless an SQM is typed.
- Zenith SQM = −2.5·log10((L + 0.171) / 1.08·10⁸). With it, central Aguascalientes reads 1.84
  mcd/m², SQM 19.3 (Bortle 7), and San Pedro Mártir reads 0.0006 mcd/m², SQM 22.0.

`verify` checks that both grids parse and that six checkpoints give plausible values:

- Aguascalientes and Mexico City (bright);
- San Pedro Mártir, the Pacific and Cerro Tololo (dark);
- New York (world grid, bright).

## What `build-catalogs` does

**OpenNGC → objects** (`OpenNgcImporter`)

- Types are mapped to `CelestialObjectType` (`G` → Galaxy, `Cl+N` → ClusterWithNebula,
  `HII` → HiiRegion, `*Ass` → StellarAssociation, `Other` → Unknown, …); an unknown type stops
  the build.
- `NonEx` rows (objects that do not exist) are dropped. `Dup` rows are folded into the object
  they duplicate as an extra designation (e.g. IC 11 becomes a cross-ID of NGC 281).
- **Primary id**: the Messier number when there is one ("M42", as in v1, so plans and
  logbooks keep working), else "C<n>" for the Caldwell-only objects of the addendum (C9, C14,
  C41, C99), else the OpenNGC name formatted as "NGC 7000", "IC 434", "Mel 22"…
- **Cross-IDs**: M, NGC, IC, Caldwell and the catalogs useful to amateurs (PGC, UGC, UGCA, ESO,
  LBN, PK, PN G, HD, HIP, Sh2, Mel, Cl). Survey ids (2MASX, SDSS, IRAS, MCG…) are dropped. A
  cross-ID that is another object's own id stays with that object (OpenNGC lists NGC 6839 as a
  cross-ID of M71, but NGC 6839 is an object of its own).
- **Curated overlay** (`data/curated/messier.json`): the English common names used since v1
  come first (OpenNGC's names become alternates), and M102 is identified with NGC 5866.
- **Magnitude**: V, or B when V is unknown (`magnitude_band` records which).
- **Constellation**: recalculated from the J2000 position with the IAU boundaries (Roman 1987);
  the report lists any difference with OpenNGC (there are none apart from Serpens, which
  OpenNGC splits into Se1/Se2).

**HYG → stars** (`HygImporter`): every star to magnitude 9.0 except the Sun, with its
identifiers, B–V, proper motion and (schema 2) spectral type, distance, variability and
multiple-system grouping; the constellation is recalculated the same way.

**WDS → double stars** (`WdsImporter`, schema 2): the pairs of the Washington Double Star
Catalog summary with a primary up to mag 7, a companion up to mag 11 and a last separation of
0.3″–300″ (measured in 1900 or later), each matched to the nearest HYG star within 1′ whose
magnitude agrees within 0.7 mag. The report counts selected, matched and unmatched pairs
(2026-10-29: 3,239 selected, 2,975 matched to 2,445 stars, 264 unmatched).

**Output** (`CatalogWriter`): `catalogs.sqlite`, schema in
`src/AstroPlanner.Data/Catalog/CatalogSchema.cs` (tables `meta`, `objects`, `designations`,
`names`, `stars`, `double_stars`). The build is **deterministic**: rows are written in a fixed order, no
timestamps are stored and the file is vacuumed, so the same sources give the same bytes (a test
checks it). `meta` records the schema version and the version and checksum of every source.

## How the app uses it

`catalogs.sqlite` (~13.2 MB) is an embedded resource of `AstroPlanner.Data`. SQLite needs a
file, so `SqliteCatalogRepository` copies it once to
`%LocalAppData%\AstroPlannerPro\catalogs\catalogs-<hash>.sqlite` (older copies are deleted),
opens it read-only and loads into memory the ~13.4k deep-sky objects and the ~8.9k stars to
magnitude 6.5 (`StarCatalogLimitMagnitude`, ids "HIP n", else "HD n", else "HYG n") with their
double-star pairs. Search and filters never touch the database. The sky renderer draws all the
stars to magnitude 9 from `data/build/sky/stars.bin`, packed into the exe with the renderer
(docs/sky-renderer.md); a star picked in the sky selects its catalog row by HYG id.

## Tests

- `tests/AstroPlanner.DataBuilder.Tests` builds a catalog from small excerpts of the real files
  (`tests/fixtures/openngc`, `tests/fixtures/hyg`, `tests/fixtures/wds`): formatting of ids, type mapping, duplicates,
  ambiguous cross-IDs, the curated overlay, determinism and the basic checks of `verify`.
- `tests/AstroPlanner.Data.Tests` reads the committed `catalogs.sqlite`: 110 Messier and 109
  Caldwell objects, cross-IDs, sizes, names, metadata.
- `SkyDataBuilderTests` builds the sky files from the same excerpts and small GeoJSON fixtures
  (`tests/fixtures/sky`): order, unit vectors, labels, ≤ 2° pieces, determinism, `verify`.
- `LightPollutionGridBuilderTests` writes a tiny tiled GeoTIFF laid out like the atlas and checks
  the regional window, the world averaging (including partial edge blocks and no-data),
  determinism and `verify`. `LightPollutionTests` (Data) checks the quantisation round trip,
  interpolation, the format and the embedded grids.
- CI runs `verify` on the committed database, sky files and light-pollution grids; it never
  downloads the sources.
