---
title: 项目目录
---

# 项目目录 {#project-layout}

```text
Assets/
├─ Scenes/Boot.unity
└─ Game/
   ├─ Config/QHYFrameworkSettings.asset
   ├─ Content/{Common,UI,Audio,Scenes}
   ├─ Scripts/HotUpdate/
   └─ Generated/{HotUpdate,AOTMetadata,QHYLink,Distribution}
Releases/
└─ {gameDirectory}/
   ├─ android/{cdn/{bundles,versions,clients},origin/{current,qhy.json}}
   └─ windows/{cdn/{bundles,versions,clients},origin/{current,qhy.json}}
QHYBuilds/
└─ {platform}/{clientVersion}/{resourceVersion}/
ProjectSettings/QHYFramework/ReleaseState/
└─ {platform}/
```

`Releases/{gameDirectory}` 是该游戏的服务器镜像；不同游戏可共用一个服务器根而不会互相覆盖。
可整体或增量上传。`QHYBuilds` 保存 Player、报告、
`publish-plan.json`、哈希和双阶段 Upload 目录，两者都不上传 Plastic。可移植的
Published 基线、历史和按 ClientVersion 冻结的 AOT 元数据位于
`ProjectSettings/QHYFramework/ReleaseState`，必须上传 Plastic，否则其他开发机和 CI
无法安全执行 HotUpdateOnly。

QHY 在项目初始化和每次发布前幂等维护项目根 `ignore.conf` 中带 BEGIN/END 标记的规则块，
自动忽略 `Releases` 与 `QHYBuilds`；检测到 Git 时同样维护 `.gitignore`。现有用户规则不会被
覆盖。忽略规则只对未跟踪文件生效：如果这两个目录以前已经提交，必须在 Plastic/Git 中
取消跟踪但保留本地文件。`ReleaseState` 不得加入忽略规则。

Boot 是唯一必须内置的场景。Main、UI、音频、配置、HotUpdate DLL 和 AOT 补充元数据都由
`GamePackage` 收集。`QHYLink/link.xml` 由 FullPackage 按外置资源重建，不要手改。

schema v5 不读取此前的根目录直出平台结构或更早的 `Releases/Server`、`Revisions`、`SharedBundles` 等目录；发现旧
结构会停止构建并要求手工清空，不提供迁移。
