---
title: 十分钟入门
---

# 十分钟完成第一次热更新 {#quick-start}

## 前置条件

项目已按[安装指南](installation.md)导入，Console 无红色错误，当前目标平台为
Windows64 或 Android。

## 1. 运行 Boot

打开 `Assets/Scenes/Boot.unity`，选中 `GameBootstrap` 对象，确认
`PlayMode = EditorSimulateMode`，点击 Play。编辑器模拟模式不访问远端，也不会
检查客户端整包版本。

Boot 依次初始化 YooAsset、加载模拟 Manifest、加载热更程序集并进入 Address 为
`Main` 的场景。Main 中的文本应显示：

![Boot 启动界面正在检查和下载客户端更新](/img/screenshots/boot-update.webp)

```text
Game.HotUpdate code is running.
Change this text and publish HotUpdateOnly to verify hot update.
```

![Main 场景中的 Game.HotUpdate 文本示例](/img/screenshots/main-hot-update.webp)

## 2. 修改热更代码

打开 `Assets/Game/Scripts/HotUpdate/HotUpdateTextDemo.cs`，只修改赋给
`targetText.text` 的字符串，保存并等待 Unity 编译。不要把脚本移出
`Game.HotUpdate.asmdef` 所在目录。

## 3. 再次运行

重新从 Boot Play。Main 文本应变为新内容。这证明业务脚本属于热更程序集，且场景
上的序列化引用仍然有效。

## 4. 做第一次正式构建

打开 **QHY Framework → Release**：

1. 选择目标平台与版本 `v1.0.0`。
2. 在 `QHYFrameworkSettings` 为当前平台填写 Resource Base URL。
3. 点击 **Full Package Build**，生成首个客户端、资源包和 `latest.json`。
4. 上传资源和客户端。首次基线必须让用户安装一次。
5. 以后只改热更代码或资源时，点击 **HotUpdateOnly Build** 并仅上传热更新包。

## 预期结果

- EditorSimulateMode 能从 Boot 进入 Main。
- Main 文本来自 `Game.HotUpdate` 中的代码。
- 本地发布快照位于
  `Releases/default/Windows64/v1.0.0/Revisions/v1.0.0-r0001`（Android 使用对应平台目录）；
  同一客户端版本的后续修订继续归档在 `Revisions` 下。

## 常见错误

- `Location is invalid: Game.HotUpdate.dll`：项目未生成热更 DLL 或 Collector 未收集。
  直接从发布窗口构建；不要手工伪造 `.bytes`。
- 找不到模拟版本文件：确认编辑器目标平台与 YooAsset 模拟构建平台一致。
- Main 没有 Camera：删除错误的空 Main 后让初始化补齐，或手动添加 Camera。

**下一步：** 阅读[QHY 启动与更新工作流](../concepts/qhy-workflows.md)。
