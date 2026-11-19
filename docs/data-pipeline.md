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
  sky/*.json             d3-celestial files for the sky renderer (M2)
```

## Commands

Run from anywhere inside the repository (or pass `--root <repo>`):

```bash
dotnet run --project tools/AstroPlanner.DataBuilder -- download        # fetch + verify the pinned sources
dotnet run --project tools/AstroPlanner.DataBuilder -- build-catalogs  # data/raw + data/curated → data/build
dotnet run --project tools/AstroPlanner.DataBuilder -- verify          # sanity checks of data/build/catalogs.sqlite
dotnet run --project tools/AstroPlanner.DataBuilder -- all             # the three, in order
```

`download` skips files that are already present with the right checksum and fails if a
download does not match the pinned SHA-256. To update a source, change its URL, version and
checksum in `tools/AstroPlanner.DataBuilder/DataSources.cs` and in `data/SOURCES.md`, rebuild,
and review the diff of `catalogs-report.txt`.

The light-pollution grids (`build-lp-grid`, from the World Atlas 2015) arrive in M3.

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
identifiers, B–V and proper motion; the constellation is recalculated the same way.

**Output** (`CatalogWriter`): `catalogs.sqlite`, schema in
`src/AstroPlanner.Data/Catalog/CatalogSchema.cs` (tables `meta`, `objects`, `designations`,
`names`, `stars`). The build is **deterministic**: rows are written in a fixed order, no
timestamps are stored and the file is vacuumed, so the same sources give the same bytes (a test
checks it). `meta` records the schema version and the version and checksum of every source.

## How the app uses it

`catalogs.sqlite` (~10.8 MB, ~4.8 MB compressed inside the `.exe`) is an embedded resource of
`AstroPlanner.Data`. SQLite needs a file, so `SqliteCatalogRepository` copies it once to
`%LocalAppData%\AstroPlannerPro\catalogs\catalogs-<hash>.sqlite` (older copies are deleted),
opens it read-only and loads the ~13k objects into memory. Search and filters never touch the
database. Stars are not loaded yet: the sky renderer reads them from M2.

## Tests

- `tests/AstroPlanner.DataBuilder.Tests` builds a catalog from small excerpts of the real files
  (`tests/fixtures/openngc`, `tests/fixtures/hyg`): formatting of ids, type mapping, duplicates,
  ambiguous cross-IDs, the curated overlay, determinism and the basic checks of `verify`.
- `tests/AstroPlanner.Data.Tests` reads the committed `catalogs.sqlite`: 110 Messier and 109
  Caldwell objects, cross-IDs, sizes, names, metadata.
- CI runs `verify` on the committed database; it never downloads the sources.
