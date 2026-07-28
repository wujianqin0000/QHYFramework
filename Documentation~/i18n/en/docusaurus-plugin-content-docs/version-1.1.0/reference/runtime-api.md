---
title: Runtime API
---

# Runtime API {#runtime-api}

Runtime types use the `GameIntegration` namespace.

```csharp
var runtime = new GamePackageRuntime(settings, playMode);
runtime.ProgressChanged += value => Debug.Log(value.Progress);
await runtime.StartAsync();
if (runtime.NeedsDownloadConfirmation)
    await runtime.ConfirmDownloadAsync();
await runtime.LoadHotUpdateAssembliesAsync();
await runtime.EnterStartupSceneAsync();
```

`StartAsync` prepares package, version, manifest, and download list. It does not
bypass confirmation.

```csharp
SceneHandle scene = await YooAssetSceneKit.LoadSceneAsync(
    "Main", LoadSceneMode.Single);
SceneHandle sync = YooAssetSceneKit.LoadSceneSync("Lobby");
await YooAssetSceneKit.UnloadSceneAsync("Main");
```

`ClientUpdateService.CheckAsync`, `DownloadAsync`, and
`InstallAndRestartAsync` implement the full-client flow and are normally driven
by `GameBootstrap`.

`StartupProgress` exposes state, normalized progress, file counts, bytes, and
message. A custom progress Image must use Filled type. `IResourceAddressResolver`
maps legacy short names to valid YooAsset Addresses.
