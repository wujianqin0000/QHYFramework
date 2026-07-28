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
