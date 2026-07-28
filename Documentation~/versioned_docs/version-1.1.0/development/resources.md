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
