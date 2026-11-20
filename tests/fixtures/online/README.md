# Recorded online responses

Real responses saved once for the Online tests, which never use the network. The coordinates are
those of Aguascalientes city centre, rounded to 2 decimals as the app sends them.

| File | Request (2026-11-20) |
|---|---|
| `7timer-astro.json` | `https://www.7timer.info/bin/astro.php?lon=-102.29&lat=21.88&ac=0&unit=metric&output=json&tzshift=0` |
| `open-meteo-forecast.json` | `https://api.open-meteo.com/v1/forecast?latitude=21.88&longitude=-102.29&hourly=…&timezone=GMT&forecast_days=7` (the fields of `OpenMeteoForecastService.BuildUrl`) |

To refresh them, repeat the requests with `curl` and update the expected values in the tests.
