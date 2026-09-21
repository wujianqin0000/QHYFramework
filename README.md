# QHY Framework

QHY Framework 是面向 Unity 2022.3 LTS 的标准 UPM 包，将 QFramework、HybridCLR
和 YooAsset 无侵入地整合为一条完整的开发、资源热更新、代码热更新、客户端整包升级
与发布工作流。

## 安装

在 Unity Package Manager 中选择 **Add package from git URL...**：

```text
https://github.com/wujianqin0000/QHYFramework.git#v1.1.7
```

只需导入 `com.wjq.qhy-framework`。框架会自动安装 HybridCLR 8.12.0 与
YooAsset 3.0.4，QFramework 1.0.246 已包含在包中；依赖完成后自动创建 Boot、
Main、可编辑 BootUI Prefab、设置、热更新程序集和资源收集配置，无需执行
Prepare Project。

当前 `Unreleased` 使用 schema v5。Settings Inspector 只显示当前 Unity BuildTarget 对应的
平台配置，各平台地址仍独立保存。BaseURL 只填写资源总根，例如 `https://aco.ai20.top`；
也可以直接填写 `aco.ai20.top`，框架会规范化为 HTTPS。发布只校验当前目标平台的 URL，
其他平台尚未配置完成不会阻止本次构建；平台重复项与 `Unknown` 仍会全局阻止。
另填一个全项目共用的“游戏资源目录”，例如 `qhy-framework-sample`。框架自动追加
`/{gameDirectory}/android`、`/{gameDirectory}/windows` 等目录，因此同一服务器根可安全放置多个游戏；
一键 Player 发布仍首期支持 Windows 与 Android。
发布工具中的“目标平台”现在会立即切换 Unity Active Build Target；切换完成后
`QHYFrameworkSettings` Inspector 自动显示该平台的 BaseURL，并加载该平台独立保存的版本与
FTP 偏好。缺少对应 Unity Build Support 时不会留下界面与项目平台不一致的状态。
用户填写的 BaseURL 不应包含平台、`/cdn` 或 `/origin`；框架派生出的运行时根地址为
`/{gameDirectory}/{platform}/cdn` 和 `/{gameDirectory}/{platform}/origin`，并按各自规则校验。
`Releases/{gameDirectory}/{platform}` 可原样上传：不可变 Bundle、Manifest 和客户端包进入 `cdn`，可变
指针与 `qhy.json` 进入 `origin`。
每次构建还会生成 `QHYBuilds/.../Upload/1-Files` 与 `2-Publish`，用于不依赖内置 FTP 的手动
增量上传；不可变大文件优先使用硬链接，不重复占用磁盘。FTP 上传成功立即 Published，手动上传
通过平台 CDN/Origin 地址检查后登记。CDN 完全可选，用户按 `*/cdn/*` 长期缓存、
`*/origin/*` 不缓存或强制回源即可。
只有当前版本确实生成对应目录时，“打开 1-Files/2-Publish”按钮才可用；无变化或未构建状态
不会调用文件管理器。
FTP 不再配置 RemoteRoot；FTP 账号登录根目录是多个游戏共用的资源根，QHY 会在其下创建
`{gameDirectory}/android`、`{gameDirectory}/windows` 等目录。
FTP 增量跳过 Bundle 时不只读取 `origin/qhy.json`，还会用 FTP `SIZE` 确认实际哈希文件仍然
存在且长度一致；索引残留但文件丢失时会自动补传，避免新 Manifest 指向 404 文件。客户端若
仍下载失败，启动界面与日志会同时显示逻辑 Bundle、实际哈希文件、完整 URL 和 HTTP/SSL 错误。
这是破坏性升级：必须清空本地 Releases、QHYBuilds、旧状态和服务器资源根，再重新 FullPackage。
`ProjectSettings/QHYFramework/ReleaseState`（含冻结 AOT 元数据）必须提交 Plastic，供其他开发机和 CI 延续热更新基线。
QHY 在首次初始化及每次发布前自动维护项目根 `ignore.conf` 的受管规则块，确保
`Releases`、`QHYBuilds` 默认不会进入 Plastic；检测到 Git 时也会维护 `.gitignore`。
它不会覆盖用户规则。如果这些目录已经被提交，仍需在版本控制中取消跟踪并保留本地文件。

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
https://github.com/wujianqin0000/QHYFramework.git#v1.1.7
```

Import only `com.wjq.qhy-framework`. Dependencies and the host project skeleton
are prepared automatically. See the [English documentation](https://wujianqin0000.github.io/QHYFramework/en/)
for the quick start, development guide, release operations, API reference, and
troubleshooting.

The current Unreleased FullPackage pipeline analyzes the real YooAsset collection and dependencies
for Unity Engine stripping. When external animation content is present it regenerates the QHY-owned
`Assets/Game/Generated/QHYLink/link.xml`, fully preserves `UnityEngine.AnimationModule`, imports it
before IL2CPP Player build, and blocks a stripped build if that protection is absent. It never edits
HybridCLR's generated `link.xml`; link preservation and supplemental AOT metadata remain independent.

The current Unreleased distribution layer is schema v5. The Settings Inspector shows only the profile
for Unity's active BuildTarget while retaining every platform independently. Enter only a resource
root such as `https://aco.ai20.top`, plus one project-wide game directory such as
`qhy-framework-sample`. A bare host such as `aco.ai20.top` is normalized to HTTPS. Publication
validates URL syntax for the target platform only, while duplicate and `Unknown` profiles remain
global errors. QHY appends `/{gameDirectory}/android`, `/{gameDirectory}/windows`, or the applicable
platform segment. This isolates multiple games below
one server root. `Releases/{gameDirectory}/{platform}` is the exact uploadable mirror. One-click Player publication remains
limited to Windows and Android.
Changing **Build Target** in the Release window now immediately switches Unity's Active Build
Target. The `QHYFrameworkSettings` Inspector follows the active platform and the Release window
loads that platform's version and FTP preferences. Missing Build Support fails without leaving the
window out of sync with the project.
The configured BaseURL must not include a platform, `/cdn`, or `/origin`; QHY derives
`/{gameDirectory}/{platform}/cdn` and `/{gameDirectory}/{platform}/origin` and validates both runtime roots.
immutable Bundles, manifests, and clients live under `cdn`, while pointers and `qhy.json` live under `origin`.
while every build provides `QHYBuilds/.../Upload/1-Files` and `2-Publish` for safe manual incremental
deployment. Immutable large files use hard links where possible. FTP commits Published immediately
after uploading `current` last; manual deployment is registered by checking the ordinary resource
server. CDN is optional: cache `*/cdn/*` as immutable and bypass or disable caching for `*/origin/*`.
Open 1-Files/2-Publish is enabled only when the exact directory exists for the current build; an
unbuilt or no-change version never invokes the file manager.
FTP no longer has a RemoteRoot setting. The account login directory is the resource root, with
QHY creating `{gameDirectory}/android`, `{gameDirectory}/windows`, and other platform folders below it.
An indexed Bundle is skipped only after a lightweight FTP `SIZE` probe confirms that the actual
hashed object still exists with the expected length. A stale index therefore triggers a repair
upload instead of publishing a manifest that points at a missing file. Runtime failures include the
logical Bundle, hashed file, complete URL, and HTTP/SSL transport error.
This is a breaking change with no migration: clear local and remote outputs and rebuild a
FullPackage.
Commit `ProjectSettings/QHYFramework/ReleaseState`, including frozen AOT metadata, so other machines
and CI can continue from the same hot-update baseline.
On initialization and before every release, QHY maintains a marked block in the project-root
`ignore.conf` and, when Git is detected, `.gitignore`, so `Releases` and `QHYBuilds` stay untracked
without replacing user rules. Directories committed before the rule was added must still be
untracked manually while keeping their local files.
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
Historical ResourceVersions are represented by immutable manifests in Releases and private build
records. Rollback changes only the resource pointer; no Bundle is copied or deleted.

HotUpdateOnly validates restored AOT metadata by file set, length, and SHA-256. A changed carrier
bundle is only a warning when its metadata payload still matches the published client snapshot;
actual metadata additions, removals, or byte changes remain blocked.
When an actual mismatch is detected from the Release window, an explicit one-shot **Force Publish**
button can continue the current HotUpdateOnly pipeline after a high-risk warning. This override is
never persisted or silently enabled for command-line builds. The release report records
`aotMetadataForcePublished` and `aotMetadataMismatchReason`; Full Package remains the safe default.
