---
title: UIKit 与 AudioKit
---

# UIKit 与 AudioKit {#ui-audio}

## UIKit Panel

Panel Prefab 放在 `Assets/Game/Content/UI` 并使用与类型/调用一致的 Address：

```csharp
using QFramework;

UIKit.OpenPanel<LoginPanel>(
    UILevel.Common,
    new LoginPanelData { UserName = "guest" });

UIKit.ClosePanel<LoginPanel>();
```

Boot 初始化后会替换 `UIKit.Config` 的 Root/Panel Loader。若出现
`UIKitConfig.LoadPanel` 空引用，通常是适配器尚未安装、Prefab Address 不匹配，
或在 Boot 启动链完成前打开 Panel。

## AudioKit

```csharp
AudioKit.PlayMusic("BgmLobby", loop: true);
AudioKit.PlaySound("ButtonClick");
AudioKit.PlayVoice("Guide01");
AudioKit.StopMusic();
```

AudioClip 放 `Content/Audio` 并被 Collector 收集。适配器使用
`AudioKit.Config.AudioLoaderPool` 提供的 Loader 对象，不会尝试给只读的
`AudioKit.Config` 属性重新赋值。

## 自检方法

在编辑器 Profiler 或日志中确认没有业务资源触发 `Resources.Load`。网络图片和
本地图片是例外，它们不是打包资源，仍使用 QFramework 原加载器。
