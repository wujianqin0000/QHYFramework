---
title: 运行时 API
---

# 运行时 API {#runtime-api}

命名空间为 `GameIntegration`。

## GamePackageRuntime

```csharp
var runtime = new GamePackageRuntime(settings, playMode);
runtime.ProgressChanged += progress => Debug.Log(progress.Progress);
runtime.StateChanged += state => Debug.Log(state);

await runtime.StartAsync();
if (runtime.NeedsDownloadConfirmation)
    await runtime.ConfirmDownloadAsync();

await runtime.LoadHotUpdateAssembliesAsync();
await runtime.EnterStartupSceneAsync();
```

`StartAsync` 完成 Package 初始化、版本/Manifest 与下载清单准备；它不会绕过用户确认
自动下载。失败时 `State`/`Error` 与事件可驱动自定义 Boot UI。

## YooAssetSceneKit

```csharp
SceneHandle handle = await YooAssetSceneKit.LoadSceneAsync(
    "Main", LoadSceneMode.Single);

SceneHandle sync = YooAssetSceneKit.LoadSceneSync("Lobby");
await YooAssetSceneKit.UnloadSceneAsync("Lobby");
```

Handle 会由 SceneKit 登记。Unload 按 Address 卸载仍然有效的 Additive 场景。

## ClientUpdateService

```csharp
var service = new ClientUpdateService(settings, playMode);
await service.CheckAsync();
if (service.HasUpdate)
{
    await service.DownloadAsync();
    await service.InstallAndRestartAsync();
}
```

通常由 `GameBootstrap` 调用。自定义 Boot 可以订阅其进度，但仍应保持“校验通过才安装”
和 Mandatory 不可跳过的规则。

## StartupProgress

包含 `State`、`Progress`、当前/总文件数、当前/总字节与 `Message`。进度范围 `0..1`，
BootUI 的 `ProgressFill.fillAmount` 使用该值；自定义 Image 必须把 Type 设为 Filled。

## IResourceAddressResolver

解析 QFramework 旧短名为 YooAsset Address。默认直接使用短名；需要前缀/迁移表时可
提供自定义实现，但解析结果仍必须存在于当前 Manifest。
