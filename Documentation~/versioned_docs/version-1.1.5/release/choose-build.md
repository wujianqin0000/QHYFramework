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
建立客户端专属 AOT SHA-256 快照 → 使用新的 ResourceVersion 构建 GamePackage →
生成 Bundle Delta/体积诊断/上传计划 → 复制首包到 StreamingAssets → 在最后一次资源刷新后重新锁定本地
IL2CPP → 构建 Player → 校验原生 HybridCLR 解释器 → 客户端归档 →
SHA-256/报告/`latest.json`。非 Development Android 可使用默认 Debug Keystore；
自定义 Keystore 推荐但不强制，正式商店包必须自行保持签名一致。

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

如果 AOT Metadata Bundle 相对已发布资源版本发生任何变化，构建会阻止发布并要求新的
Full Package。当前工程 AOT 输出与旧客户端不同仍只提示兼容风险，因为它不会进入本次
资源包。

:::danger 兼容不等于一定安全
允许 HotUpdateOnly 是发布策略，不会把新 AOT 原生代码注入旧客户端。如果热更代码依赖旧客户端不存在的 AOT API，运行时仍会失败。
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
2. 选择正确平台、ClientVersion 和新的 ResourceVersion。
3. 确认远端根 URL 不含版本。
4. Full Package 时确认磁盘空间和 IL2CPP 模块。
5. 构建成功后先在本地客户端验证，再上传。
6. 检查超大 Bundle 报告；默认 4 MiB 警告、16 MiB 阻止发布。
