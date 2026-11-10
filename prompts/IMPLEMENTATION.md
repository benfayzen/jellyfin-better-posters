# Development-agent handoff

Copy the assignment below into a new coding-agent session with repository access.

---

You are implementing `benfayzen/jellyfin-better-posters`, a private, independent Better Posters plugin for Jellyfin **12.x only**. Do the development work; do not stop at a plan or scaffold.

Read `AGENTS.md`, `GUIDELINES.md`, `README.md` and all documents in `docs/` before changing files. Keep all repository content, UI strings, commits and PRs in English. Work on `feat/jellyfin12-provider` or another focused feature branch and deliver a PR to `main`.

## Assignment

1. Establish a real compatibility baseline from official Jellyfin 12 sources. Bootstrap verified v12.4 and its net10.0 server target. Verify current NuGet packages and a real available .NET 10 SDK, pin them and document sources/commands in `docs/COMPATIBILITY.md`. Do not invent a package version or downgrade to Jellyfin 10.x/11.x. Re-check the btttr API contract, usage rules, supported options and IMDb/TMDB semantics. Record unsupported behavior honestly.
2. Implement a new native Jellyfin plugin with its own GUID/assembly identity, remote primary-image provider for movies and series, native configuration page, validated IDs and options, and a testable HTTP/URL-building layer. Use the product defaults and acceptance criteria in `docs/PROJECT.md`.
3. Add verified overlay toggles, rating sources and languages; implement IMDb-first and TMDB fallback only where current endpoint behavior has been confirmed. Do not fake unsupported variants or add token-based watched-progress features.
4. Handle not-found, rate limits, invalid images, bounded sizes, timeouts, cancellation, redirects and transient failures safely. Preserve current posters and let other providers work when this provider has no usable result.
5. Once the MVP is verified, implement the separate opt-in scheduled refresh milestone: no default triggers, explicit library scope, dry-run, progress/cancellation, bounded processing and outcome summaries. Never remove an existing poster before the replacement is valid. Test zero writes in dry-run and old-image retention on failure. Document recovery limitations.
6. Add meaningful offline unit and HTTP tests, disposable Jellyfin 12 integration tests, real restore/build/test CI, reproducible packaging and private manual-install documentation. Check archive contents and avoid bundling Jellyfin host assemblies. Do not create a fictitious catalog entry or publish an artifact without a corresponding successful build and load test.

## Boundaries

- No old-version compatibility, no copying old projects wholesale, no reference GUID reuse.
- No production Jellyfin access, service restarts, installs, library refreshes or poster replacement without separate owner authorization. Use a disposable instance with synthetic/licensed fixtures.
- Keep the GitHub repository private; do not publish a catalog or expose tokens in URLs.
- Original-code license is pending. Do not import GPL code without a compatible licensing decision; do not copy the reference with no established license or its binaries/assets.
- Resolve ordinary implementation problems and run the checks yourself. If a hard dependency is unavailable, investigate a supported alternative and document the exact blocker rather than fabricating test output or silently retargeting.

## Completion report

Open a focused PR with the implementation, updated documents and actual verification results. Report exact SDK/package/server versions; commands run and results; tests covered; actual Jellyfin plugin-load and settings/provider smoke-test evidence; packaging details; and unresolved limitations. Distinguish MVP from scheduled-refresh completion and mocked tests from live checks. Never say compatible, green, installed or finished based solely on a scaffold or successful compile.
