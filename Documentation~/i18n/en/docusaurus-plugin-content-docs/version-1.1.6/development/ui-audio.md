---
title: UIKit and AudioKit
---

# UIKit and AudioKit {#ui-audio}

```csharp
UIKit.OpenPanel<LoginPanel>(
    UILevel.Common,
    new LoginPanelData { UserName = "guest" });
UIKit.ClosePanel<LoginPanel>();

AudioKit.PlayMusic("BgmLobby", loop: true);
AudioKit.PlaySound("ButtonClick");
AudioKit.PlayVoice("Guide01");
```

Collect panel prefabs and audio clips with matching addresses. Boot installs
QHY's Root/Panel loader and AudioLoaderPool; packaged assets no longer call
`Resources.Load`. A `UIKitConfig.LoadPanel` null reference usually means the
adapter is not initialized yet or the prefab address is wrong.

Network and local images remain on QFramework's original loaders because they
are not packaged resources.
