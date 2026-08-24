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
构建 GamePackage → 复制首包到 StreamingAssets → 在最后一次资源刷新后重新锁定本地
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

只生成当前客户端版本的热更 DLL 和 YooAsset 资源，不构建 Player、不更新客户端
`latest.json`。AOT 输出变化会警告但不会阻止，发布者应确认新代码仍兼容历史客户端。

:::danger 兼容不等于一定安全
允许 HotUpdateOnly 是发布策略，不会把新 AOT 原生代码注入旧客户端。如果热更代码依赖旧客户端不存在的 AOT API，运行时仍会失败。
:::

## 构建前清单

1. 保存所有打开场景。
2. 选择正确平台与该平台版本。
3. 确认远端根 URL 不含版本。
4. Full Package 时确认磁盘空间和 IL2CPP 模块。
5. 构建成功后先在本地客户端验证，再上传。
