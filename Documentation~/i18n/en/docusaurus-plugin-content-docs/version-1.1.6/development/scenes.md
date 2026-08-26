---
title: Scene development
---

# Scene development and transitions {#scenes}

Boot is built in; Main and gameplay scenes are YooAsset content. Other scenes
may exist under `Assets/Scenes`; only the Boot entry requirement matters.

```csharp
using GameIntegration;
using UnityEngine.SceneManagement;

await YooAssetSceneKit.LoadSceneAsync("Battle", LoadSceneMode.Single);
var handle = YooAssetSceneKit.LoadSceneSync("Lobby");
await YooAssetSceneKit.UnloadSceneAsync("Lobby");
```

Use SceneKit because QFramework's old scene helpers hard-code their own loading
path and offer no non-invasive loader extension. Loading Main in Single mode
destroys Boot UI; do not mark startup UI `DontDestroyOnLoad`.

For a black Main scene, verify an enabled Camera and that the scene Address
matches `startupSceneAddress`.
