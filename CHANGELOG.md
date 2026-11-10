# Changelog

## 1.0.0.0 (2026-11-10)

### MVP: Remote Primary-Image Provider & Configuration

- Target Jellyfin `v12.4` (`12.4.0`, `targetAbi: 12.4.0.0`, `net10.0`) on `.NET SDK 10.0.403` with new plugin identity (`Jellyfin.Plugin.BetterPosters`, GUID `4c7a9d3e-6f12-4b88-9e41-2a5d8c0f7b19`).
- Add pure domain layer (`ProviderIdNormalizer`, `BtttrUrlBuilder`, `PosterRequestOptions`, `RatingSourceOption`, `PosterLanguageOption`) supporting all 16 `btttr.cc` route prefixes (`poster`..`poster-nqa`), 8 rating sources, and 20 languages.
- Add `BtttrClient` and `ImagePayloadValidator` enforcing HTTPS-only `btttr.cc` requests, manual redirect validation (`x-bp-fallback: noart` handled as `not_found` without following third-party redirects), bounded timeouts and retry/backoff (`429 Retry-After` and `502`/`503`/`504`), streamed byte caps (`15 MiB` images / `256 KiB` metadata), and `JPEG`/`PNG`/`WebP` magic-byte checks.
- Add verified TMDB-to-IMDb fallback resolution via `https://btttr.cc/meta/{movie|series}/tmdb:{id}.json` with thread-safe in-memory caching.
- Add `BetterPostersImageProvider` (`IRemoteImageProvider`, `IHasOrder`) for `Movie` and `Series` (`ImageType.Primary` only).
- Add `PluginConfiguration` (`ValidateOrThrow`) and embedded Jellyfin admin configuration page (`configPage.html`).

### Packaging, CI, and Verification

- Add `scripts/package-plugin.sh` producing `artifacts/better-posters_1.0.0.0.zip` (`Jellyfin.Plugin.BetterPosters.dll` and `meta.json` only).
- Add 133 offline unit and mocked HTTP contract tests, 1 opt-in live `btttr.cc` contract test (`BETTER_POSTERS_RUN_LIVE_TESTS=1`), and an automated disposable loopback Jellyfin 12.4 server smoke test (`tests/integration/run-jellyfin12-smoke.sh`).
- Add GitHub Actions CI workflow (`.github/workflows/ci.yml`).
