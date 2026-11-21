# External services

AstroPlanner Pro works offline. These are the only services it contacts, what each one receives,
how long its answers are kept and how to turn it off (spec §9). Every client goes through
`Online/Http/CachedFetch`: a fresh cached answer avoids the request, and a failed request falls
back to the last cached answer (shown with its age). The cache lives in
`%LocalAppData%\AstroPlannerPro\cache` and can be cleared in **Settings → Internet connection**.

Coordinates are **rounded to 2 decimals (~1 km)** before they are put in a URL (which is also the
cache key). Requests identify the app (`AstroPlannerPro/<version>` and the repository URL) and
nothing else: no accounts, cookies, analytics or user identifiers. Tests never call these
services; they use recorded answers in `tests/fixtures/online/`.

| Service | What for | What it receives | Cache | Setting | Client |
|---|---|---|---|---|---|
| [Open-Meteo](https://open-meteo.com/) `api.open-meteo.com` | Hourly clouds, dew point, wind | Site rounded to ~1 km | 1 h | Cloud forecast | `Weather/OpenMeteoForecastService` |
| [7Timer!](https://www.7timer.info/) `www.7timer.info` | Seeing and transparency | Site rounded to ~1 km | 3 h | Seeing and transparency forecast | `Weather/SevenTimerAstroService` |
| [CDS HiPS](https://aladin.cds.unistra.fr/hips/) `alasky.cds.unistra.fr`, mirror [ESA ESASky](https://sky.esa.int/) `skies.esac.esa.int` | Survey tiles for the framing preview (DSS colour, Pan-STARRS) | Sky coordinates only (HEALPix tile numbers) | Kept (tiles do not change); a failing mirror is skipped for 10 min | Survey images | `Imaging/HipsTileService` |
| [Wikipedia REST](https://en.wikipedia.org/api/rest_v1/) `en.wikipedia.org`, `commons.wikimedia.org` | Object description, image and its license | The object's name | 30 days | Wikipedia descriptions | `Info/WikipediaSummaryService` |
| [CDS Sesame](https://cds.unistra.fr/cgi-bin/Sesame) `cds.unistra.fr` (mirrors `vizier.cfa.harvard.edu`, `vizier.idia.ac.za`) | Names the catalog does not know (sky search) | The name typed | 30 days | Online name search | `Info/SesameResolver` |
| [Minor Planet Center](https://minorplanetcenter.net/) `minorplanetcenter.net` | Comet and bright-asteroid orbital elements | Nothing about the user | Comets 24 h, asteroids 30 days | Comets and asteroids | `SmallBodies/MpcSmallBodyService` |
| [CelesTrak](https://celestrak.org/) `celestrak.org` | Satellite elements (OMM, groups `visual` and `stations`) | Nothing about the user | 12 h; no retries and a 2 h pause after any error, as CelesTrak asks | Satellites | `Satellites/CelesTrakService` |
| [GitHub REST](https://docs.github.com/rest/releases) `api.github.com` | New-version notice | Nothing about the user | Once per start | Check for updates | `UI/Services/UpdateChecker` |

## Rules

- **Terms of use.** Each service is used through its public API, within its published limits and
  with the attribution it asks for (credits in the app's About window, the framing preview and the
  description panel, and in [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)). No site is
  scraped.
- **Resilience.** One named `HttpClient` per service (`UI/Services/OnlineClients`) with a 15 s
  budget, two retries with backoff and a circuit breaker; CelesTrak has its own client without
  retries.
- **Links.** Links shown in the app open only if their host is on `SafeLinks`' allow-list.
- **Nothing is downloaded and run.** The update notice only links to the GitHub release page, and
  the installer only offers to open Microsoft's WebView2 page.
- **Adding a service** needs: an `Online/*Service` using `CachedFetch`, a toggle in Settings and in
  the top-bar status tooltip, recorded fixtures for its tests, a row in this table and in the
  README's *Privacy* section, and its credit.
