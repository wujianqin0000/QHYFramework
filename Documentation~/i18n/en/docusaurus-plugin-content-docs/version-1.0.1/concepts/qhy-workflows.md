---
title: QHY startup and update workflows
---

# QHY startup and update workflows {#qhy-workflows}

```mermaid
flowchart TD
  A["Boot (AOT + UGUI)"] --> B{"Editor simulation?"}
  B -- "yes" --> E["Initialize simulated package"]
  B -- "no" --> C["Request client latest.json"]
  C --> D{"Newer client?"}
  D -- "yes" --> I["Confirm → resume download → SHA-256 → install/restart"]
  D -- "no/check failed" --> F["Initialize YooAsset package"]
  E --> G["Load manifest and required bundles"]
  F --> G
  G --> H["Load AOT metadata + HotUpdate DLL"]
  H --> J["Load Main with YooAssetSceneKit"]
```

Client-manifest failure does not block the old client. Resource request failure
uses a valid cache when available. A mandatory newer client cannot enter Main
until installed.

HotUpdateOnly uploads resources under `/CDN/<Platform>/<Version>` and never
updates client `latest.json`. Full Package produces resources, client ZIP/APK,
and publishes `latest.json` last.
