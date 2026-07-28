---
title: YooAsset 基础
---

# YooAsset 基础 {#yooasset}

YooAsset 负责资源构建、寻址、下载、缓存、加载和释放。

## 七个核心名词

| 名词 | 含义 |
|---|---|
| Package | 一组独立版本和清单的资源集合；默认是 `GamePackage` |
| Collector | 编辑器中的收集规则，把目录/资产纳入 Package |
| Address | 运行时稳定短名，例如 `Main` 或 `LoginPanel` |
| Manifest | 某版本的资产、Bundle、依赖和哈希索引 |
| Bundle | 实际下载与加载的归档文件 |
| Handle | 资产/场景加载生命周期凭据，必须释放 |
| Cache | 下载到本机且校验通过的 Bundle |

Address 不是文件路径。构建后 Bundle 文件名可以变化，业务代码只依赖稳定 Address。
短地址必须全局唯一；发布校验会阻止重名。

## 四种 PlayMode

| 模式 | 场景 | 是否访问远端 |
|---|---|---|
| `EditorSimulateMode` | 编辑器快速迭代 | 否 |
| `OfflinePlayMode` | 单机完整包 | 否 |
| `HostPlayMode` | 内置首包 + CDN 热更新 | 是 |
| `WebPlayMode` | WebGL | 由 Web 服务器提供 |

QHY Framework 复刻 YooAsset 示例的 PlayMode 选择：编辑器默认模拟；打包时只有当前
值为 `EditorSimulateMode` 才自动改为 `HostPlayMode`，显式选择的 Offline/Web
不会被覆盖。

## 版本与缓存

Host 启动先请求 `<ResourceBaseUrl>/<Version>/<PackageName>.version`，再获取该
版本 Manifest。远端失败时尝试已缓存可用清单；首次安装且没有缓存则留在 Boot
显示重试。版本目录不会自动递增，只有发布者手动改变平台版本才创建新目录。

## 句柄为什么重要

YooAsset 用引用计数判断 Bundle 是否仍被使用。`YooAssetRes` 会把 `AssetHandle`
交给 QFramework 的资源对象持有，并在 `Recycle2Cache` 时释放。绕过适配器直接调用
YooAsset 时，你要自己保存并释放 Handle。
