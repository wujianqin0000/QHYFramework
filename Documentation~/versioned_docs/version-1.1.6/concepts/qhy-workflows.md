---
title: QHY 启动与更新流程
---

# QHY 启动与更新流程 {#qhy-workflows}

## 启动总流程

```mermaid
flowchart TD
  A["Boot (AOT + UGUI)"] --> B{"EditorSimulateMode?"}
  B -- "是" --> E["初始化模拟 Package"]
  B -- "否" --> C["请求 Client latest.json"]
  C --> D{"有更高客户端版本?"}
  D -- "是" --> I["确认 → 断点下载 → SHA-256 → 安装/重启"]
  D -- "否/检查失败" --> E2["初始化当前 PlayMode Package"]
  E --> F["加载 Manifest"]
  E2 --> F
  F --> G{"有待下载资源?"}
  G -- "是" --> H["显示数量与大小，确认并下载"]
  G -- "否" --> J["加载 AOT 元数据和 HotUpdate DLL"]
  H --> J
  J --> K["YooAssetSceneKit.LoadSceneAsync(Main)"]
  K --> L["销毁 Boot UI，进入业务"]
```

客户端检查失败不会封锁旧客户端；资源远端请求失败则优先回退本地缓存。若检测到
强制客户端新版，用户确认前不能进入 Main。

## 热更新发布流

```mermaid
flowchart LR
  A["修改 Game.HotUpdate / 资源"] --> B["HotUpdateOnly Build"]
  B --> C["构建 GamePackage"]
  C --> D["上传 /CDN/{Platform}/{Version}"]
  D --> E["客户端启动更新 Manifest"]
```

HotUpdateOnly 不改变客户端版本，也不更新 `/Client/.../latest.json`。

## 完整客户端发布流

```mermaid
flowchart LR
  A["AOT/原生配置变化"] --> B["Full Package Build"]
  B --> C["资源目录"]
  B --> D["Windows ZIP / Android APK"]
  D --> E["生成 latest.json"]
  C --> F["先上传资源"]
  D --> G["再上传客户端"]
  E --> H["最后原子更新 latest.json"]
```

最后更新 `latest.json` 避免客户端看到尚未上传完成的新包。服务器必须继续保留旧
资源目录。
