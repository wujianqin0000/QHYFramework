---
title: Windows 与 Android 发布
---

# Windows 与 Android 发布 {#windows-android}

## Windows64

Full Package 输出 Unity 客户端文件夹，并打成
`Client_Windows64_v1.0.1.zip`。上传使用 ZIP，避免把几十个文件逐一发送。体积主要
来自 IL2CPP `GameAssembly.dll`、UnityPlayer、托管数据、首包资源和 Debug 符号。

QHY Framework 1.1.3 会在构建后扫描 `GameAssembly.dll`，要求存在 HybridCLR
解释器标记 `InterpreterImage::GetClassFromToken`。缺少该标记表示 Player 很可能用了
Unity 全局 IL2CPP；该次发布会立即失败，不会生成可上传的完整发布报告。

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

构建 APK/AAB 后，框架会打开压缩包并检查每个 ABI 下的 `libil2cpp.so`。任何 ABI
缺少 HybridCLR 解释器标记都会阻止发布。该检查针对原生解释器代码，不能用
`LoadMetadataForAOTAssembly` 字符串代替，因为普通 Unity IL2CPP 产物也可能包含后者的
托管方法元数据。

## 签名基线

工具记录 Android 签名基线并提示变化。没有自定义 Keystore 不阻止构建或上传，但
默认 Debug 签名不适合商店/长期生产更新：换签名后旧用户无法覆盖安装。
