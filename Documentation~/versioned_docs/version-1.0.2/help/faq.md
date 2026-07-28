---
title: FAQ
---

# 常见问题 {#faq}

## 我完全没用过三套框架，先学哪一个？

先完成[十分钟入门](../getting-started/quick-start.md)，再学 QFramework 的业务分层和
资源调用；随后理解 YooAsset Address/Handle；最后掌握 HybridCLR 的 AOT 边界。无需
先通读三套上游全部文档。

## Assets/Scenes 必须只有 Boot 吗？

不需要。只要求 Boot 是正确的内置启动场景。其他内置工具场景可以存在；需要热更新的
业务场景放 `Assets/Game/Content/Scenes` 并使用 YooAsset Address。

## Prepare Project 去哪里了？

已取消面向用户的手动步骤。安装和构建入口自动执行幂等准备，避免多余菜单和误操作。

## 同一资源版本能重复发布吗？

可以，`refreshHostManifestEveryStartup` 允许客户端刷新同一 PackageVersion 的
Manifest，适合日常 HotUpdateOnly。只有你手动从 v1 升 v2 才换目录；不要为每次小改
频繁改版本。

## 资源变化为什么有时仍要完整包？

普通远端资源不需要。若你改变了 Boot 内置 Prefab、StreamingAssets 首包布局、
Player 原生设置或 AOT 引用边界，则需要 Full Package。

## 客户端版本检查失败会不会进不了游戏？

不会。`latest.json` 请求失败会警告并继续当前客户端。之后 YooAsset 远端也失败时，
可用已缓存 Manifest 回退；首次安装且没有缓存才停留 Boot。

## QFramework 的 ownerBundle 还有意义吗？

仅用于旧调用签名兼容，不再定位 AssetBundle。最终 YooAsset Address 由短资源名解析。

## 可以改 BootUI 吗？

可以。编辑 `Assets/Game/Boot/BootUI.prefab`，保留 BootUGUIView 所需引用。Progress
Fill 的 Image Type 应为 Filled。不要把外部 YooAsset 资源作为 Boot UI 的必需依赖。

## 支持 iOS/macOS/Linux 吗？

设置和资源层预留平台，但首版只完整验收 Windows64 与 Android 的客户端安装器。其他
平台不要据此宣称已支持自动整包安装。
