---
title: Prefabs, configuration, and addresses
---

# Prefabs, configuration, and addresses {#resources}

Put content under `Assets/Game/Content`:

| Folder | Content | Address |
|---|---|---|
| `Common` | Prefabs, materials, ScriptableObjects | `Player` |
| `UI` | UIKit panel prefabs | `LoginPanel` |
| `Audio` | Music, sound, voice | `BgmLobby` |
| `Scenes` | Business scenes | `Main` |

## Default packing and host customization

For a newly initialized project using the current code, scenes and assets directly under a
collector are independent. Nested UI/Common/Audio assets group by the first
feature directory. HotUpdate DLL/PDB, AOT metadata, and UIRoot use independent
bundles. Make directories express update correlation, for example
`UI/LevelSelect/Navigation`, `UI/LevelSelect/Background`, and `Audio/Music`.
Avoid both one giant UI image directory and blindly creating one bundle per image.

These are initialization defaults only. With InitializeOnly, builds never
rewrite an existing collector package; configure atlases, shared dependencies,
and custom pack rules in YooAsset. `release-report.json` reports main assets,
dependencies, upstream references, and the ten largest source files for bundles
over 4 MiB by default. A 16 MiB bundle blocks publication until it is split or
the threshold is deliberately changed.

```csharp
var loader = QFramework.ResLoader.Allocate();
var config = loader.LoadSync<GameConfig>("GameConfig");
loader.Add2Load<GameObject>("Player", prefab =>
    Object.Instantiate(prefab));
loader.LoadAsync();
```

Addresses are globally unique short names. The legacy ownerBundle parameter is
only signature compatibility. Use ScriptableObject/JSON/TextAsset for
hot-updatable configuration rather than `Resources`.

**Expected:** every address is visible in the YooAsset Build Report.
