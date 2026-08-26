# QHY Framework

QHY Framework 是面向 Unity 2022.3 LTS 的标准 UPM 包，将 QFramework、HybridCLR
和 YooAsset 无侵入地整合为一条完整的开发、资源热更新、代码热更新、客户端整包升级
与发布工作流。

## 安装

在 Unity Package Manager 中选择 **Add package from git URL...**：

```text
https://github.com/wujianqin0000/QHYFramework.git#v1.1.6
```

只需导入 `com.wjq.qhy-framework`。框架会自动安装 HybridCLR 8.12.0 与
YooAsset 3.0.4，QFramework 1.0.246 已包含在包中；依赖完成后自动创建 Boot、
Main、可编辑 BootUI Prefab、设置、热更新程序集和资源收集配置，无需执行
Prepare Project。

当前未发布变更将客户端版本与资源版本分离，发布时不再覆盖项目自定义 YooAsset Collector；
FTP 会按内容哈希增量上传并在最后原子切换 `GamePackage.version`。HotUpdateOnly 不修改
Player Version，也不会重新生成 AOT Metadata；没有 DLL、资源新增/变化/删除时会报告空更新，
不会生成或上传新版本。所有 FTP 配置只存在于 QHY Release 窗口，不再属于
`QHYFrameworkSettings`。密码默认安全保存到 Windows 凭据管理器，也可使用
`QHY_FTP_PASSWORD`。远端 `.qhy-bundle-hashes.json` 与本机 Library 验证缓存避免重复下载
Bundle 计算哈希；旧服务器只对索引缺口做一次兼容校验。上传成功的 ResourceVersion 会立即
禁用上传按钮，构建新版本或回滚后才能再次发布。

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
https://github.com/wujianqin0000/QHYFramework.git#v1.1.6
```

Import only `com.wjq.qhy-framework`. Dependencies and the host project skeleton
are prepared automatically. See the [English documentation](https://wujianqin0000.github.io/QHYFramework/en/)
for the quick start, development guide, release operations, API reference, and
troubleshooting.

The current unreleased changes separate immutable client versions from resource revisions,
preserves host YooAsset collectors, uploads only missing content-addressed
bundles, atomically publishes the version pointer, freezes AOT metadata for
HotUpdateOnly, and removes FTP passwords from serialized project assets.
Empty HotUpdateOnly bundle deltas and legacy empty upload plans are blocked. All FTP configuration
now belongs to the project/platform-scoped Release window instead of `QHYFrameworkSettings`;
passwords may be remembered in Windows Credential Manager, and remote SHA-256 preflight exposes
cancellable per-file progress. An atomic remote hash index plus a Library verification cache avoids
re-downloading existing bundles, and a successfully published ResourceVersion cannot be uploaded twice.
The author-project UPM publisher keeps the last stable version during development and chooses the
Major/Minor/Patch target only at publication, with a mandatory `npm run check` before either push.

The Release window uses one **Automatic Versioning** toggle for both values and enables it by
default for each new project/platform preference. Enabled mode provides
Major/Minor/Patch client builds and assigns the next available `ClientVersion-rNNNN` automatically;
disabled mode makes both ClientVersion and ResourceVersion explicit manual inputs. Automatic
revision selection skips published baselines, local Release snapshots, and leftover YooAsset output.
The Release window can roll back to any locally complete revision within the published history, not
only the immediately previous one. It verifies the target manifests and all required remote bundles
before atomically changing the version pointer; a later published revision can also be selected to
undo a rollback.
For first-time users this action appears in its own **Restore an Online Resource Version
(Rollback)** section, shows the active online version, explains the failure-recovery scenario, and
uses the explicit **Choose an Online Version to Restore…** action instead of revision terminology.
Local revisions are archived under `Revisions/<ResourceVersion>` rather than being scattered beside
the client version. Immutable bundle payloads are kept once in the sibling `SharedBundles` store and
hard-linked into each Windows snapshot (with a normal-copy fallback), so revision folders remain
independently inspectable without duplicating unchanged bundle bytes. Existing legacy layout remains
valid as a baseline and is included in automatic revision discovery.

HotUpdateOnly validates restored AOT metadata by file set, length, and SHA-256. A changed carrier
bundle is only a warning when its metadata payload still matches the published client snapshot;
actual metadata additions, removals, or byte changes remain blocked.
When an actual mismatch is detected from the Release window, an explicit one-shot **Force Publish**
button can continue the current HotUpdateOnly pipeline after a high-risk warning. This override is
never persisted or silently enabled for command-line builds. The release report records
`aotMetadataForcePublished` and `aotMetadataMismatchReason`; Full Package remains the safe default.
