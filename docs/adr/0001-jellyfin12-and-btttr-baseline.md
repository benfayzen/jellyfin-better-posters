# ADR 0001: Jellyfin 12.4 Baseline, Upstream Contract, and Safe Poster Replacement

## Status

Accepted (2026-11-10)

## Context

We are building a clean-room, private Better Posters plugin exclusively for Jellyfin 12.x (`v12.4`, commit `83d0d0e7f76e23e2e085c8ff67d28b772d35967d`, targeting `net10.0`). Before writing code, we needed to verify:

1. Availability of `net10.0` SDK and `Jellyfin.*` `12.4.0` NuGet packages.
2. Current `btttr.cc` poster URL construction rules, supported languages, rating sources, and TMDB fallback behavior.
3. How to guarantee that failed downloads or invalid upstream content never overwrite or delete an existing poster during remote image fetching or scheduled refresh tasks.

## Decisions

### 1. Target `net10.0` and `Jellyfin.*` `12.4.0` Only

- Pin `.NET SDK 10.0.403` (runtime `10.0.14`) in `global.json` and `Jellyfin.Controller` / `Jellyfin.Model` / `Jellyfin.Common` / `Jellyfin.Data` `12.4.0` in `Directory.Packages.props`.
- Use a brand-new plugin GUID (`4c7a9d3e-6f12-4b88-9e41-2a5d8c0f7b19`) and assembly name (`Jellyfin.Plugin.BetterPosters`).
- Mark all Jellyfin and `Microsoft.Extensions.*` package references with `ExcludeAssets="runtime"` and `PrivateAssets="all"` so the packaged plugin archive contains only `Jellyfin.Plugin.BetterPosters.dll` and `meta.json`.

### 2. Deterministic URL Construction and Two-Step Verified TMDB Fallback

- Use `https://btttr.cc/{routePrefix}/imdb/poster-default/{imdbId}.jpg` with invariant query parameter ordering (`tag`, `lang`, `rs`) as verified against `https://btttr.cc/configure`.
- Because `btttr.cc` direct poster endpoints return `HTTP 404` for `tmdb:` IDs, implement TMDB fallback via the verified Stremio metadata endpoint declared in `https://btttr.cc/manifest.json`:
  `GET https://btttr.cc/meta/{movie|series}/tmdb:{tmdbId}.json`
  Extract and validate the canonical `tt\d{7,10}` IMDb ID from `meta.poster` (cached in memory with bounded capacity and TTL), then generate the full option-aware poster URL.

### 3. Bounded, Validated HTTP Adapter and Stream-Based Image Replacement

- Enforce a strict HTTPS host allowlist (`btttr.cc` and `image.tmdb.org` only if ever redirected for static assets, or strictly `btttr.cc` for API/poster endpoints; we validate every redirect hop against an explicit HTTPS host allowlist and reject HTTP downgrades or untrusted hosts).
- Enforce per-request timeouts, cancellation token propagation, bounded response body size (max 10 MiB for images, 256 KiB for metadata JSON), bounded retry/backoff on `429 Too Many Requests` (honoring `Retry-After` up to a safe cap) and transient `5xx` errors, and magic-byte validation (`JPEG`, `PNG`, `WebP`).
- In the opt-in scheduled refresh service, always download and validate the new poster bytes into a memory stream **before** calling `IProviderManager.SaveImage(item, stream, mimeType, ImageType.Primary, 0, cancellationToken)`. If download or validation fails, or if `DryRun` is enabled, `SaveImage` is never invoked and the item's existing poster remains untouched.
- Track a per-item state fingerprint (resolved IMDb ID + effective poster URL + existing primary image path existence) in the plugin data folder so repeated scheduled runs skip items whose poster already matches the current configuration unless forced.

## Consequences

- Zero legacy maintenance burden or reflection hacks.
- Complete isolation between pure domain logic (ID validation, option validation, URL building, image magic-byte inspection) and Jellyfin/HTTP adapters.
- Safe failure modes across both interactive image selection and opt-in scheduled library refreshes.
