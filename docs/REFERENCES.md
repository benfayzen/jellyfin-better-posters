# Reference sources and provenance

Checked through GitHub API/source on 2026-11-10. Re-verify before implementation.

## Official compatibility evidence

- [Jellyfin v12.4 release](https://github.com/jellyfin/jellyfin/releases/tag/v12.4), published 2026-10-26.
- Pinned server commit: `83d0d0e7f76e23e2e085c8ff67d28b772d35967d`.
- [Server project](https://github.com/jellyfin/jellyfin/blob/83d0d0e7f76e23e2e085c8ff67d28b772d35967d/Jellyfin.Server/Jellyfin.Server.csproj): `TargetFramework` is `net10.0`.
- [SDK declaration](https://github.com/jellyfin/jellyfin/blob/83d0d0e7f76e23e2e085c8ff67d28b772d35967d/global.json): version `10.0.0`, rollForward `latestMinor`. This upstream declaration is not a recommended downloadable SDK version; select and verify an available .NET 10 SDK.
- [Remote image interface](https://github.com/jellyfin/jellyfin/blob/83d0d0e7f76e23e2e085c8ff67d28b772d35967d/MediaBrowser.Controller/Providers/IRemoteImageProvider.cs).
- [Plugin base](https://github.com/jellyfin/jellyfin/blob/83d0d0e7f76e23e2e085c8ff67d28b772d35967d/MediaBrowser.Common/Plugins/BasePlugin.cs).
- [Service registration](https://github.com/jellyfin/jellyfin/blob/83d0d0e7f76e23e2e085c8ff67d28b772d35967d/MediaBrowser.Controller/Plugins/IPluginServiceRegistrator.cs).

Package restore, SDK execution, btttr endpoint behavior and actual Jellyfin loading were **not** tested during planning.

## Behavioral references — not dependencies

### neurekadev/better-posters

- https://github.com/neurekadev/better-posters
- Observed master commit: `e30e086fd626b91ab8b2457d4c3d21c0260ec663`.
- Archived; GitHub reports GPL-3.0. Inspect the actual license text and file notices before any reuse.
- Useful reference: native movie/series primary-image provider, settings and optional scheduled replacement. Its stated baseline is Jellyfin 10.11.10, not evidence of Jellyfin 12 compatibility.

### TheAceOfficials/BetterPoster-for-Jellyfin

- https://github.com/TheAceOfficials/BetterPoster-for-Jellyfin
- Observed main commit: `db02fea7bdfc3c6c927d41813050909e98d84085`.
- GitHub license endpoint returned 404 at bootstrap; no reuse permission established. Do not copy code, binaries or artwork without establishing permission.
- Useful behavioral reference: IMDb-first/TMDB fallback and customizable options. Watched-progress token behavior is outside MVP.

## Upstream poster service

https://btttr.cc/ — verify current endpoint contract, usage conditions, rate limits and supported options before implementing. No availability, API stability, artwork rights or token privacy guarantees are inferred from the reference READMEs.

## Licensing gate

No upstream source/assets were imported. Original-code licensing is pending owner decision. Independent implementation may proceed; importing GPL source requires compatible licensing and preserved notices. Unlicensed reference material must not be copied. Record third-party package/fixture licenses before shipping.
