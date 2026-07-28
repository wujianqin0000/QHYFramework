---
title: Choose a build type
---

# Full Package or HotUpdateOnly {#choose-build}

![Version automation, Full Package, and HotUpdateOnly controls in the QHY Framework Release window](/img/screenshots/release-window.webp)

| Change | Build |
|---|---|
| `Game.HotUpdate` gameplay code | HotUpdateOnly |
| Main, prefab, UI, audio, config | HotUpdateOnly |
| Boot/AOT code or native plugin | Full Package |
| Player settings, identifier, permissions, signing | Full Package |

Full Package validates, runs HybridCLR Generate/All, copies DLL/metadata, builds
GamePackage and StreamingAssets, builds the IL2CPP Player, creates ZIP/APK,
hashes, a report, and `latest.json`.

HotUpdateOnly does not build a Player or update `latest.json`. A changed local
AOT output warns but does not block; compatibility with the published client is
the publisher's responsibility.

Before building, save all scenes, select the correct platform/version, ensure
remote roots contain no version, and test the local artifact before upload.
