---
title: Architecture
---

# Architecture and boundaries {#architecture}

```mermaid
flowchart TB
  subgraph BuiltIn["Built-in client / AOT"]
    Boot["Boot + GameBootstrap"]
    Runtime["GamePackageRuntime"]
    Update["Client update + installer"]
    Adapter["QFramework YooAsset adapters"]
  end
  subgraph Remote["GamePackage / CDN"]
    Manifest["Version + manifest"]
    AOTM["AOT metadata"]
    Hot["Game.HotUpdate.dll"]
    Content["Main / UI / Audio / Config"]
  end
  subgraph Business["Business calls"]
    QF["ResKit / UIKit / AudioKit"]
    Kit["YooAssetSceneKit"]
  end
  Boot --> Update
  Boot --> Runtime
  Runtime --> Remote
  QF --> Adapter --> Runtime
  Kit --> Runtime
```

Upstream packages remain unmodified. QHY owns AOT integration and Editor tools.
The host project owns Boot/BootUI, while business code/content is hot-updatable.
Server artifacts remain versioned and old directories stay available.
