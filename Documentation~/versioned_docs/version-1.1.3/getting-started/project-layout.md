---
title: 项目目录
---

# 项目目录 {#project-layout}

```text
Assets/
├─ Scenes/
│  └─ Boot.unity                  # 唯一必须内置的启动场景
└─ Game/
   ├─ Boot/BootUI.prefab          # 可自由定制的启动 UI
   ├─ Config/QHYFrameworkSettings.asset
   ├─ Content/
   │  ├─ Common/                  # 通用 Prefab、材质、配置
   │  ├─ UI/                      # UIKit Panel Prefab
   │  ├─ Audio/                   # 音乐、音效、语音
   │  └─ Scenes/Main.unity        # YooAsset 业务场景
   ├─ Scripts/HotUpdate/          # Game.HotUpdate 业务代码
   └─ Generated/
      ├─ HotUpdate/               # 复制后的热更 DLL.bytes
      └─ AOTMetadata/             # 补充元数据 DLL.bytes
Packages/
└─ com.wjq.qhy-framework/         # 只读 UPM 依赖，不放业务代码
Releases/
└─ default/<Platform>/<Version>/  # 本地发布结果
```

## 哪些文件进客户端

Boot 及其脚本属于 AOT/内置内容。Main、UI、音频、配置、HotUpdate DLL 和 AOT
补充元数据由 `GamePackage` 收集。Boot 加载 Main 后会销毁自己的 UI，Boot
对象不会被带入 Main。

## 修改规则

- 不要把业务代码写进 `Packages/com.wjq.qhy-framework`，包更新会覆盖它。
- 不修改 QFramework、HybridCLR、YooAsset 源码。
- Address 使用短名且全局唯一，例如 `Main`、`LoginPanel`、`BgmLobby`。
- `Generated` 是构建输入，不把旧发布产物当源码。

**下一步：** [完成第一次运行与热更新](quick-start.md)。
