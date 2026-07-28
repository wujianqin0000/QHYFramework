---
title: YooAsset fundamentals
---

# YooAsset fundamentals {#yooasset}

| Term | Meaning |
|---|---|
| Package | Independently versioned resource set (`GamePackage`) |
| Collector | Editor rule selecting assets |
| Address | Stable runtime name such as `Main` |
| Manifest | Versioned asset, bundle, dependency, and hash index |
| Bundle | Downloadable resource archive |
| Handle | Asset/scene lifetime token that must be released |
| Cache | Verified bundles stored locally |

Addresses are not file paths and must be globally unique.

| Play mode | Use |
|---|---|
| `EditorSimulateMode` | Fast editor iteration, no remote request |
| `OfflinePlayMode` | Fully offline package |
| `HostPlayMode` | Built-in first package plus CDN |
| `WebPlayMode` | WebGL hosting |

During player builds, only `EditorSimulateMode` is automatically converted to
HostPlayMode; explicit Offline/Web choices are preserved.

Host mode requests
`<ResourceBaseUrl>/<Version>/<PackageName>.version`, then its Manifest. On
remote failure it falls back to a usable local cache. QHY's resource wrapper
holds `AssetHandle` until the QFramework loader is recycled.
