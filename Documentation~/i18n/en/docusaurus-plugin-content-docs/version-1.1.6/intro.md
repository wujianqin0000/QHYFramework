---
sidebar_position: 1
title: Welcome to QHY Framework
description: A non-invasive integration and release workflow for QFramework, HybridCLR, and YooAsset.
---

# Welcome to QHY Framework {#welcome}

QHY Framework combines **QFramework 1.0.246**, **HybridCLR 8.12.0**, and
**YooAsset 3.0.4** into a production workflow. Continue using `ResLoader`,
`UIKit`, and `AudioKit`; YooAsset owns packaged resource loading, caching, and
release. Game code lives in `Game.HotUpdate` and is loaded by HybridCLR at boot.

:::tip New to all three frameworks?
Follow Installation → Ten-minute quick start → First release. No prior knowledge is required.
:::

You get a dependency-free Boot scene, a hot-updatable Main scene, independent
Windows/Android release profiles, resource hot update, and full-client updates
for AOT/native changes.

## Recommended path

1. [Requirements](getting-started/requirements.md)
2. [Installation](getting-started/installation.md)
3. [Ten-minute quick start](getting-started/quick-start.md)
4. [QHY workflows](concepts/qhy-workflows.md)
5. [Choose a build type](release/choose-build.md)
6. [Troubleshooting](help/troubleshooting.md)

The first release supports Unity `2022.3.62f2c1`, Windows64, and Android. QHY
does not modify QFramework, HybridCLR, or YooAsset source.
