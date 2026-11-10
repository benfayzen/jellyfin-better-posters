# Engineering guidelines

## Structure

Current repository contains planning documents and templates only. During implementation, prefer:

- `src/BetterPosters/`: plugin, configuration UI, Jellyfin adapters and application services.
- `tests/BetterPosters.Tests/`: deterministic unit and HTTP contract tests.
- `tests/integration/`: disposable Jellyfin 12 harness and licensed/synthetic fixtures.
- `docs/`: architecture, requirements, compatibility evidence, operations and decisions.
- `prompts/`: agent handoffs.
- `scripts/`: reproducible build/package/validation helpers when needed.
- `.github/workflows/`: CI once real project files and tests exist.

Do not create empty projects, fake tests or nonfunctional release workflows merely to fill the tree.

## Implementation standards

C# with nullable reference types, asynchronous cancellable I/O, dependency injection, deterministic URL construction and validated typed configuration. Use invariant serialization of URL values and supported enums. Avoid global mutable state, blocking async calls and new HTTP clients per request. Keep dependencies minimal and do not bundle Jellyfin host assemblies into the plugin archive.

Use scoped structured logging with reason codes and no secrets. Configuration defaults must be documented and round-trip through the native settings page. Library operations need item-level error isolation, cancellation, bounded concurrency and a final success/skip/failure summary.

## Pull requests

Use focused conventional commits. Include behavior changes, verification commands and actual results, target server/package versions, upstream API evidence, licensing implications and remaining limitations. No auto-deployment or production secrets in CI. Prefer read-only GitHub Actions permissions and pinned action revisions when adding workflows.

## Packaging

Package only plugin-owned runtime dependencies and metadata actually required by the verified Jellyfin 12 loader. Use a stable new GUID and reproducible versioning; verify archive contents, checksums and target ABI. Do not publish a catalog version until its corresponding downloadable artifact exists and was load-tested. Private GitHub release assets require authentication; document authenticated manual installation rather than embedding a token.
