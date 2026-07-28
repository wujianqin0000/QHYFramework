---
title: Automatic setup
---

# Automatic setup {#automatic-setup}

Setup is additive and idempotent; it is not a project reset. It creates only
missing framework assets:

- `Assets/Game/Config/QHYFrameworkSettings.asset`
- editable `Assets/Game/Boot/BootUI.prefab`
- Boot and Main scenes when missing
- `Game.HotUpdate.asmdef` and generated binary directories
- Boot build settings and YooAsset `GamePackage` collectors

Existing saved scenes and business folders are not cleared. An unsaved
Untitled scene is closed without preserving its default Camera, Light, or other
temporary objects before host scenes are generated.

There is no user-facing “Prepare Project” step. Release buttons run the needed
preparation and validation automatically. You may customize generated assets;
commit them to version control.

**Expected:** repeating initialization does not overwrite existing business
content. **Next:** [Project layout](project-layout.md).
