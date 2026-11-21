# Recorded online responses

Real responses saved once for the Online and UI tests, which never use the network. The
coordinates are those of Aguascalientes city centre, rounded to 2 decimals as the app sends them.

| File | Request (2026-11-20) |
|---|---|
| `7timer-astro.json` | `https://www.7timer.info/bin/astro.php?lon=-102.29&lat=21.88&ac=0&unit=metric&output=json&tzshift=0` |
| `open-meteo-forecast.json` | `https://api.open-meteo.com/v1/forecast?latitude=21.88&longitude=-102.29&hourly=...&timezone=GMT&forecast_days=7` (the fields of `OpenMeteoForecastService.BuildUrl`) |
| `hips-dsscolor-norder5-npix115.jpg` | `https://skies.esac.esa.int/DSSColor/Norder5/Dir0/Npix115.jpg` (DSS colour HiPS tile with the Pleiades; DSS: STScI/NASA, coloured and HEALPixed by CDS) |
| `sesame-barnard33.xml` | `https://vizier.cfa.harvard.edu/viz-bin/nph-sesame/-oxp/SNV?Barnard%2033` |
| `sesame-nothing.xml` | the same for `NoSuchObjectXyz123` |
| `wikipedia-summary-messier42.json` | `https://en.wikipedia.org/api/rest_v1/page/summary/Messier_42` (text CC BY-SA 4.0) |
| `wikipedia-summary-disambiguation.json` | the same for `Castor` |
| `commons-extmetadata-m42.json` | `https://commons.wikimedia.org/w/api.php?action=query&format=json&formatversion=2&prop=imageinfo&iiprop=extmetadata&iiextmetadatafilter=LicenseShortName\|LicenseUrl\|Artist\|Credit\|UsageTerms\|AttributionRequired&titles=File:Orion_Nebula_-_Hubble_2006_mosaic_18000.jpg` |

| `mpc-cometels-excerpt.txt` | 8 lines of `https://minorplanetcenter.net/iau/MPCORB/CometEls.txt` (2026-11-20) |
| `mpc-bright-2026-excerpt.txt` | first 6 lines of `https://minorplanetcenter.net/iau/Ephemerides/Bright/2026/Soft00Bright.txt` |
| `celestrak-visual.json` | `https://celestrak.org/NORAD/elements/gp.php?GROUP=visual&FORMAT=JSON` (2026-11-20, 157 objects) |
| `celestrak-stations.json` | `https://celestrak.org/NORAD/elements/gp.php?GROUP=stations&FORMAT=JSON` (2026-11-20, 17 objects) |

To refresh them, repeat the requests with `curl` and update the expected values in the tests.
