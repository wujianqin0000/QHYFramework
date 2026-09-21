---
title: 选择构建类型
---

# Full Package 还是 HotUpdateOnly {#choose-build}

![QHY Framework 发布窗口中的版本自动化、完整构建与热更新构建入口](/img/screenshots/release-window.webp)

| 变化 | 推荐构建 | 原因 |
|---|---|---|
| `Game.HotUpdate` 业务代码 | HotUpdateOnly | DLL 可运行时加载 |
| Main、Prefab、UI、音频、配置 | HotUpdateOnly | YooAsset 资源 |
| Boot/AOT 代码、原生插件 | Full Package | 已转为原生代码 |
| Player Settings、Application Identifier | Full Package | 属于客户端 |
| Android 签名、权限、Manifest | Full Package | APK 原生配置 |
| 不确定兼容性的大改动 | Full Package | 由客户端升级兜底 |

## Full Package Build

固定流程：校验本地 HybridCLR 及版本 → HybridCLR Generate/All → 复制 DLL/AOT 元数据 →
建立客户端专属 AOT SHA-256 快照 → 按 YooAsset 真实收集依赖生成 Engine Stripping 保护 →
使用新的 ResourceVersion 构建 GamePackage →
生成 Bundle Delta/体积诊断/上传计划 → 复制首包到 StreamingAssets → 在最后一次资源刷新后重新锁定本地
IL2CPP → 构建 Player → 校验原生 HybridCLR 解释器 → 客户端归档 →
SHA-256/报告/`latest.json`。非 Development Android 可使用默认 Debug Keystore；
自定义 Keystore 推荐但不强制，正式商店包必须自行保持签名一致。

### 外置资源 Engine Stripping 保护

FullPackage 在最后一次 Collector 配置完成后执行依赖收集，且使用 `simulateBuild: false`，不会
沿用只做地址校验、会忽略依赖的模拟收集结果。分析范围包含全部主收集资源及其 YooAsset
依赖。目前会识别：

- `.anim`、`AnimationClip`、`Motion`；
- `.controller`、`RuntimeAnimatorController`；
- `AnimatorOverrideController`；
- `Avatar`、`AvatarMask`；
- 由 `ModelImporter` 导入且包含 AnimationClip 的 FBX/模型资源。

检测到任一外置动画资源时，框架覆盖生成：

```xml
<linker>
  <assembly fullname="UnityEngine.AnimationModule" preserve="all" />
</linker>
```

文件固定在 `Assets/Game/Generated/QHYLink/link.xml`，随后同步导入 AssetDatabase，再进入
YooAsset 构建和 `BuildPipeline.BuildPlayer`。QHY 不修改
`Assets/HybridCLRGenerate/link.xml`，所以再次执行 `HybridCLR Generate/All` 不会丢失这项保护。

当 `PlayerSettings.stripEngineCode == true`、外置资源需要动画模块、但最终 QHY link.xml
缺失、损坏或没有 `preserve="all"` 时，发布校验直接阻止 FullPackage，并提示 Android
IL2CPP 动画可能无法播放。`release-report.json` 记录 `stripEngineCode`、
`engineStrippingLinkPath`、`engineStrippingProtectedAssemblies`、
`externalAnimationAssetCount` 和 `externalAnimationAssets`，可用于审计实际保护结果。关闭 Engine Stripping 时仍会生成
link.xml，确保之后重新启用裁剪不会依赖手工配置。

### 1.1.3 构建保护

Full Package 不再只在 `Generate/All` 前设置一次工具链。发布会话会阻止依赖初始化器
覆盖 HybridCLR 配置，并在紧邻 `BuildPipeline.BuildPlayer` 前重新断言：

- `HybridCLRSettings.enable == true`；
- `HybridCLRSettings.useGlobalIl2cpp == false`；
- 进程级 `UNITY_IL2CPP_PATH` 等于 `SettingsUtil.LocalIl2CppDir`；
- HybridCLR 包版本等于本地 `libil2cpp-version.txt`；
- 本地目录同时包含 `libil2cpp/hybridclr` 和已知 IL2CPP 编译器。

任一断言失败都会抛出构建失败，不会继续归档或上传。Player 构建成功后还会检查
HybridCLR 解释器特有标记，避免发布一个实际上由 Unity 原生 IL2CPP 生成的客户端。

## HotUpdateOnly Build

只生成已发布客户端的热更 DLL 和 YooAsset 资源，不构建 Player、不更新客户端
`latest.json`，也绝不写 `PlayerSettings.bundleVersion`。流程不会执行 `Generate/All`、
不会分析当前 AOT 输出，而是按 ClientVersion 恢复 Full Package 保存的元数据快照，并
逐文件验证 SHA-256。

构建不再仅凭 AOT Metadata Bundle 哈希变化阻断发布。它会在 YooAsset 构建后再次比较
最终 `Assets/Game/Generated/AOTMetadata/*.dll.bytes` 与客户端快照的文件集合、长度和
SHA-256，并确认每个文件仍被当前 YooAsset Manifest 收集：负载完全一致时，即使承载
Bundle 因打包布局或依赖变化而 Changed，也只警告并允许发布；新增、缺失、未收集或字节
变化仍会阻断并要求新的 Full Package。

发生实际负载不一致时，Release 窗口会显示高风险确认对话框，其中提供 **强制发布** 按钮。
确认后会继续当前流水线，不需要换 ResourceVersion 重跑；这个授权只对本次点击有效，不会写入
EditorPrefs，也不会让命令行发布自动放行。`release-report.json` 会永久记录
`aotMetadataForcePublished: true` 和 `aotMetadataMismatchReason`。除 AOT 不一致外的版本、签名、
资源完整性、Bundle 体积与上传校验不会被绕过。

当前工程 AOT 输出与旧客户端不同仍只提示兼容风险，因为 HotUpdateOnly 恢复的是旧客户端
快照，新 AOT 原生代码不会进入本次资源包。

:::danger 兼容不等于一定安全
允许或强制执行 HotUpdateOnly 都不会把新 AOT 原生代码注入旧客户端。如果热更代码依赖旧客户端
不存在或签名已变化的 AOT API，可能出现 `MissingMethodException`、补充元数据加载失败或崩溃。
无法证明兼容时必须取消并执行 Full Package。
:::

## Collector 所有权

发布前调用 Collector 配置不再等于重建配置：

- `InitializeOnly`（默认）：目标 Package 不存在时创建一次；之后只校验，不改用户设置。
- `ManagedGroupsOnly`：只替换所有 Collector 都带 `QHYFramework.Managed:v2` 的组。
- `External`：Package 完全由项目维护，QHY 仅校验。

从 1.1.4 升级的未标记 Collector 不会自动被接管。若选择 ManagedGroupsOnly，先在
YooAsset Collector 窗口确认旧默认 Collector 并添加标记；检测到同路径未标记项时构建
会停止，不会按组名删除或产生重复 Address。

## 构建前清单

1. 保存所有打开场景。
2. 选择正确平台；勾选“版本自动化”让 ClientVersion 与 ResourceVersion 一起计算，或取消勾选后手工填写两者。
3. 确认资源 BaseURL 不含平台目录和版本。
4. Full Package 时确认磁盘空间和 IL2CPP 模块。
5. 构建成功后先在本地客户端验证，再上传。
6. 检查超大 Bundle 报告；默认 4 MiB 警告、16 MiB 阻止发布。
