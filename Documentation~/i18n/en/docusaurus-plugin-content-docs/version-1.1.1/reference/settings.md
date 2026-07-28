---
title: QHYFrameworkSettings fields
---

# QHYFrameworkSettings field reference {#settings}

The asset lives at `Assets/Game/Config/QHYFrameworkSettings.asset`.

![YooAsset and per-platform remote profiles in QHYFrameworkSettings](/img/screenshots/settings.webp)

## YooAsset

| Field | Default | Purpose |
|---|---:|---|
| `packageName` | `GamePackage` | Package and server filename prefix |
| `platformProfiles` | empty | Per-platform resource/client roots |
| `editorSimulatePackageRoot` | empty | Last simulation path, diagnostic only |
| `requestTimeoutSeconds` | 60 | Version/manifest request timeout |
| `downloadConcurrency` | 8 | Parallel file downloads |
| `downloadRetryCount` | 2 | Per-file retries |
| `downloadMaxRequestPerFrame` | 1 | New requests started per frame |
| `downloadWatchdogTimeoutSeconds` | 10 | No-progress watchdog; 0 disables |
| `copyBuiltinPackageManifest` | true | Copy first-package manifest to StreamingAssets |
| `clearUnusedCacheAfterUpdate` | true | Remove cache outside current manifest |
| `refreshHostManifestEveryStartup` | true | Refresh repeated same-version releases |
| `autoUnloadBundleWhenUnused` | false | Unload bundles at zero references |

Each `PlatformRemoteProfile` contains `platform`, version-free `remoteBaseUrl`,
and version-free `clientUpdateBaseUrl`.

## FTP, editor only

`ftpHost` (empty), `ftpPort` (21), `ftpUserName` (empty), and `ftpPassword`
(empty) bind to the Release window. Do not publish credentials.

## Startup

| Field | Default | Purpose |
|---|---:|---|
| `startupSceneAddress` | `Main` | First gameplay scene address |
| `hotUpdateAssemblies` | `Game.HotUpdate` | Assembly name and DLL address pairs |
| `aotMetadataAssemblyNames` | empty | Stripped AOT DLL names for metadata |

The default hot-update address is `Game.HotUpdate.dll`; the source asset may end
in `.dll.bytes` while retaining that logical address.
