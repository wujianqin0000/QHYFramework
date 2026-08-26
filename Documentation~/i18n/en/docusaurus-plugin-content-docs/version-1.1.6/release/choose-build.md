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
Generate/All, copies DLL/metadata, stores a client-specific AOT SHA-256 snapshot,
builds GamePackage and bundle delta/size/upload reports, copies StreamingAssets,
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

HotUpdateOnly locks the published ClientVersion, never writes Player Version,
does not run Generate/All, and does not inspect or copy current local AOT output.
It restores the Full Package snapshot for that client and verifies the final
metadata file set, lengths, and SHA-256 values after the YooAsset build, and
confirms that every file is still collected by the current manifest. A changed
carrier bundle is only a warning when that payload still matches the client
snapshot; added, missing, uncollected, or byte-changed metadata remains blocked and
requires a new Full Package. A local AOT difference is still a compatibility
warning because it is excluded from this hot update.

When an actual payload mismatch is detected, the Release window shows a high-risk
confirmation with a **Force Publish** button. Confirmation continues the current
pipeline without rerunning under another ResourceVersion. The authorization applies
to that attempt only, is not stored in EditorPrefs, and is never silently enabled for
command-line publication. `release-report.json` records
`aotMetadataForcePublished: true` and `aotMetadataMismatchReason`. Client-version,
signing, resource-integrity, bundle-size, and upload checks remain enforced.

:::danger Force publication is not AOT compatibility
Neither normal nor forced HotUpdateOnly injects new native AOT code into an existing
client. Hot-update code that depends on an absent or signature-changed AOT API can
cause `MissingMethodException`, supplemental-metadata failure, or a crash. Cancel and
run Full Package unless compatibility has been established.
:::

Collector management is non-destructive:

- `InitializeOnly` (default) creates the target package once, then only validates it;
- `ManagedGroupsOnly` replaces only groups whose collectors all carry
  `QHYFramework.Managed:v2`;
- `External` leaves the complete package to the host project.

Legacy unmarked collectors are never adopted by group name. Mark confirmed
legacy QHY collectors before selecting ManagedGroupsOnly; path conflicts stop
the build rather than deleting user configuration or creating duplicate addresses.

Before building, save all scenes and select the correct platform. Enable Automatic
Versioning to calculate ClientVersion and ResourceVersion together, or disable it to
enter both manually. Ensure remote roots contain no version, inspect the
bundle-size report (4 MiB warning / 16 MiB error by default), and test locally.
