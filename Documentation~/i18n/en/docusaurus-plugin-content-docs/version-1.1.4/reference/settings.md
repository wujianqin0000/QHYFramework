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
| `refreshHostManifestEveryStartup` | true | Check the remote manifest for repeated same-version releases |
| `autoUnloadBundleWhenUnused` | false | Unload bundles when their reference count reaches zero |

Each `PlatformRemoteProfile` contains a platform, `remoteBaseUrl`, and
`clientUpdateBaseUrl`. Do not include a version in either URL.

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
4. Full Package Build regenerates and validates the result, writes it into the client settings, and freezes a platform-specific metadata snapshot.
5. HotUpdateOnly always restores the snapshot for the published client and never mixes in new local AOT output.

If the stored analysis belongs to another target, run `Generate/All` for the active target or click **Refresh**. Refresh reads existing generated output; it does not run HybridCLR generation.

## FTP, editor only

| Field | Default | Purpose |
|---|---:|---|
| `ftpHost` | empty | FTP host or IP address |
| `ftpPort` | 21 | FTP/FTPS port |
| `ftpUserName` | empty | Upload account |
| `ftpPassword` | empty | Upload password; do not commit it to a public repository |

These fields are synchronized with the QHY Framework Release window.
