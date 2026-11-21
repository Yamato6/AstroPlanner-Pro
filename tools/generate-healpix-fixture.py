"""Regenerates tests/fixtures/healpix/nested.csv from astropy-healpix (BSD-3-Clause).

Rarely needed: the CSV is committed and the tests never run Python.

    python -m venv .venv && .venv/Scripts/python -m pip install astropy-healpix
    .venv/Scripts/python tools/generate-healpix-fixture.py

Columns: order, lon, lat (degrees), nested index, dx, dy (astropy-healpix's offsets inside the
pixel, in [0, 1)), and the centre of that pixel (lon, lat).
"""

import pathlib

import astropy.units as u
import astropy_healpix
import numpy as np
from astropy_healpix import healpix_to_lonlat, lonlat_to_healpix

OUT = pathlib.Path(__file__).resolve().parent.parent / "tests" / "fixtures" / "healpix" / "nested.csv"

rng = np.random.default_rng(20261120)
points = [
    # Poles, the RA 0/360 seam, the polar-cap boundary (|z| = 2/3 → |lat| = 41.8103°) and face corners.
    (0.0, 90.0), (123.4, 90.0), (0.0, -90.0), (300.0, -90.0),
    (0.0, 0.0), (359.9999999, 0.0), (360.0, 10.0), (-0.0000001, -10.0),
    (45.0, 41.8103149), (45.0, 41.8103148), (135.0, -41.8103149), (135.0, -41.8103148),
    (90.0, 0.0), (180.0, 0.0), (270.0, 0.0), (45.0, 0.0), (225.0, 41.81),
    (83.8221, -5.3911),   # M42
    (10.6847, 41.2690),   # M31
    (250.4235, 36.4613),  # M13
    (88.7929, 7.4071),    # Betelgeuse
    (279.2347, 38.7837),  # Vega
    (37.9545, 89.2641),   # Polaris
    (95.9880, -52.6957),  # Canopus
]
points += [(float(lon), float(np.degrees(np.arcsin(z)))) for lon, z in
           zip(rng.uniform(0, 360, 60), rng.uniform(-1, 1, 60))]
# Near the poles, where the precision of the cap formula matters.
points += [(float(lon), float(lat)) for lon, lat in
           zip(rng.uniform(0, 360, 12), np.concatenate([rng.uniform(89.9, 90, 6), rng.uniform(-90, -89.9, 6)]))]

orders = [0, 1, 3, 5, 9, 12, 15, 18]

lines = ["order,lon,lat,nested,dx,dy,center_lon,center_lat"]
for order in orders:
    nside = 2 ** order
    lon = np.array([p[0] for p in points]) * u.deg
    lat = np.array([p[1] for p in points]) * u.deg
    index, dx, dy = lonlat_to_healpix(lon, lat, nside, return_offsets=True, order="nested")
    clon, clat = healpix_to_lonlat(index, nside, order="nested")
    for i, (plon, plat) in enumerate(points):
        lines.append(",".join([
            str(order), repr(plon), repr(plat), str(int(index[i])),
            repr(float(dx[i])), repr(float(dy[i])),
            repr(float(clon[i].to_value(u.deg))), repr(float(clat[i].to_value(u.deg))),
        ]))

OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
print(f"{len(lines) - 1} rows, astropy-healpix {astropy_healpix.__version__} -> {OUT}")
