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

Full Package validates the local HybridCLR installation and version, runs
Generate/All, copies DLL/metadata, builds GamePackage and StreamingAssets,
reasserts local IL2CPP after the last refresh, builds the Player, verifies the
native HybridCLR interpreter, then creates ZIP/APK, hashes, a report, and
`latest.json`.

## 1.1.3 build guards

Full Package no longer selects the toolchain only before `Generate/All`. A
release-session flag prevents dependency initializers from overriding the
selection. Immediately before `BuildPipeline.BuildPlayer`, QHY asserts:

- `HybridCLRSettings.enable == true`;
- `HybridCLRSettings.useGlobalIl2cpp == false`;
- process `UNITY_IL2CPP_PATH` equals `SettingsUtil.LocalIl2CppDir`;
- the package version matches local `libil2cpp-version.txt`;
- both `libil2cpp/hybridclr` and a known local compiler are present.

Any failure stops the release before archive or upload. After a successful
Player build, QHY also requires a HybridCLR interpreter-specific native marker,
preventing publication of a client accidentally built with stock Unity IL2CPP.

HotUpdateOnly does not build a Player or update `latest.json`. A changed local
AOT output warns but does not block; compatibility with the published client is
the publisher's responsibility.

Before building, save all scenes, select the correct platform/version, ensure
remote roots contain no version, and test the local artifact before upload.
