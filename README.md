# QHY Framework

QHY Framework 是面向 Unity 2022.3 LTS 的标准 UPM 包，将 QFramework、HybridCLR
和 YooAsset 无侵入地整合为一条完整的开发、资源热更新、代码热更新、客户端整包升级
与发布工作流。

## 安装

在 Unity Package Manager 中选择 **Add package from git URL...**：

```text
https://github.com/wujianqin0000/QHYFramework.git#v1.1.5
```

只需导入 `com.wjq.qhy-framework`。框架会自动安装 HybridCLR 8.12.0 与
YooAsset 3.0.4，QFramework 1.0.246 已包含在包中；依赖完成后自动创建 Boot、
Main、可编辑 BootUI Prefab、设置、热更新程序集和资源收集配置，无需执行
Prepare Project。

当前未发布变更将客户端版本与资源版本分离，发布时不再覆盖项目自定义 YooAsset Collector；
FTP 会按内容哈希增量上传并在最后原子切换 `GamePackage.version`。HotUpdateOnly 不修改
Player Version，也不会重新生成 AOT Metadata。FTP 密码已从项目资产删除，只接受当前
Editor 会话输入或 `QHY_FTP_PASSWORD` 环境变量。

框架开发工程在日常开发中保持最后稳定版本不变；发布时才选择主/次/修订目标。工具会
固化 Unreleased、更新版本和 README、生成双语快照并强制执行 `npm run check`。

从 1.1.4 升级后，默认 `Collector Management Mode = InitializeOnly`，现有 Collector
保持原样。需要让框架管理默认组时，应先确认并给旧 Collector 添加
`QHYFramework.Managed:v2` 标记，再切换 `ManagedGroupsOnly`，避免误接管用户配置。

完整的零基础教程、发布运维、API 和排障文档：

- [在线文档（默认中文）](https://wujianqin0000.github.io/QHYFramework/)
- [Online documentation (English)](https://wujianqin0000.github.io/QHYFramework/en/)
- [离线 Markdown 文档](Documentation~/README.md)

## Installation

In Unity Package Manager, choose **Add package from git URL...** and enter:

```text
https://github.com/wujianqin0000/QHYFramework.git#v1.1.5
```

Import only `com.wjq.qhy-framework`. Dependencies and the host project skeleton
are prepared automatically. See the [English documentation](https://wujianqin0000.github.io/QHYFramework/en/)
for the quick start, development guide, release operations, API reference, and
troubleshooting.

The current unreleased changes separate immutable client versions from resource revisions,
preserves host YooAsset collectors, uploads only missing content-addressed
bundles, atomically publishes the version pointer, freezes AOT metadata for
HotUpdateOnly, and removes FTP passwords from serialized project assets.
The author-project UPM publisher keeps the last stable version during development and chooses the
Major/Minor/Patch target only at publication, with a mandatory `npm run check` before either push.
