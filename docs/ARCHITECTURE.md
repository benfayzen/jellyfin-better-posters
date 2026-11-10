# Architecture

Implemented and verified against **Jellyfin v12.4** (`net10.0`). See [docs/COMPATIBILITY.md](COMPATIBILITY.md) and [ADR 0001](adr/0001-jellyfin12-and-btttr-baseline.md) for baseline verification records.

## Boundaries

`PluginConfiguration -> PosterOptions -> ProviderIdNormalizer & BtttrUrlBuilder -> BtttrClient -> BetterPostersImageProvider`

- **Plugin Composition (`src/BetterPosters/Plugin.cs`, `PluginServiceRegistrator.cs`):**
  - Distinct plugin identity (`Jellyfin.Plugin.BetterPosters`, GUID `4c7a9d3e-6f12-4b88-9e41-2a5d8c0f7b19`).
  - Implements `BasePlugin<PluginConfiguration>` and `IHasWebPages` serving the embedded `Configuration/configPage.html`.
  - Registers `Func<PluginConfiguration>`, `IBtttrClient` (`HttpClient` with `SocketsHttpHandler` configured with `AllowAutoRedirect = false`), and `IRemoteImageProvider` via `IPluginServiceRegistrator`.
- **Domain (`src/BetterPosters/Domain/`):**
  - Pure, network-free domain model: `PosterMediaType`, `PosterRatingSource`, `PosterLanguage`, `PosterOptions`, `ProviderIdNormalizer`, and `BtttrUrlBuilder`.
  - Validates IMDb IDs (`^tt\d{7,10}$`) and numeric TMDB IDs (`^\d{1,12}$`).
  - Constructs deterministic `https://btttr.cc/{routePrefix}/imdb/poster-default/{imdbId}.jpg` URLs and `/meta/{movie|series}/tmdb:{tmdbId}.json` metadata lookup URLs.
- **HTTP Adapter (`src/BetterPosters/Http/`):**
  - `BtttrClient` enforces fixed origin `https://btttr.cc`, manual redirect validation (up to 3 same-origin HTTPS hops; detects `302 Found` with `x-bp-fallback: noart` as `not_found` without following third-party redirects), bounded timeouts (`3`–`60`s), bounded retries (`0`–`5`) with `Retry-After` support on `429`/`502`/`503`/`504`, streamed byte caps (`15 MiB` for posters, `256 KiB` for metadata), `ImagePayloadValidator` magic-byte verification (`JPEG`, `PNG`, `WebP`), and thread-safe in-memory TMDB-to-IMDb resolution caching.
- **Remote Image Provider (`src/BetterPosters/Providers/BetterPostersImageProvider.cs`):**
  - Implements `IRemoteImageProvider` and `IHasOrder` (`Order => 0`).
  - Supports `Movie` and `Series` (`ImageType.Primary` only); ignores all other item and image types.
  - Uses IMDb ID directly when valid, or resolves via `BtttrClient.ResolveImdbIdFromTmdbAsync` when `EnableTmdbFallback` is enabled.
