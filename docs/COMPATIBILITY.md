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
  Live responses return `HTTP 200` with `Content-Type: image/webp` (RIFF/WEBP magic bytes `52 49 46 46 ... 57 45 42 50`) even though the path ends in `.jpg`. Unknown IMDb IDs (e.g., `tt999999999`) return `HTTP 404` with `text/plain; charset=utf-8`.

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
