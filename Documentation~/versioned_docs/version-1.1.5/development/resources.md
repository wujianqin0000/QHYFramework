---
title: Prefab、配置与资源地址
---

# Prefab、配置与资源地址 {#resources}

## 组织资源

把资源放入 `Assets/Game/Content`：

| 目录 | 建议内容 | Address 示例 |
|---|---|---|
| `Common` | 角色、材质、ScriptableObject | `Player` |
| `UI` | UIKit Panel Prefab | `LoginPanel` |
| `Audio` | Music/Sound/Voice | `BgmLobby` |
| `Scenes` | 业务场景 | `Main` |

Collector 会启用 Addressable。短地址必须全局唯一，不要同时存在
`UI/Login.prefab` 和 `Common/Login.prefab` 并都使用 `Login`。

## 默认拆包与项目自定义

新项目首次初始化的当前默认规则是：

- Scenes 和位于 Collector 根目录的资源按文件独立；
- UI、Common、Audio 的子目录按 Collector 下一级功能目录打包；
- HotUpdate DLL/PDB、AOT Metadata、UIRoot 各自独立 Bundle。

因此建议目录直接表达更新关联性，例如
`UI/LevelSelect/Navigation`、`UI/LevelSelect/Background`、`Audio/Music`。不要把所有 UI
图片放进同一个大目录，也不要盲目让每张图片都独立 Bundle。

这些只是首次接入默认值。`InitializeOnly` 下框架不会在构建时重写 Collector；项目可在
YooAsset 窗口自由配置图集、共享依赖和自定义 PackRule。构建后查看
`release-report.json`：默认超过 4 MiB 输出主资源、依赖、上游引用和最大的 10 个源
文件；超过 16 MiB 会阻止发布，直到拆包或有意识地调整阈值。

## 同步与异步加载

```csharp
var loader = QFramework.ResLoader.Allocate();

var config = loader.LoadSync<GameConfig>("GameConfig");

loader.Add2Load<GameObject>("Player", (success, resource) =>
{
    if (success)
        UnityEngine.Object.Instantiate(resource.Asset as GameObject);
});
loader.LoadAsync();
```

旧代码中的 `ownerBundle` 仅保留签名兼容，实际定位只看 Address。发布校验发现地址
重名、Main/DLL 未收集会直接中止。

## 配置资源

可热更新配置建议使用 ScriptableObject、JSON `TextAsset` 或二进制
`TextAsset`，统一由 YooAsset 加载。不要将业务配置放到 `Resources`，否则它进入
客户端且不能通过当前资源清单替换。

**预期结果：** Build Report 中能按 Address 找到资源，运行时不出现
`Location is invalid`。
