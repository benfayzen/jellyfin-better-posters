# Development roadmap

## 0 — Discovery and compatibility gate

Verify official Jellyfin 12 plugin contracts/packages and btttr API behavior. Record exact version pins, sources, endpoint contract and licensing decision. If required packages are unavailable, investigate supported source-build/reference alternatives without retargeting to old Jellyfin. Report a real blocker if none work.

## 1 — First tested vertical slice

Create the solution, SDK/package pins and test project only after discovery. Implement ID validation and one verified movie request with tests, then a native plugin identity/provider that loads in disposable Jellyfin 12. Add actual restore/build/test CI.

## 2 — MVP provider

Add series, settings UI, supported overlay/source/language options, verified TMDB fallback, cancellation, HTTP failure handling and deterministic regression tests. Complete provider acceptance matrix before bulk operations.

## 3 — Safe scheduled refresh

Implement opt-in library scope, dry-run, no default triggers, bounded execution, progress and summary reasons. Prove failed fetch/cancellation cannot destroy an existing poster. Document restoration limitations.

## 4 — Delivery

Reproducible packaging, dependency/archive inspection, checksums, real Jellyfin 12 load test, manual private installation guide and changelog. No public catalog or production installation without separate owner approval.

## Bootstrap state

Only project documents, agent instructions and GitHub templates exist. No SDK was installed, no plugin code copied, no package versions guessed, no production server touched, and no development agent launched by the bootstrap task.
