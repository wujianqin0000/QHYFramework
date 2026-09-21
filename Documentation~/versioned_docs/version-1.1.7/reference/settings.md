---
title: QHYFrameworkSettings 字段
---

# QHYFrameworkSettings 全字段参考 {#settings}

配置资产默认位于 `Assets/Game/Config/QHYFrameworkSettings.asset`。

## 平台资源

`gameDirectory` 是项目级游戏资源目录，例如 `my-game`。它在所有平台间共用，只允许小写字母、数字、点、短横线和下划线，不能包含斜杠、`.` 或 `..`。同一服务器资源根中的每个游戏必须使用不同值。

Inspector 不提供平台下拉框，也不同时展开全部 Profile，只显示当前 Unity BuildTarget 对应的平台配置。切换 BuildTarget 后自动显示新平台的配置，各平台 `baseUrl` 仍独立保存，不会互相覆盖。缺少当前平台 Profile 时点击“创建当前平台配置”。

可以直接在 QHY 发布工具中修改“目标平台”；它会真正切换 Unity Active Build Target，并刷新
本 Inspector，而不是只改变一次构建参数。平台切换期间可能发生资源导入和脚本编译。

BaseURL 只填写服务器资源总根，例如 `aco.ai20.top` 或 `https://aco.ai20.top`，不要包含游戏目录、`/android`、`/windows`、`/cdn` 或 `/origin`。裸域名自动补全为 HTTPS；从富文本复制的完整 Markdown 链接或尖括号 URL 也会提取并保存为规范地址。框架派生 `cdnRoot = baseUrl/{gameDirectory}/{platform}/cdn` 和 `originRoot = baseUrl/{gameDirectory}/{platform}/origin`。Windows、Android、iOS、macOS、Linux、WebGL 都可保存配置；同一平台不能重复，`Unknown` 禁止使用。发布时 URL 语法只校验目标平台，其他平台未填完不会阻止当前构建；重复项和 `Unknown` 仍全局阻止。一键 Player 发布首期支持 Windows 与 Android。

URL 必须是 HTTP(S) 绝对地址，不允许查询、片段、凭据，也不能以平台目录、`/cdn` 或 `/origin` 结尾。旧 `resourceBaseUrl` 已删除且不迁移。

## YooAsset 与下载

| 字段 | 默认值 | 说明 |
|---|---:|---|
| `editorSimulatePackageRoot` | 空 | 编辑器模拟包目录，仅诊断 |
| `requestTimeoutSeconds` | 60 | 版本和 Manifest 请求超时 |
| `downloadConcurrency` | 8 | 并行下载数量 |
| `downloadRetryCount` | 2 | 单文件重试次数 |
| `downloadMaxRequestPerFrame` | 1 | 每帧新建请求上限 |
| `downloadWatchdogTimeoutSeconds` | 10 | 无进度超时，0 关闭 |
| `copyBuiltinPackageManifest` | true | FullPackage 内置首包 Manifest |
| `clearUnusedCacheAfterUpdate` | true | 更新后清理不再使用的缓存 |
| `refreshHostManifestEveryStartup` | true | 每次启动请求 Origin 资源指针 |
| `collectorManagementMode` | `InitializeOnly` | Collector 所有权策略 |
| `bundleWarningThresholdMiB` | 4 | Bundle 体积警告阈值 |
| `bundleErrorThresholdMiB` | 16 | Bundle 体积阻断阈值 |

包名固定为 `GamePackage`。

## 启动和程序集

`startupSceneAddress` 默认 `Main`；`hotUpdateAssemblies` 默认 `Game.HotUpdate`。AOT 自动名单由 FullPackage 维护，额外名单只用于特殊补充。

## FTP

FTP 不属于 Settings。Host、Port、用户名、FTPS 和被动模式按目标平台保存在 Release 窗口本机配置中；密码可存 Windows 凭据管理器。FTP 不提供 RemoteRoot，账号登录根目录作为多个游戏共用的资源根，上传路径从 `{gameDirectory}/{platform}` 开始。
