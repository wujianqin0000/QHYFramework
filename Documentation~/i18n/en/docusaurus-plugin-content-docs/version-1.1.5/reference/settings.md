---
title: QHYFrameworkSettings fields
---

# QHYFrameworkSettings field reference {#settings}

The configuration asset lives at `Assets/Game/Config/QHYFrameworkSettings.asset`.

![YooAsset and per-platform remote profiles in QHYFrameworkSettings](/img/screenshots/settings.webp)

## YooAsset

| Field | Default | Purpose |
|---|---:|---|
| `packageName` | `GamePackage` | YooAsset package name; it must match the build output |
| `platformProfiles` | empty | Per-platform resource and client-update roots without a version suffix |
| `editorSimulatePackageRoot` | empty | Last editor simulation directory, for diagnostics only |
| `requestTimeoutSeconds` | 60 | Version and manifest request timeout |
| `downloadConcurrency` | 8 | Parallel file downloads |
| `downloadRetryCount` | 2 | Retries per file |
| `downloadMaxRequestPerFrame` | 1 | New download requests started per frame |
| `downloadWatchdogTimeoutSeconds` | 10 | No-progress timeout; 0 disables it |
| `copyBuiltinPackageManifest` | true | Copy the first-package manifest to StreamingAssets |
| `clearUnusedCacheAfterUpdate` | true | Remove cache entries outside the active manifest |
| `refreshHostManifestEveryStartup` | true | Request the version pointer every startup to discover a new ResourceVersion |
| `autoUnloadBundleWhenUnused` | false | Unload bundles when their reference count reaches zero |
| `collectorManagementMode` | `InitializeOnly` | Initialize once, manage marked groups only, or use an external package |
| `bundleWarningThresholdMiB` | 4 | Bundle-size warning threshold; 0 disables it |
| `bundleErrorThresholdMiB` | 16 | Bundle-size publication blocker; 0 disables it |
| `ignoreTypeTreeChangesForIncrementalBuild` | false | Advanced TypeTree-impact diagnostics; SBP still writes type trees |

Each `PlatformRemoteProfile` contains a platform, `remoteBaseUrl`, and
`clientUpdateBaseUrl`. Do not include a version in either URL.

InitializeOnly leaves an existing package untouched. ManagedGroupsOnly owns a
group only when all of its collectors carry `QHYFramework.Managed:v2`; an
unmarked path conflict produces a migration error instead of deletion.
External requires the host project to create the package and QHY only validates it.

YooAsset 3.0.4 Scriptable Build Pipeline does not support
`IgnoreTypeTreeChanges`. The advanced switch only flags a heuristic case where
assets/dependencies are stable but the bundle hash changes. Test new scenes, UI,
and shared prefabs with the old client before relying on the result.

## Startup and assemblies

| Field | Default | Purpose |
|---|---:|---|
| `startupSceneAddress` | `Main` | YooAsset address of the first gameplay scene |
| `hotUpdateAssemblies` | `Game.HotUpdate` | Hot-update assembly names and DLL addresses |
| `aotMetadataAssemblyNames` | automatic | Effective runtime AOT metadata list maintained by the tools |
| `aotMetadataExtraAssemblyNames` | empty | Special AOT assemblies added beyond automatic analysis |

The Inspector reads HybridCLR output for the active Build Target. Hot-update candidates must be configured in HybridCLR and generated in `HybridCLRData/HotUpdateDlls/<target>`.

### Automatic AOT metadata

QHY Framework treats HybridCLR's generated `AOTGenericReferences.PatchedAOTAssemblyList` as the authoritative automatic list:

1. HybridCLR `Generate/All` synchronizes the automatic list for the active Build Target.
2. Automatically detected entries are locked in the Inspector and cannot be cleared manually.
3. Use **Additional AOT Metadata Assemblies** only for exceptional requirements outside HybridCLR's analysis.
4. Full Package Build regenerates and validates the result, writes it into client settings, and freezes a platform/ClientVersion-specific per-file SHA-256 snapshot.
5. HotUpdateOnly restores that published-client snapshot and never mixes in new local AOT output; verification failure or an AOT bundle delta blocks publication.

If the stored analysis belongs to another target, run `Generate/All` for the active target or click **Refresh**. Refresh reads existing generated output; it does not run HybridCLR generation.

## FTP, editor only

| Field | Default | Purpose |
|---|---:|---|
| `ftpHost` | empty | FTP host or IP address |
| `ftpPort` | 21 | FTP/FTPS port |
| `ftpUserName` | empty | Upload account |
Password is deliberately not a settings field and is never written to Assets,
ProjectSettings, or UserSettings. Enter it for the current Release Window
session or inject `QHY_FTP_PASSWORD`; CI may override the serialized username
with `QHY_FTP_USERNAME`. Success, failure, cancellation, or closing the window
clears the session password. Errors are redacted and project text files are
scanned for credential residue after publication.
