---
title: QHYFrameworkSettings 字段
---

# QHYFrameworkSettings 全字段参考 {#settings}

资产默认位于 `Assets/Game/Config/QHYFrameworkSettings.asset`。

![QHYFrameworkSettings 的 YooAsset 与平台远端配置](/img/screenshots/settings.webp)

## YooAsset

| 字段 | 默认值 | 说明 |
|---|---:|---|
| `packageName` | `GamePackage` | YooAsset Package 名，构建和服务器文件名前缀必须一致 |
| `platformProfiles` | 空 | 各平台 Resource/Client 根 URL；根地址不含版本 |
| `editorSimulatePackageRoot` | 空 | 最近模拟构建路径，仅诊断，模拟启动会重建 |
| `requestTimeoutSeconds` | 60 | 版本、Manifest 请求超时 |
| `downloadConcurrency` | 8 | 并行下载数；移动网络可降低 |
| `downloadRetryCount` | 2 | 单文件重试次数 |
| `downloadMaxRequestPerFrame` | 1 | 每帧发起请求上限，控制峰值 |
| `downloadWatchdogTimeoutSeconds` | 10 | 无下载进展的看门狗时间；0 关闭 |
| `copyBuiltinPackageManifest` | true | Full Package 把内置首包清单复制到 StreamingAssets |
| `clearUnusedCacheAfterUpdate` | true | 更新后清理当前 Manifest 不再使用的缓存 |
| `refreshHostManifestEveryStartup` | true | 同一版本目录重复发布时仍刷新远端清单 |
| `autoUnloadBundleWhenUnused` | false | 引用归零时自动卸载 Bundle |

## 平台远端配置

每个 `PlatformRemoteProfile` 包含：

| 字段 | 说明 |
|---|---|
| `platform` | Windows、Android、iOS、MacOS、Linux、WebGL |
| `remoteBaseUrl` | 资源根，如 `https://example.com/CDN/PC` |
| `clientUpdateBaseUrl` | 客户端根，如 `https://example.com/Client/PC` |

Windows/Android 的版本与 URL 完全隔离。当前只实现 Windows、Android 客户端安装器。

## FTP（仅编辑器）

| 字段 | 默认值 | 说明 |
|---|---:|---|
| `ftpHost` | 空 | 主机名或 IP |
| `ftpPort` | 21 | FTP/FTPS 端口 |
| `ftpUserName` | 空 | 上传账号 |
| `ftpPassword` | 空 | 上传密码，避免提交公共仓库 |

Release 窗口和设置资产双向绑定这些字段。

## 启动与程序集

| 字段 | 默认值 | 说明 |
|---|---:|---|
| `startupSceneAddress` | `Main` | 首个业务场景 Address |
| `hotUpdateAssemblies` | `Game.HotUpdate` | 程序集名与 `.dll` 资源 Address 列表 |
| `aotMetadataAssemblyNames` | 空 | 需补充元数据的裁剪 AOT DLL 名 |

`hotUpdateAssemblies.assetAddress` 默认 `Game.HotUpdate.dll`，实际资产文件可以是
`.dll.bytes`，Address 仍保持配置值。
