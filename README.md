# Better Posters for Jellyfin 12

Private planning repository for a new, independent Jellyfin server plugin that provides customizable movie and series posters from [btttr.cc](https://btttr.cc/).

**Status: specification and agent handoff only. No plugin has been implemented, built, installed, or tested.**

## Compatibility

- Jellyfin **12.x only**. No 10.x, 11.x, Emby, or legacy migration support.
- Initial development and integration baseline: **Jellyfin v12.4**, the latest stable release observed on 2026-11-10. This is not a claim of support for every 12.x minor.
- Official v12.4 server source targets `net10.0`. Confirm plugin package versions and runtime requirements before creating project files; do not reuse the old examples' net8.0/net9.0 settings.

## Start here

1. [AGENTS.md](AGENTS.md): binding agent workflow and boundaries.
2. [Project goals and acceptance criteria](docs/PROJECT.md).
3. [Architecture](docs/ARCHITECTURE.md).
4. [Development milestones](docs/ROADMAP.md).
5. [Evidence and reference projects](docs/REFERENCES.md).
6. [Development-agent handoff prompt](prompts/IMPLEMENTATION.md).

The repository and all code, UI strings, documentation, issues, and commits are maintained in English. Owner conversations may be in German.

## Intended features

Native remote primary-image provider for movies and series; configurable overlays, ratings source, and language; validated IMDb lookup with TMDB fallback only if verified upstream; optional, explicitly enabled scheduled updates with dry-run and non-destructive failure handling.

No running Jellyfin instance or existing poster library is to be modified during implementation without separate owner authorization.

## Distribution and licensing

This repository is private. Authenticated manual artifact download is the initial distribution path; a private GitHub raw manifest is **not** an anonymously usable Jellyfin plugin repository. Do not expose the repository or place access tokens in manifest URLs.

No third-party implementation or assets have been imported. Licensing for original code is intentionally pending; this is not a grant of reuse rights. Review [reference licensing](docs/REFERENCES.md) before importing anything. No downloadable release or catalog manifest exists yet.
