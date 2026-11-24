# Contributing to AstroPlanner Pro

Thanks for your interest! Issues and pull requests are welcome.

## Before you start

- Read [docs/architecture.md](docs/architecture.md) and the other notes in [docs/](docs).
- For anything larger than a fix, open an issue first so we can agree on the approach.
- By contributing you agree that your work is licensed under the [MIT license](LICENSE).

## Build and test

Requires Windows, the .NET 10 SDK and Node.js 22.12+.

```bash
dotnet build AstroPlanner.slnx -c Release -warnaserror   # must stay at 0 warnings
dotnet test AstroPlanner.slnx -c Release                 # all tests, no network
cd src/web && npm ci && npm run lint && npm test
dotnet run --project tools/AstroPlanner.BridgeGen     # after changing a bridge contract (docs/bridge.md)
cd src/web && npm run e2e -w @astroplanner/planetarium  # planetarium UI E2E with a mock host (docs/ui.md)
dotnet run --project src/AstroPlanner.UI                 # run the app
```

## Rules

- **Everything in English**: UI, identifiers, comments, docs, commit messages. UI text goes in
  `src/AstroPlanner.Application/Resources/Strings.resx`, never as literals.
- **Layers**: all astronomy lives in `AstroPlanner.Core` (BCL only); see the dependency rules in
  `ArchitectureTests`. The renderer draws; it never computes positions.
- **Tests**: every new algorithm is checked against an authoritative reference (a Meeus worked
  example or a committed JPL Horizons fixture). Never loosen a reference tolerance without
  explaining why. Tests must not use the network or the real `%LocalAppData%`.
- **Style**: file-scoped namespaces, `sealed` by default, records for immutable models, async IO
  with `CancellationToken`, units in names (`…Degrees`, `…Arcsec`, `…Mm`), an `IFormatProvider`
  everywhere. Package versions are pinned in `Directory.Packages.props`.
- **UI**: brushes through `DynamicResource` (both themes), an accessible name on every control,
  WCAG AA contrast — `AccessibilityTests` checks all three.
- **Data**: catalog changes go through the DataBuilder ([docs/data-pipeline.md](docs/data-pipeline.md))
  with `data/SOURCES.md` updated; `app.db` changes are a new migration, never an edit of an old one.
- **Privacy**: no new external service without a toggle, a cache, recorded test fixtures and an
  entry in [docs/external-services.md](docs/external-services.md). Coordinates sent anywhere are
  rounded to 2 decimals.
- **Licenses**: MIT-compatible dependencies only — no GPL/AGPL (e.g. PDFsharp stays at 6.2.x).
- **Commits**: [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`,
  `docs:`…), small and focused. Update `CHANGELOG.md` for user-visible changes.

## Pull requests

CI must be green (build with warnings as errors, all tests, the renderer's lint and tests, the
data verification and the 85 % Core coverage gate). Describe what changed and how you checked it;
add a screenshot for UI changes.
