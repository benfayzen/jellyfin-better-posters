# Development roadmap

## 0 — Discovery and compatibility gate (Completed)

Verified official Jellyfin 12.4 (`12.4.0`, `net10.0`, commit `83d0d0e7f76e23e2e085c8ff67d28b772d35967d`) plugin contracts/packages on `.NET SDK 10.0.403` and live `btttr.cc` upstream behavior (`/configure`, `/manifest.json`, `/poster.../imdb/poster-default/{id}.jpg`, `/meta/{movie|series}/tmdb:{id}.json`, and `x-bp-fallback: noart` redirect semantics). Recorded in [docs/COMPATIBILITY.md](COMPATIBILITY.md) and [ADR 0001](adr/0001-jellyfin12-and-btttr-baseline.md).

## 1 — First tested vertical slice (Completed)

Created `Jellyfin.Plugin.BetterPosters.sln`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, domain ID normalization and URL builder, and GitHub Actions CI workflow (`.github/workflows/ci.yml`).

## 2 — MVP provider (Completed)

Implemented `Plugin`, `PluginConfiguration`, `PluginConfigurationValidator`, embedded `configPage.html`, `BtttrClient`, `ImagePayloadValidator`, and `BetterPostersImageProvider` supporting `Movie` and `Series` (`ImageType.Primary` only) with verified TMDB fallback and comprehensive unit/HTTP contract tests.

## 3 — Safe scheduled refresh (Completed)

Implemented `BetterPostersRefreshTask` (`IScheduledTask` with zero default triggers), `PosterRefreshService`, `JellyfinPosterRefreshGateway`, and `JsonPosterRefreshStateStore` with explicit opt-in, dry-run default, library scoping, bounded item count/concurrency, download-and-validate-before-save semantics, and per-item URL fingerprint deduplication.

## 4 — Delivery (Completed)

Implemented reproducible packaging (`scripts/package-plugin.sh` producing `artifacts/better-posters_1.0.0.0.zip`), archive/dependency validation, opt-in live upstream contract test (`LiveBtttrContractTests`), and end-to-end disposable Jellyfin 12.4 loopback smoke test (`tests/integration/run-jellyfin12-smoke.sh`).
