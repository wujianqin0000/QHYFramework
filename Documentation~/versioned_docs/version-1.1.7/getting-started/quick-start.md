---
title: 十分钟入门
---

# 十分钟完成第一次热更新 {#quick-start}

## 1. 在编辑器运行

完成[安装](installation.md)后打开 `Assets/Scenes/Boot.unity`，将 `PlayMode` 设为
`EditorSimulateMode` 并运行。Boot 会初始化 YooAsset、加载 `Game.HotUpdate`，然后进入
Address 为 `Main` 的场景。

修改 `Assets/Game/Scripts/HotUpdate/HotUpdateTextDemo.cs` 中的显示文本，再次从 Boot
运行；新文本出现即证明示例热更程序集生效。

## 2. 第一次完整发布

:::danger 全新协议
schema v5 不兼容此前发布数据。先手工清空本地 `Releases`、
`QHYBuilds`、`ProjectSettings/QHYFramework/ReleaseState` 与服务器资源根。第一次必须
执行完整客户端构建，不能从 HotUpdateOnly 开始。
:::

1. 先把 Unity BuildTarget 切到 Android 或 Windows。在 `QHYFrameworkSettings` 填写项目唯一的游戏资源目录（例如 `my-game`）；当前平台 BaseURL 填服务器资源总根，例如 `https://aco.ai20.top`。QHY 自动追加 `/my-game/android` 或 `/my-game/windows`。
2. 打开 **QHY Framework → Release**，选择平台和版本。
3. 点击 **完整客户端构建**。Android 使用 `/android`，Windows 使用 `/windows`。
4. 选择 FTP，点击 **上传更新**；或把对应 `Releases/{gameDirectory}/{platform}` 手工上传到服务器相同相对位置。
5. 手工上传时最后上传 `origin/current`，再点击 **检查上传并完成发布**。

例如游戏目录为 `my-game` 时，产物位于 `Releases/my-game/android` 或
`Releases/my-game/windows`；报告位于
`QHYBuilds/{platform}/{clientVersion}/{resourceVersion}`。

## 3. 第一次热更新

修改热更代码或 YooAsset 资源，点击 **仅热更新构建**。手动发布时依次上传
`Upload/1-Files` 和 `Upload/2-Publish`，然后完成上传检查。没有实际变化时不会生成
ResourceVersion，也不能上传或重复登记。

常见错误见[排障](../help/troubleshooting.md)，完整目录和上传规则见
[FTP、手动上传与可选 CDN](../release/ftp-cdn.md)。
