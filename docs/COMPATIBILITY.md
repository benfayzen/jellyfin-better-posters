# Compatibility and Upstream Verification

Verified on **2026-11-10** prior to plugin implementation.

## 1. Jellyfin 12 Server Baseline

- **Release Tag:** [`v12.4`](https://github.com/jellyfin/jellyfin/releases/tag/v12.4) (published `2026-10-26T21:30:17Z`)
- **Commit SHA:** `83d0d0e7f76e23e2e085c8ff67d28b772d35967d`
- **Target Framework:** `net10.0` (verified in `Jellyfin.Server/Jellyfin.Server.csproj` and NuGet `.nuspec` metadata)
- **Target ABI:** `12.4.0.0`
- **Official Portable Server Archive (for disposable loopback smoke tests):**
  `https://repo.jellyfin.org/files/server/linux/stable/v12.4/amd64/jellyfin_12.4-amd64.tar.xz`

No Jellyfin 10.x or 11.x compatibility shims, multi-targeting, or reflection fallbacks are included.

## 2. .NET SDK and Runtime

Verified against Microsoft's official `.NET 10.0` release metadata (`https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json`):

- **.NET SDK Version:** `10.0.403` (commit `5a2162bea6`, MSBuild `18.9.15+5a2162bea`)
- **.NET Runtime / ASP.NET Core Runtime:** `10.0.14` (commit `0427b9abbe`)
- **Pinning:** `global.json` pins SDK `10.0.403` with `rollForward: latestFeature`.

### Verification Command

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --version 10.0.403 --install-dir "$HOME/.dotnet"
"$HOME/.dotnet/dotnet" --info
```

## 3. Official NuGet Packages

All required Jellyfin 12.4 packages were verified on `https://api.nuget.org/v3/index.json` and inspected via `.nuspec` metadata to confirm they were built from commit `83d0d0e7f76e23e2e085c8ff67d28b772d35967d` targeting `net10.0`:

| Package | Version | Target Framework | Notes |
| --- | --- | --- | --- |
| `Jellyfin.Controller` | `12.4.0` | `net10.0` | Host assembly (`ExcludeAssets="runtime"`, `PrivateAssets="all"`) |
| `Jellyfin.Model` | `12.4.0` | `net10.0` | Host assembly (`ExcludeAssets="runtime"`, `PrivateAssets="all"`) |
| `Jellyfin.Common` | `12.4.0` | `net10.0` | Host assembly (`ExcludeAssets="runtime"`, `PrivateAssets="all"`) |
| `Jellyfin.Data` | `12.4.0` | `net10.0` | Host assembly (`ExcludeAssets="runtime"`, `PrivateAssets="all"`) |
| `Microsoft.Extensions.Http` | `10.0.14` | `net10.0` | Host-aligned DI/HttpClientFactory abstractions (`ExcludeAssets="runtime"`, `PrivateAssets="all"`) |
| `Microsoft.NET.Test.Sdk` | `18.0.1` / `17.13.0` | `net10.0` | Test project only |
| `xunit` | `2.9.3` | `net10.0` | Test project only |
| `xunit.runner.visualstudio` | `3.0.2` | `net10.0` | Test project only |

## 4. Verified Jellyfin 12.4 Host Contracts

Checked directly against `jellyfin/jellyfin` commit `83d0d0e7f76e23e2e085c8ff67d28b772d35967d`:

- **Plugin Identity & Configuration:**
  - `MediaBrowser.Common.Plugins.BasePlugin<TConfigurationType>` (`MediaBrowser.Common/Plugins/BasePluginOfT.cs`)
  - `MediaBrowser.Model.Plugins.BasePluginConfiguration`
  - `MediaBrowser.Model.Plugins.IHasWebPages` and `PluginPageInfo`
  - `Emby.Server.Implementations.Plugins.PluginManager` loads `meta.json` (`MediaBrowser.Common.Plugins.PluginManifest`) from `<pluginsPath>/<Name>_<Version>/meta.json`, checks `_appVersion >= targetAbi`, and validates the `assemblies` whitelist.
- **Service Registration:**
  - `MediaBrowser.Controller.Plugins.IPluginServiceRegistrator.RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)`
- **Remote Image Provider:**
  - `MediaBrowser.Controller.Providers.IRemoteImageProvider` (`Supports(BaseItem)`, `GetSupportedImages(BaseItem)`, `GetImages(BaseItem, CancellationToken)`, `GetImageResponse(string, CancellationToken)`)
  - `MediaBrowser.Controller.Providers.IHasOrder` (`Order => 0`)
- **Scheduled Task & Safe Image Saving:**
  - `MediaBrowser.Model.Tasks.IScheduledTask` (`Name`, `Key`, `Description`, `Category`, `ExecuteAsync(IProgress<double>, CancellationToken)`, `GetDefaultTriggers()`)
  - `MediaBrowser.Controller.Providers.IProviderManager.SaveImage(BaseItem item, Stream source, string mimeType, ImageType type, int? imageIndex, CancellationToken cancellationToken)` — verified in `MediaBrowser.Providers/Manager/ProviderManager.cs`. Passing an already-downloaded and validated `Stream` ensures Jellyfin never overwrites an existing image if the upstream download or image validation fails.
  - `MediaBrowser.Controller.BaseItemManager.IBaseItemManager.IsImageFetcherEnabled(BaseItem baseItem, TypeOptions? libraryTypeOptions, string name)`

## 5. Verified `btttr.cc` Upstream Contract

Checked live against `https://btttr.cc/`, `https://btttr.cc/configure`, and `https://btttr.cc/manifest.json` on **2026-11-10**:

### 5.1 Transport and Headers

- **Allowed Origin:** `https://btttr.cc` (HTTPS only).
- **User-Agent Requirement:** Requests with default scripting User-Agents (such as `Python-urllib`) are rejected with `HTTP 403 Forbidden` by Cloudflare. Requests with an explicit application `User-Agent` (e.g., `Jellyfin-BetterPosters/1.0`) succeed with `HTTP 200 OK`.

### 5.2 Deterministic Poster Image Endpoint

`btttr.cc/configure` (`updateAioUrl()`) defines the external poster URL pattern:

```text
https://btttr.cc/{routePrefix}/imdb/poster-default/{imdbId}.jpg[?{query}]
```

- **`{routePrefix}` construction:**
  - `EnableGenre && EnableRating` -> `poster`
  - `EnableGenre && !EnableRating` -> `poster-g`
  - `!EnableGenre && EnableRating` -> `poster-r`
  - `!EnableGenre && !EnableRating` -> `poster-n`
  - Suffix flags: append `q` if `EnableQualityTags`, append `a` if `EnableAgeRating` (using `-` separator when the base prefix is `poster`, producing `poster-q`, `poster-a`, `poster-qa`, `poster-gq`, `poster-ga`, `poster-gqa`, `poster-rq`, `poster-ra`, `poster-rqa`, `poster-nq`, `poster-na`, `poster-nqa`).
- **Query parameters (in deterministic order):**
  1. `tag=none` when `EnableTrendTags == false` (omitted when `true`).
  2. `lang={code}` when `Language != English`.
  3. `rs={code}` when `EnableRating == true` and `RatingSource != Average`.
- **Supported Rating Sources (`rs`):**
  - `Average` (`avg`, omitted by default)
  - `Imdb` (`IM`)
  - `Tmdb` (`TM`)
  - `RottenTomatoes` (`RT`)
  - `Metacritic` (`MC`)
  - `Trakt` (`TR`)
  - `Letterboxd` (`LB`)
  - `RogerEbert` (`RE`)
- **Supported Languages (`lang`):**
  Verified in `btttr.cc/configure` and via live `HEAD` requests (`200 OK`, `image/webp`):
  `en` (default, omitted), `es-ES`, `es-MX`, `es`, `fr`, `de`, `pt-BR`, `pt-PT`, `it`, `nl`, `pl`, `ru`, `tr`, `ar`, `ja`, `ko`, `zh`, `hi`, `sv`, `cs`.
- **Response Content-Type & Magic Bytes:**
  Live responses return `HTTP 200` with `Content-Type: image/webp` (RIFF/WEBP magic bytes `52 49 46 46 ... 57 45 42 50`) even though the path ends in `.jpg`.
- **Missing Artwork / Unknown IMDb IDs (`x-bp-fallback: noart`):**
  When `btttr.cc` has no poster for an IMDb ID (e.g., `tt999999999`), it returns `HTTP 302 Found` with header `x-bp-fallback: noart` and `Location: https://images.metahub.space/poster/medium/<id>/img`. With `AllowAutoRedirect = false`, `BtttrClient` inspects `x-bp-fallback: noart` and immediately classifies the result as `not_found` without following the third-party redirect to `images.metahub.space`. Any other off-host or non-HTTPS redirect is rejected with `redirect_rejected`.

### 5.3 IMDb ID Validation and TMDB Fallback Semantics

- **IMDb IDs:** Must match `^tt\d{7,10}$` (trimmed, case-normalized to lowercase `tt`).
- **Direct TMDB Poster Routes:** Direct poster URLs (`/poster/imdb/poster-default/tmdb:27205.jpg`, `/poster/tmdb/poster-default/27205.jpg`, `/poster/movie/tmdb:27205/auto~gr.png`) return `HTTP 404`.
- **Verified TMDB Fallback via Official Stremio `meta` Endpoint:**
  `https://btttr.cc/manifest.json` explicitly declares:
  - `"resources": ["catalog", "meta"]`
  - `"types": ["movie", "series"]`
  - `"idPrefixes": ["tt", "tmdb:"]`
  Querying `GET https://btttr.cc/meta/{movie|series}/tmdb:{tmdbId}.json` for a numeric TMDB ID returns JSON containing `meta.poster` with the resolved `tt...` IMDb ID (for example, `movie` `tmdb:27205` -> `https://btttr.cc/poster/movie/tt1375666/Oscar%20Nominee.png` -> `tt1375666`; `series` `tmdb:1396` -> `https://btttr.cc/poster/series/tt0903747/Emmy%20Winner.png` -> `tt0903747`; unknown `tmdb:999999999` -> `{"meta":null}`).
  When `EnableTmdbFallback` is enabled and an item has no valid IMDb ID, the plugin resolves the `tt...` IMDb ID via this verified `/meta/{movie|series}/tmdb:{id}.json` endpoint (with caching and bounded size/timeouts) and then builds the deterministic poster URL with all configured options.

### 5.4 Explicitly Deferred Upstream Features

- Token-authenticated personal watch-progress overlays (`Trakt` / `PublicMetaDB` / `MDBList`) are out of scope for the MVP and are not implemented.

## 6. Licensing and Provenance Record

- **Reference Projects:** `neurekadev/better-posters` is `GPL-3.0`; `TheAceOfficials/BetterPoster-for-Jellyfin` has no repository license.
- **Decision:** Zero source files, HTML markup, binaries, or images were copied from either reference repository. All code, UI, tests, and synthetic image fixtures in this repository were written from scratch against the verified Jellyfin 12.4 API and `btttr.cc` upstream contract.
- **Host Packages:** `Jellyfin.*` NuGet packages are licensed under `GPL-3.0-only` by Jellyfin Contributors and are referenced at compile time without redistributing host binaries in the plugin archive.

## 7. Packaging and Archive Verification

- **Packaging Script:** `bash scripts/package-plugin.sh`
- **Output Archive:** `artifacts/better-posters_1.0.0.0.zip`
- **Archive Contents:** Exclusively `Jellyfin.Plugin.BetterPosters.dll` (with embedded `configPage.html`) and `meta.json` (`targetAbi: "12.4.0.0"`, `guid: "4c7a9d3e-6f12-4b88-9e41-2a5d8c0f7b19"`). Zero host `Jellyfin.*`, `MediaBrowser.*`, or `Microsoft.Extensions.*` assemblies are bundled.
- **Verified Checksums (`1.0.0.0` build):**
  - `SHA256`: `3b5183bfc261f13cd7a5237a729cf41d8e9fa2a4430247214c9a129d35f072d7`
  - `MD5`: `8567ec76c0a16bf513d2993c3e3b2b0b`

## 8. Executed Verification Evidence

All four verification tiers were executed and verified on **2026-11-10**:

1. **Offline Unit & Mocked HTTP Contract Tests (`dotnet test`):**
   - Command: `dotnet test Jellyfin.Plugin.BetterPosters.sln --configuration Release`
   - Result: **149 passed, 0 failed, 0 warnings** (1 opt-in live test skipped by default).
   - Coverage: IMDb/TMDB ID normalization, deterministic URL construction across all 16 route prefixes (`poster`..`poster-nqa`), 8 rating sources, 20 languages, configuration validation, `BtttrClient` mocked HTTP status/retry/rate-limit/redirect/`x-bp-fallback: noart`/magic-byte/size-limit checks, `BetterPostersImageProvider` Movie/Series filtering and TMDB fallback, and `PosterRefreshService` opt-in/dry-run/scope/non-destructive replacement/fingerprint state persistence.
2. **Opt-In Live Upstream Contract Check (`BETTER_POSTERS_RUN_LIVE_TESTS=1`):**
   - Command: `BETTER_POSTERS_RUN_LIVE_TESTS=1 dotnet test Jellyfin.Plugin.BetterPosters.sln --filter "FullyQualifiedName~LiveBtttrContractTests"`
   - Result: **1 passed, 0 failed**.
   - Verified against live `https://btttr.cc`: TMDB `27205` -> IMDb `tt1375666` via `/meta/movie/tmdb:27205.json`, live WebP poster download and magic-byte validation for `tt1375666`, and `not_found` classification on `tt999999999` (`302 Found` with `x-bp-fallback: noart`).
3. **Disposable Jellyfin 12.4.0 Loopback Server Smoke Test (`tests/integration/run-jellyfin12-smoke.sh`):**
   - Command: `bash tests/integration/run-jellyfin12-smoke.sh`
   - Server Binary: Official `jellyfin_12.4-amd64.tar.xz` (`Version: 12.4.0`) bound strictly to `127.0.0.1:18096` in a temporary directory (`/tmp/jellyfin12-smoke.*`) with synthetic `.nfo`/`.strm` fixtures.
   - Verified Outcomes:
     - Plugin discovered and loaded with `Status: Active` (`Id: 4c7a9d3e6f124b889e412a5d8c0f7b19`, `Version: 1.0.0.0`).
     - Embedded configuration HTML page served at `/web/ConfigurationPage?name=Better%20Posters`.
     - Default configuration retrieved, modified, saved, and round-tripped via `/Plugins/{pluginId}/Configuration`.
     - Scheduled task `JellyfinPluginBetterPostersRefresh` (`Update Better Posters`) registered with `0` default triggers.
     - `IRemoteImageProvider` returned the expected `btttr.cc` candidate URL for both an IMDb-tagged movie (`tt1375666`) and a TMDB-only movie (`157336` -> `tt0816692`).
     - Scheduled refresh in dry-run mode completed with zero image writes.
     - Scheduled refresh in opt-in live mode downloaded, validated, and saved the primary poster (`ImageTags.Primary`) and persisted item fingerprints to `Jellyfin.Plugin.BetterPosters.RefreshState.json`.
