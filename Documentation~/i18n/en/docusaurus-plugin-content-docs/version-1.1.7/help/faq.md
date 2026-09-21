---
title: FAQ
---

# FAQ {#faq}

## Which framework should I learn first?

Complete the quick start, then learn QFramework business architecture, YooAsset
Address/Handle concepts, and finally HybridCLR's AOT boundary.

## Must Assets/Scenes contain only Boot?

No. Boot must be the correct built-in entry; hot-updatable gameplay scenes
belong under `Assets/Game/Content/Scenes`.

## Where is Prepare Project?

It is intentionally removed. Import and release entry points run idempotent
preparation automatically.

## Can I republish the same resource version?

No. ResourceVersion is immutable in the current release system; every HotUpdateOnly advances the
`-rNNNN` revision. Clients still use the same ClientVersion compatibility
directory and discover the new manifest through `GamePackage.version`, so the
client version and CDN root do not change.

## Does client update check failure block the game?

No. It warns and continues. YooAsset then falls back to a usable cached manifest
when remote resources fail.

## Can I customize BootUI?

Yes. Edit `Assets/Game/Boot/BootUI.prefab`, keep required references, and use a
Filled Image for ProgressFill. Do not add required remote assets to Boot UI.

## Are iOS/macOS/Linux fully supported?

Resource profiles are reserved, but the first release only validates complete
Windows64 and Android client installers.
