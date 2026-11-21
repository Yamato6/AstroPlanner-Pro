# SGP4 verification set (near-Earth cases)

From D. Vallado, P. Crawford, R. Hujsak and T.S. Kelso, "Revisiting Spacetrack Report #3",
AIAA 2006-6753 (2006), as distributed with their SGP4 code on CelesTrak and with
[python-sgp4](https://github.com/brandon-rhodes/python-sgp4) (MIT), downloaded 2026-11-20:

- `sgp4-ver-near-earth.tle`: the near-Earth satellites (period under 225 minutes) of `SGP4-VER.TLE`
  (00005, 06251, 22312, 28057, 28350, 28872, 29141, 29238, 88888). Line 2 ends with the start, stop
  and step of the reference run, in minutes.
- `tcppver-near-earth.out`: the matching TEME states of `tcppver.out` (time since epoch in minutes,
  position in km, velocity in km/s); the extra columns are dropped. 22312, 28872 and 29141 decay:
  their output stops where SGP4 reports the error.

Used by `tests/AstroPlanner.Core.Tests/SatelliteTests.cs`.
