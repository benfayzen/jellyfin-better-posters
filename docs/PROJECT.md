# Project goals and acceptance criteria

## Goal

Develop a maintainable, independent Better Posters server plugin for Jellyfin 12, using btttr.cc for customizable primary posters. Older Jellyfin versions are explicitly out of scope.

## MVP

1. Native remote primary-image provider for Movie and Series only; unsupported item types/image types are ignored safely.
2. Validate IMDb IDs and construct deterministic HTTPS poster requests. TMDB fallback is enabled only once movie/series endpoint semantics have been verified; otherwise document it as unavailable, not guessed.
3. Native admin configuration for trend tags, quality tags, genre, ratings, age rating, rating source and language. Supported values must be checked against current upstream behavior.
4. Intended defaults: trend on, quality off, genre on, ratings on, age rating off, average rating source, English. Reject invalid settings and preserve safe existing configuration.
5. No secrets required for baseline behavior. Token-authenticated personal watched-progress overlays are deferred.
6. Safe HTTP behavior: timeouts, cancellation, bounded retry/backoff respecting rate limits, upstream host/redirect validation, image type/size validation and bounded resource consumption.
7. Gracefully return no candidate on absent IDs, not-found, upstream failures or invalid images, without corrupting existing images or suppressing other providers.

## Next milestone: opt-in refresh

An unscheduled-by-default task can update selected movie/series libraries with the current configuration. Require explicit opt-in, dry-run, progress, cancellation and per-item outcome reasons. Download and validate before replacement using verified Jellyfin APIs. Repeated runs must not produce unnecessary traffic or rewrites. Explain that disabling this plugin does not automatically restore a previously replaced poster; document backup/restore or native manual restoration before enabling bulk operation.

## Acceptance matrix

| Area | Required evidence | Verified status |
| --- | --- | --- |
| Compatibility | Exact Jellyfin 12 tag, SDK and package versions recorded; no old-version paths | Verified against Jellyfin `v12.4` (`12.4.0`, `net10.0`) on `.NET SDK 10.0.403` ([docs/COMPATIBILITY.md](COMPATIBILITY.md)) |
| Provider | Movie/series candidates work in disposable Jellyfin; unsupported types do not | Verified in `BetterPostersImageProviderTests` and disposable Jellyfin 12.4 smoke test |
| Configuration | Native page loads, saves, persists and changes generated requests | Verified in `PluginTests`, `PluginConfigurationValidatorTests`, and disposable Jellyfin 12.4 smoke test |
| URL generation | Deterministic tests for valid/invalid IDs, flags, languages, sources and encoding | Verified in `BtttrUrlBuilderTests` and `ProviderIdNormalizerTests` |
| HTTP | Tests for 404, 429, 5xx, timeout, cancellation, redirects, oversized/non-image content | Verified in `BtttrClientTests` and `ImagePayloadValidatorTests` |
| Fallback | Verified upstream evidence and tests, or explicit deferral with no invented URLs | Verified upstream `/meta/{movie|series}/tmdb:{id}.json` resolution in `BtttrClientTests`, `LiveBtttrContractTests`, and Jellyfin 12.4 smoke test |
| Isolation | Unit tests offline; live tests opt-in; no real library access | Verified: 149 offline tests run with zero external network; 1 live test gated by `BETTER_POSTERS_RUN_LIVE_TESTS=1` |
| Task | Dry-run causes zero writes; scope and opt-in enforced; failure retains old image | Verified in `PosterRefreshServiceTests`, `JsonPosterRefreshStateStoreTests`, and disposable Jellyfin 12.4 smoke test |
| Packaging | Restore/build/tests pass; archive checked; actual Jellyfin 12 load verified | Verified via `scripts/package-plugin.sh` (`artifacts/better-posters_1.0.0.0.zip`) and `tests/integration/run-jellyfin12-smoke.sh` |
| Documentation | Setup, settings, limits, privacy, licensing and real verification results | Documented in `README.md`, `docs/COMPATIBILITY.md`, `docs/ARCHITECTURE.md`, and `CHANGELOG.md` |

MVP and refresh milestone must be reported separately. A provider-only delivery is not full completion of scheduled-refresh requirements.

## Non-goals

Jellyfin 10.x/11.x support, Emby/Plex, local overlay renderer, metadata scraping, a separate web service, production deployment, copying reference binaries/assets, external catalog publication, or importing watched-progress tokens.
