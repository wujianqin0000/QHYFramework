---
title: Windows 与 Android 发布
---

# Windows 与 Android 发布 {#windows-android}

## Windows64

Full Package 输出 Unity 客户端文件夹，并打成
`Client_Windows64_v1.0.1.zip`。上传使用 ZIP，避免把几十个文件逐一发送。体积主要
来自 IL2CPP `GameAssembly.dll`、UnityPlayer、托管数据、首包资源和 Debug 符号。

减小体积：

- 关闭 Development Build、Script Debugging、Autoconnect Profiler。
- Release 不上传 `.pdb`、`_BurstDebugInformation_DoNotShip` 等调试产物。
- 资源尽量放 YooAsset 远端包，首包只保留启动必需内容。
- Player Settings 使用合适的 Managed Stripping Level 与纹理压缩。

## Android

安装 Android Build Support 后切换平台，配置 Application Identifier。语义版本会
生成递增 `bundleVersionCode`。Full Package 输出 APK，整包更新要求：

- 新 APK 与已安装应用的 Application Identifier 相同。
- 签名证书相同；正式发布强烈建议自定义 Keystore。
- 新 `versionCode` 必须更高。
- Android 8+ 用户允许该应用“安装未知应用”。

框架提供 FileProvider 和安装 Intent。用户取消系统安装后仍停留 Boot，可重试。

## 签名基线

工具记录 Android 签名基线并提示变化。没有自定义 Keystore 不阻止构建或上传，但
默认 Debug 签名不适合商店/长期生产更新：换签名后旧用户无法覆盖安装。
