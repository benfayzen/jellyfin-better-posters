# Proposed architecture

This is a design, not an implemented system. Confirm details against actual Jellyfin 12 interfaces before coding.

## Boundaries

`Native configuration -> validated poster options -> ID selection and URL builder -> btttr HTTP adapter -> Jellyfin remote image provider`

Optional refresh task reuses the same services and delegates image storage to verified Jellyfin APIs, not direct filesystem overwrites.

- **Plugin composition:** new identity, native service registration, settings page.
- **Domain:** supported item/ID model, option enums, deterministic request construction; no network or Jellyfin dependency where practical.
- **HTTP adapter:** injected client, fixed HTTPS upstream, redirect checks, timeout/cancellation, rate-limit handling and streamed size limits. Determine image validation approach before buffering large responses.
- **Provider:** maps Jellyfin Movie/Series and provider IDs to candidates; uses Jellyfin-supported ordering and caching; never forces priority through unrelated metadata mutation.
- **Refresh service:** explicit scope, dry-run, bounded concurrency, deduplication/cache policy, item-level isolation and summary counts. No schedule on install.

## Decisions to validate first

1. Exact Jellyfin 12 NuGet package availability and target ABI; the server source targeting net10.0 is evidence, not proof that every plugin package exists under tag-like version strings.
2. Native plugin configuration UI and remote image download contract.
3. Current btttr endpoint, supported flags, languages, ratings and TMDB mapping. Do not assume legacy query parameters still work.
4. How caching keys vary with ID, item type, language, overlays and rating source; no tokens in keys/logs.
5. How to preserve an existing image when a replacement fails and avoid rewriting unchanged content.
6. Disposable Jellyfin 12 integration image/artifact availability and licensed fixture strategy.

Record decisions and evidence in docs/COMPATIBILITY.md and focused ADRs during implementation. Do not claim unresolved assumptions as facts.
