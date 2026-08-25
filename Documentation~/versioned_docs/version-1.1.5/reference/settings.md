---
title: QHYFrameworkSettings 字段
---

# QHYFrameworkSettings 全字段参考 {#settings}

配置资产默认位于 `Assets/Game/Config/QHYFrameworkSettings.asset`。

![QHYFrameworkSettings 的 YooAsset 与平台远端配置](/img/screenshots/settings.webp)

## YooAsset

| 字段 | 默认值 | 说明 |
|---|---:|---|
| `packageName` | `GamePackage` | YooAsset Package 名称，必须与构建产物一致 |
| `platformProfiles` | 空 | 各平台独立的资源和客户端更新根地址，地址不包含版本 |
| `editorSimulatePackageRoot` | 空 | 最近一次编辑器模拟构建目录，仅用于诊断 |
| `requestTimeoutSeconds` | 60 | 版本和 Manifest 请求超时 |
| `downloadConcurrency` | 8 | 并行下载数量 |
| `downloadRetryCount` | 2 | 单文件重试次数 |
| `downloadMaxRequestPerFrame` | 1 | 每帧新建下载请求的上限 |
| `downloadWatchdogTimeoutSeconds` | 10 | 下载无进度超时，设为 0 时关闭 |
| `copyBuiltinPackageManifest` | true | Full Package 将首包 Manifest 复制到 StreamingAssets |
| `clearUnusedCacheAfterUpdate` | true | 更新后清理当前 Manifest 不再使用的缓存 |
| `refreshHostManifestEveryStartup` | true | 每次启动请求 version 指针以发现新的 ResourceVersion |
| `autoUnloadBundleWhenUnused` | false | 引用归零时自动卸载 Bundle |
| `collectorManagementMode` | `InitializeOnly` | Collector 所有权：仅初始化、仅托管标记组或完全外部维护 |
| `bundleWarningThresholdMiB` | 4 | 构建报告中的 Bundle 体积警告阈值，0 关闭 |
| `bundleErrorThresholdMiB` | 16 | Bundle 体积阻断阈值，0 关闭 |
| `ignoreTypeTreeChangesForIncrementalBuild` | false | 高级 TypeTree 影响诊断；SBP 不会因此忽略 TypeTree |

每个 `PlatformRemoteProfile` 包含平台、`remoteBaseUrl` 和 `clientUpdateBaseUrl`。两个 URL 都不要填写版本号。

`InitializeOnly` 不会修改已经存在的 Package。`ManagedGroupsOnly` 只处理所有 Collector
都带 `QHYFramework.Managed:v2` 的组；同路径未标记项会产生迁移错误而不是被删除。
`External` 要求项目提前创建 Package，QHY 只执行地址和收集校验。

YooAsset 3.0.4 的 Scriptable Build Pipeline 不支持 `IgnoreTypeTreeChanges`。高级开关
只让差异报告标记“资源和依赖未变但 Bundle 哈希变化”的疑似 TypeTree 影响；启用前仍
必须用旧客户端回归场景、UI 和公共 Prefab。

## 启动和程序集

| 字段 | 默认值 | 说明 |
|---|---:|---|
| `startupSceneAddress` | `Main` | 第一个业务场景的 YooAsset Address |
| `hotUpdateAssemblies` | `Game.HotUpdate` | 热更新程序集名称和 DLL Address |
| `aotMetadataAssemblyNames` | 自动 | 客户端运行时使用的最终 AOT 元数据名单，由工具维护 |
| `aotMetadataExtraAssemblyNames` | 空 | 自动分析之外的特殊 AOT 程序集 |

热更新程序集候选来自 `HybridCLRData/HotUpdateDlls/<平台>`，并且必须已在 HybridCLR 中配置。勾选后默认 Address 为 `<AssemblyName>.dll`，可在高级配置中修改。

### AOT 元数据自动化

QHY Framework 读取 HybridCLR 生成的 `AOTGenericReferences.PatchedAOTAssemblyList`，自动确定需要补充元数据的程序集：

1. 执行 HybridCLR `Generate/All` 后，自动名单会同步到当前 Build Target。
2. 自动检测项在 Inspector 中锁定，不能手工取消。
3. 只有 HybridCLR 未能覆盖的特殊需求才应勾选“额外 AOT 元数据程序集”。
4. Full Package Build 会重新生成分析结果、校验 DLL、写入客户端配置，并按平台和 ClientVersion 冻结名单与逐文件 SHA-256 快照。
5. HotUpdateOnly 始终恢复对应已发布客户端的冻结快照，不会混入当前工程的新 AOT 输出；校验失败或 AOT Bundle 变化会阻止发布。

切换平台后如果显示分析平台不一致，请为当前平台执行 `Generate/All` 或点击“刷新”。“刷新”只重新读取已有生成结果，不执行耗时的 HybridCLR 生成。

## FTP（仅编辑器）

| 字段 | 默认值 | 说明 |
|---|---:|---|
| `ftpHost` | 空 | FTP 主机名或 IP |
| `ftpPort` | 21 | FTP/FTPS 端口 |
| `ftpUserName` | 空 | 上传账号 |
密码不是设置字段，也不会写入 Asset、ProjectSettings 或 UserSettings。发布窗口只在当前
Editor 会话内保存输入；关闭窗口或上传完成/失败/取消后清空。CI 使用
`QHY_FTP_PASSWORD`，必要时用 `QHY_FTP_USERNAME` 覆盖序列化账号。错误消息会脱敏，
发布结束后还会扫描项目文本文件中的密码残留。
