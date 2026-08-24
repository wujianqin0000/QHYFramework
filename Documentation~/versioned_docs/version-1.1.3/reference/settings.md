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
| `refreshHostManifestEveryStartup` | true | 同一版本目录重复发布时仍检查远端 Manifest |
| `autoUnloadBundleWhenUnused` | false | 引用归零时自动卸载 Bundle |

每个 `PlatformRemoteProfile` 包含平台、`remoteBaseUrl` 和 `clientUpdateBaseUrl`。两个 URL 都不要填写版本号。

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
4. Full Package Build 会重新生成分析结果、校验 DLL、写入客户端配置，并冻结该平台的名单和元数据快照。
5. HotUpdateOnly 始终恢复对应已发布客户端的冻结快照，不会混入当前工程的新 AOT 输出。

切换平台后如果显示分析平台不一致，请为当前平台执行 `Generate/All` 或点击“刷新”。“刷新”只重新读取已有生成结果，不执行耗时的 HybridCLR 生成。

## FTP（仅编辑器）

| 字段 | 默认值 | 说明 |
|---|---:|---|
| `ftpHost` | 空 | FTP 主机名或 IP |
| `ftpPort` | 21 | FTP/FTPS 端口 |
| `ftpUserName` | 空 | 上传账号 |
| `ftpPassword` | 空 | 上传密码，不要提交到公共仓库 |

这些字段与 QHY Framework 发布窗口双向绑定。
