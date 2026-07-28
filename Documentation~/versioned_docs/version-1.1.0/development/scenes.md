---
title: 场景开发与切换
---

# 场景开发与切换 {#scenes}

Boot 是内置场景；Main 和后续业务场景属于 YooAsset。`Assets/Scenes` 可以有其他
场景，框架只要求 Build Settings 中 Boot 作为启动入口。

```csharp
using GameIntegration;
using UnityEngine.SceneManagement;

await YooAssetSceneKit.LoadSceneAsync(
    "Battle",
    LoadSceneMode.Single);
```

同步加载仅在确有必要时使用：

```csharp
var handle = YooAssetSceneKit.LoadSceneSync("Lobby");
```

卸载 Additive 场景：

```csharp
await YooAssetSceneKit.UnloadSceneAsync("Battle");
```

## 为什么不用 QFramework 原生场景 API

旧场景扩展内部硬编码 AssetBundle/SceneManager，没有可替换 Loader 的扩展点。
为了不修改 QFramework 源码，业务场景统一走 `YooAssetSceneKit`。

## 生命周期

`LoadSceneMode.Single` 进入 Main 时 Boot UI 与 Boot 场景对象会被销毁。不要给 Boot
对象加 `DontDestroyOnLoad`，除非它确实是全局服务；启动界面不应带进业务场景。

**常见错误：** Main 黑屏时先检查场景是否有启用的 Camera，以及 Main 的 YooAsset
Address 是否恰好为配置中的 `startupSceneAddress`。
