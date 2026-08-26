---
title: Project layout
---

# Project layout {#project-layout}

```text
Assets/
├─ Scenes/Boot.unity
└─ Game/
   ├─ Boot/BootUI.prefab
   ├─ Config/QHYFrameworkSettings.asset
   ├─ Content/{Common,UI,Audio,Scenes}
   ├─ Scripts/HotUpdate/
   └─ Generated/{HotUpdate,AOTMetadata}
Releases/default/<Platform>/<ClientVersion>/
├─ Revisions/<ResourceVersion>/
└─ SharedBundles/
```

Boot is built into the client. Main, UI, audio, configuration assets, hot-update
DLLs, and AOT metadata are collected by `GamePackage`. Boot UI is destroyed
when Main loads.

Keep business code outside `Packages/com.wjq.qhy-framework`, never edit the
three upstream frameworks, and make every short Address globally unique.
Revision snapshots stay independently inspectable under `Revisions`; unchanged
content-addressed bundles share storage through `SharedBundles`.

**Next:** [Ten-minute quick start](quick-start.md).
