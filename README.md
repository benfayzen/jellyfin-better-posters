# Better Posters for Jellyfin 12

Independent Jellyfin 12.x server plugin (`Jellyfin.Plugin.BetterPosters`, GUID `4c7a9d3e-6f12-4b88-9e41-2a5d8c0f7b19`) that provides customizable primary movie and series posters from [btttr.cc](https://btttr.cc/).

**Status: Implemented and verified against Jellyfin v12.4 (`12.4.0.0`, `net10.0`).**

## Compatibility

- **Jellyfin 12.x only** (`targetAbi: 12.4.0.0`). Zero 10.x, 11.x, Emby, or legacy migration code.
- **Verified baseline:** Jellyfin `v12.4` (commit `83d0d0e7f76e23e2e085c8ff67d28b772d35967d`) on `.NET SDK 10.0.403` / `.NET 10.0.14` (`net10.0`).
- Full compatibility and upstream contract details are documented in [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md) and [ADR 0001](docs/adr/0001-jellyfin12-and-btttr-baseline.md).

## Features

### 1. MVP: Native Remote Primary-Image Provider (`IRemoteImageProvider`)

- Supports `Movie` and `Series` (`ImageType.Primary` only); all other item types (`Season`, `Episode`, `BoxSet`, `Person`, `Audio`, etc.) and image types are ignored safely.
- Validates IMDb IDs (`^tt\d{7,10}$`) and builds deterministic HTTPS poster URLs against `https://btttr.cc`.
- Verified TMDB fallback: when `EnableTmdbFallback` is enabled and an item only has a numeric TMDB ID, resolves the canonical `tt...` IMDb ID via `https://btttr.cc/meta/{movie|series}/tmdb:{id}.json` (with in-memory caching) before constructing the poster URL.
- Enforces strict outbound HTTPS (`btttr.cc` only), manual redirect inspection (immediately treating `x-bp-fallback: noart` redirects to third-party fallback hosts as `not_found`), bounded response sizes (15 MiB image / 256 KiB metadata), magic-byte validation (`JPEG`, `PNG`, `WebP`), and retry/rate-limit handling (`429 Retry-After` and `502`/`503`/`504`).

### 2. Native Admin Configuration UI (`IHasWebPages`)

Available in Jellyfin under **Dashboard -> Plugins -> Better Posters**:

| Setting | Default | Description |
| --- | --- | --- |
| `EnableTrendTags` | `true` | Show trending/award tags on posters (`tag=none` when disabled) |
| `EnableQualityTags` | `false` | Show quality badges (`q` route suffix) |
| `EnableGenre` | `true` | Show genre overlay (`g` route flag) |
| `EnableRating` | `true` | Show rating overlay (`r` route flag) |
| `EnableAgeRating` | `false` | Show age certification badge (`a` route suffix) |
| `RatingSource` | `Average` | `Average` (default), `Imdb` (`IM`), `Tmdb` (`TM`), `RottenTomatoes` (`RT`), `Metacritic` (`MC`), `Trakt` (`TR`), `Letterboxd` (`LB`), `RogerEbert` (`RE`) |
| `Language` | `English` | 20 verified `btttr.cc` languages (`en`, `de`, `fr`, `es-ES`, `es-MX`, `es`, `pt-BR`, `pt-PT`, `it`, `nl`, `pl`, `ru`, `tr`, `ar`, `ja`, `ko`, `zh`, `hi`, `sv`, `cs`) |
| `EnableTmdbFallback` | `true` | Resolve IMDb ID from TMDB ID via `btttr.cc/meta/{movie|series}/tmdb:{id}.json` when IMDb ID is absent |
| `RequestTimeoutSeconds` | `15` | Bounded per-request timeout (`3`–`60` seconds) |
| `MaxRetryAttempts` | `2` | Bounded retry count (`0`–`4`) for transient `429`/`502`/`503`/`504` responses |

### 3. Opt-In Scheduled Refresh Milestone (`IScheduledTask`)

Registered as **Update Better Posters** (`Key: JellyfinPluginBetterPostersRefresh`) under **Dashboard -> Scheduled Tasks -> Library**:

- **Zero default triggers:** Never runs automatically on installation.
- **Explicit opt-in required:** Exits immediately with zero writes unless `EnableScheduledRefreshOptIn` is enabled in plugin settings.
- **Dry-run by default:** `ScheduledRefreshDryRun` defaults to `true`, logging and counting candidate evaluations without modifying any images.
- **Explicit library scope & bounds:** Requires at least one library selected in `ScopedLibraryIds`, and enforces `MaxItemsPerRun` (`1`–`5000`, default `250`) and `MaxConcurrency` (`1`–`8`, default `2`).
- **Non-destructive replacement:** Downloads and validates the complete poster stream into memory before calling `IProviderManager.SaveImage(item, stream, mimeType, ImageType.Primary, ...)`. Any network error, missing poster, invalid image payload, or cancellation preserves the item's existing poster untouched.
- **Fingerprint deduplication:** Persists per-item URL fingerprints in `Jellyfin.Plugin.BetterPosters.RefreshState.json` so repeated runs skip items whose poster configuration and resolved ID have not changed (unless `ForceReplaceUnchanged` is enabled).

> **Important Restoration & Overwrite Notice:**
> When live scheduled replacement is enabled and saves a new primary poster, Jellyfin replaces the item's active primary image. **Disabling or uninstalling this plugin does not automatically restore previously overwritten posters.** Before enabling live scheduled refresh on a library, test with **Dry-Run Mode** enabled, back up your library metadata/artwork, or use Jellyfin's native **Edit Images** UI / library metadata refresh with another image provider to restore artwork manually.

## Private Manual Installation (Jellyfin 12.4+)

This repository is private. Do **not** configure a GitHub raw URL with embedded access tokens in Jellyfin's plugin repositories. Instead, install from the build artifact:

1. Build the plugin archive locally or download `better-posters_1.0.0.0.zip` from a private CI build:
   ```bash
   bash scripts/package-plugin.sh
   ```
2. Verify the archive contents (`Jellyfin.Plugin.BetterPosters.dll` and `meta.json` only) and SHA256 checksum printed by the packaging script.
3. Extract `artifacts/better-posters_1.0.0.0.zip` into your Jellyfin 12.4 server's plugins directory under `<JELLYFIN_DATA_DIR>/plugins/BetterPosters_1.0.0.0/`.
4. Restart Jellyfin and enable **Better Posters** in your Movie and/or Series library settings under **Image Fetchers (Primary)**.

## Development and Verification Commands

```bash
# 1. Restore and build (Release, warnings as errors)
dotnet restore Jellyfin.Plugin.BetterPosters.sln
dotnet build Jellyfin.Plugin.BetterPosters.sln --configuration Release --no-restore

# 2. Run offline unit and mocked HTTP contract tests (145 tests)
dotnet test Jellyfin.Plugin.BetterPosters.sln --configuration Release --no-build

# 3. Run opt-in live upstream contract check against https://btttr.cc
BETTER_POSTERS_RUN_LIVE_TESTS=1 dotnet test Jellyfin.Plugin.BetterPosters.sln --configuration Release --no-build --filter "FullyQualifiedName~LiveBtttrContractTests"

# 4. Build and validate the distributable plugin archive
bash scripts/package-plugin.sh

# 5. Run the disposable loopback Jellyfin 12.4 end-to-end smoke test
bash tests/integration/run-jellyfin12-smoke.sh
```

## Documentation Index

1. [AGENTS.md](AGENTS.md): binding agent workflow and safety boundaries.
2. [Compatibility & Verification Evidence](docs/COMPATIBILITY.md).
3. [ADR 0001: Jellyfin 12.4 and btttr.cc Baseline](docs/adr/0001-jellyfin12-and-btttr-baseline.md).
4. [Project Goals and Acceptance Matrix](docs/PROJECT.md).
5. [Architecture](docs/ARCHITECTURE.md).
6. [Development Roadmap](docs/ROADMAP.md).
7. [Evidence and Reference Projects](docs/REFERENCES.md).

## Licensing

All code, configuration UI, tests, and synthetic test fixtures in this repository are original implementations written for Jellyfin 12.x. Zero source files, HTML, binaries, or artwork were copied from `neurekadev/better-posters` (`GPL-3.0`) or `TheAceOfficials/BetterPoster-for-Jellyfin` (unlicensed). See [docs/REFERENCES.md](docs/REFERENCES.md) and [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md) for provenance details.
