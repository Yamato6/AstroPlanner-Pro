# Data sources

Every external data file used by AstroPlanner Pro: where it comes from, the exact version,
its checksum and its license. The raw files live in `data/raw/` (git-ignored) and are fetched by
`tools/AstroPlanner.DataBuilder download`, which verifies the SHA-256 values below
(see [docs/data-pipeline.md](../docs/data-pipeline.md)). What the app ships is generated into
`data/build/` and committed.

Shipped data keeps its own license: the code is MIT, the catalogs are CC BY-SA 4.0 and the
light-pollution atlas (from M3) is CC BY-NC 4.0, which is why **AstroPlanner Pro must stay
free**. The notices shown to users are in [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

## OpenNGC — NGC/IC deep-sky catalog

| | |
|---|---|
| Source | https://github.com/mattiaverga/OpenNGC (Mattia Verga), DOI 10.21938/y.1ejWUD_MQ6b_eDFoVbbw |
| Version | tag `v20260501` |
| Files | `database_files/NGC.csv` — SHA-256 `840fe0c9ee1332e551b2e722a0e92726cd7b157914a3d2177602832aadd3aa9e`<br>`database_files/addendum.csv` — SHA-256 `1d8f0914e643ada325a5a94d88d8fefad6a4937a2f77cc34f21483af22b11983` |
| Downloaded | 2026-11-19 |
| License | **CC BY-SA 4.0** (stated in the repository README; GitHub does not detect it automatically) |
| Used for | `catalogs.sqlite`: 13,371 objects with types, positions, sizes, magnitudes, surface brightness, common names and cross-IDs (Messier, NGC, IC, Caldwell, PGC, UGC, ESO, LBN, Sh2, Mel, Cl…). All 110 Messier and 109 Caldwell objects are identified in it. |
| Attribution | "OpenNGC by Mattia Verga, https://github.com/mattiaverga/OpenNGC, CC BY-SA 4.0." OpenNGC itself acknowledges NED, HyperLEDA, SIMBAD, HEASARC and Harold Corwin's NGC/IC data. |

## Curated overlay — `data/curated/messier.json`

Written for this project (MIT): the English common names of the Messier objects used since
AstroPlanner v1, and one identification that differs from OpenNGC: **M102 = NGC 5866** (OpenNGC
lists M102 as a duplicate of M101; the app keeps the usual identification, as v1 did).

## HYG database — stars

| | |
|---|---|
| Source | https://codeberg.org/astronexus/hyg (David Nash, astronexus.net) |
| Version | HYG **v4.4** (2026-07-12), commit `53e3df311869e813ace5f1ad2ec4ce909f13256c` |
| File | `data/hyg/CURRENT/hyg_v44.csv.gz` (Git LFS, fetched from the `media/commit/…` URL) — SHA-256 `00b349893b9a53106dd488d8371e8d2fa586043e500bb3cdb8bff3931682197d` |
| Downloaded | 2026-11-19 |
| License | **CC BY-SA 4.0** |
| Used for | `catalogs.sqlite`, table `stars`: 83,476 stars to magnitude 9.0 (spec §16), with HIP/HD/HR, proper names, Bayer/Flamsteed, B–V and proper motion. Drawn by the sky renderer from M2. |
| Attribution | "HYG database v4.4 by David Nash / astronexus.net, CC BY-SA 4.0." |

## d3-celestial — constellation lines, boundaries, names, Milky Way

| | |
|---|---|
| Source | https://github.com/ofrohn/d3-celestial (Olaf Frohn) |
| Version | tag `v0.7.32` |
| Files (SHA-256) | `data/constellations.json` `11e14f1ee19b71bff5ca8af3f6e6a20596c4609dd86d43c9a796054fce88030c`<br>`data/constellations.lines.json` `294f66bef5d5cf50b1e17f16d2efa1d97a15131612c68dd935adef6e7373e13c`<br>`data/constellations.bounds.json` `f2e2687af6b20b24567879f838c21874d412efcc93ecc1966be07e78431cc196`<br>`data/mw.json` `aee221a7a0e879418e685de00c3e68fbdfac5667c0a8aab74929ef9cf4aab4fb` |
| Downloaded | 2026-11-19 |
| License | **BSD-3-Clause** |
| Used for | Copied unchanged to `data/build/sky/` for the sky renderer (M2). Not shipped yet. |

## Constellation boundaries — Roman (1987), CDS VI/42

| | |
|---|---|
| Source | Roman, N. G. (1987), "Identification of a constellation from a position", PASP 99, 695; CDS catalogue VI/42. The CDS site was unreachable on 2026-11-19, so the identical table distributed with **astropy v8.0.3** (`astropy/coordinates/data/constellation_data_roman87.dat` and `constellation_names.dat`) was used. |
| Files (SHA-256) | `constellation_data_roman87.dat` `17407174f8d1e2771e4e693bc007d542b69ae0b604cec311774c7e91e146f196` (357 rows)<br>`constellation_names.dat` `905b9005b4fe86c81140dbb818c186b96cc7f6c1bebce75113434466b35f28b2` (88 names) |
| License | astropy is **BSD-3-Clause**; the table is a published scientific catalogue (NASA/CDS). |
| Used for | Embedded in `AstroPlanner.Core` (`Algorithms/Data/`): IAU constellation of any position, including the planets. Verified with the eight test positions of the VI/42 ReadMe and against the constellations of all 13,371 OpenNGC objects (0 differences). |

## Satellite magnitudes — McCants' quicksat.mag (M5b)

| | |
|---|---|
| Source | Mike McCants, `quicksat.mag` in https://www.mmccants.org/programs/qsmag.zip (file dated 2026-09-14), downloaded 2026-11-20 |
| Files (SHA-256) | `qsmag.zip` `5228cbb2f68e2a28f3b6c5a6604352ae8c633757829edf34443ca7299a65f974`, `quicksat.mag` `955ad6f65e49dc8d95a2bc00f2930a9bcce9fb31dd7ccdfdfa64c3f145c91b67` |
| Derived | `src/AstroPlanner.Core/Algorithms/Data/satellite_magnitudes.dat` (2,688 satellites brighter than 5.0: catalog number and intrinsic magnitude), SHA-256 `d31a7aca0277bebb983b86b1a46319c69d5862123fe2bb757d98ec80c30d8489` |
| License | Freely distributed for satellite observers; only the factual magnitudes are kept, credited in THIRD_PARTY_NOTICES. |
| Used for | Embedded in `AstroPlanner.Core`: intrinsic magnitude (1000 km, full phase) of a satellite, scaled by range and phase. |

## JPL Horizons — test fixtures

| | |
|---|---|
| Source | NASA/JPL Horizons API 1.2, https://ssd.jpl.nasa.gov/api/horizons.api |
| Fetched | 2026-11-19, once, by `tools/fetch-horizons-fixtures.ps1` (the query is in each file header) |
| Files | `tests/fixtures/horizons/*.csv`: apparent azimuth/elevation every minute of the Sun (Aguascalientes, Madrid, Oslo) and Jupiter and Saturn; geocentric RA/Dec of comets and Vesta (M5a); the ISS (-125544) from Buenos Aires with range (M5b, `-SatellitesOnly`) |
| License | U.S. government work (public domain) |
| Used for | Tests only: rise, set, twilight and culmination times must agree within one minute. |

## World Atlas 2015 of artificial night-sky brightness — light pollution (M3)

| | |
|---|---|
| Source | Falchi, F. et al. (2016), "The new world atlas of artificial night sky brightness", *Science Advances* 2, e1600377. Data: GFZ Data Services, **DOI 10.5880/GFZ.1.4.2016.001** |
| Files | `https://datapub.gfz.de/download/10.5880.GFZ.1.4.2016.001/World_Atlas_2015.zip` (653 MB; `World_Atlas_2015.tif` ≈ 2.9 GB unzipped) — downloaded by hand into `data/raw/`, never committed |
| License | **CC BY-NC 4.0** — see the evidence below |
| SHA-256 | `World_Atlas_2015.tif`: `9725566ed4fcf1a8fa311654143bb024c4fe2a792b85356be99adce07e1e83c1` (3 012 927 014 bytes; checked by `build-lp-grid`) |
| Used for | Since M3: the compact grids `data/build/lp/mexico-30s.bin.gz` (30″) and `world-5m.bin.gz` (5′), 3.1 MB in total and embedded in the app (docs/data-pipeline.md), giving the zenith sky brightness of each site. Only these derived grids are distributed. |

**License evidence (checked 2026-11-19).** The `README.txt` inside the 2016 zip says further
distribution is prohibited. The DOI landing page at GFZ returned HTTP 404, so the authoritative
metadata of the DOI was checked instead: DataCite, `https://api.datacite.org/dois/10.5880/gfz.1.4.2016.001`
(record updated 2026-10-23), `rightsList`:
"Creative Commons Attribution Non Commercial 4.0 International", `rightsIdentifier`
`cc-by-nc-4.0`, plus "Open access". The current license of the dataset is therefore CC BY-NC 4.0,
which supersedes the 2016 README. Consequences: credit the dataset and the paper, distribute only
derived grids, and keep the app free.
