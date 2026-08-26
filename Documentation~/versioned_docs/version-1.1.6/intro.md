---
sidebar_position: 1
title: 欢迎使用 QHY Framework
description: QFramework、HybridCLR 与 YooAsset 的无侵入集成和完整发布工作流。
---

# 欢迎使用 QHY Framework {#welcome}

QHY Framework 把 **QFramework 1.0.246**、**HybridCLR 8.12.0** 与
**YooAsset 3.0.4** 组合成一条可以直接交付的 Unity 工作流。你仍然用熟悉的
`ResLoader`、`UIKit`、`AudioKit` 开发，底层资源加载、缓存和释放由 YooAsset
接管；业务代码放进 `Game.HotUpdate`，由 HybridCLR 在启动阶段加载。

:::tip 适合谁
即使你从未使用过三套框架，也可以按“安装 → 十分钟入门 → 第一次发布”的顺序完成项目。
:::

## 你会得到什么

- 一个无外部资源依赖的 Boot 场景，负责客户端更新、资源更新和热更程序集加载。
- 一个可热更新的 Main 场景与 `Game.HotUpdate` 示例代码。
- Windows64、Android 独立版本、远端地址、构建和 FTP 上传流程。
- AOT 变化时的 Windows ZIP / Android APK 整包更新能力。
- 中英文编辑器界面，以及不包含开发者凭据的标准 UPM 包。

## 推荐阅读路线

1. [环境准备](getting-started/requirements.md)
2. [安装](getting-started/installation.md)
3. [十分钟入门](getting-started/quick-start.md)
4. [三套框架的职责](concepts/qhy-workflows.md)
5. [选择 Full Package 或 HotUpdateOnly](release/choose-build.md)
6. [故障排查](help/troubleshooting.md)

## 支持边界

首版完整支持 Unity `2022.3.62f2c1`、Windows64 和 Android。其他 YooAsset
平台可以进行资源热更新，但客户端自动安装器尚未承诺支持。QFramework、
HybridCLR、YooAsset 的源码不会被修改，所有适配均位于 QHY Framework 包内。
