---
title: 热更新代码
---

# 编写热更新代码 {#hot-update-code}

## 放置位置

业务代码放在 `Assets/Game/Scripts/HotUpdate` 或其子目录。该目录由
`Game.HotUpdate.asmdef` 管理；移动到普通 `Assembly-CSharp` 后会进入 AOT。

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate
{
    public sealed class HotUpdateTextDemo : MonoBehaviour
    {
        [SerializeField] private Text targetText;

        private void Start()
        {
            targetText.text =
                "Game.HotUpdate v1.0.1 is running.";
        }
    }
}
```

把脚本挂到 Main 的 `Main` 对象，将 UGUI Text 拖到 `Target Text`。修改字符串后
执行 HotUpdateOnly 即可验证远端代码更新。

## 入口与反射

当前标准流程通过场景上的热更 MonoBehaviour 启动，不需要额外 Marker。若你建立
纯代码入口，AOT 层只能用程序集名/类型名反射调用，避免静态引用热更类型。

## 开发规则

- 避免把 Unity Editor API 放进运行时程序集。
- 新增程序集引用后确认 `Game.HotUpdate.asmdef` 已声明。
- 引入新的泛型组合时，完整构建生成对应 AOT 元数据；面向旧客户端热更时保留旧基线。
- 删除/重命名序列化字段前使用 `FormerlySerializedAs` 或迁移场景数据。

**预期结果：** EditorSimulateMode 从 Boot 进入 Main 后脚本执行。

**常见错误：** `Game.HotUpdate.dll` Address 无效通常是生成文件未复制或 Collector
缺失；从 Release 窗口重新构建，不要直接复制历史 DLL。
