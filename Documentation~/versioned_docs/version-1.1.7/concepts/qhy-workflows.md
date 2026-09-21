---
title: QHY 启动与更新流程
---

# QHY 启动与更新流程 {#qhy-workflows}

```mermaid
flowchart TD
  A["Boot"] --> B["请求 origin/current/client.json"]
  B --> C["初始化 GamePackage"]
  C --> D["请求 origin/current/{ClientVersion}.version"]
  D --> E["加载 cdn/versions 下的 Manifest"]
  E --> F["从 cdn 下载 bundles"]
  F --> G["加载 AOT 元数据与热更 DLL"]
  G --> H["进入 Main"]
```

远端失败时仅在本地存在完整可运行缓存的情况下离线进入。Manifest 验证成功前保留最后可用
版本，不删除回退所需文件。

```mermaid
flowchart LR
  A["构建"] --> B["Releases 镜像"]
  B --> C["上传不可变文件"]
  C --> D["上传 origin/qhy.json"]
  D --> E["最后上传 origin/current"]
  E --> F["Published"]
```

FTP 自动执行上述顺序。手动发布对应 `Upload/1-Files` 和 `Upload/2-Publish`；分别通过
CDN Root 与 Origin Root 检查后登记 Published。CDN 不在发布状态机中。
HotUpdateOnly 不生成 APK/ZIP；FullPackage 才更新 `origin/current/client.json`。
