---
title: 故障排查
---

# 故障排查 {#troubleshooting}

先看异常中的 **URL、Package 名、平台、版本和 Stage**，再按本页定位。不要通过修改
三套上游框架源码掩盖配置问题。

## 远端 404 或 Package 名不一致

**现象：** 请求 `GamePackage.version` 404，但服务器只有
`DefaultPackage.version`。

**原因：** `QHYFrameworkSettings.packageName` 与 YooAsset 构建产物不一致，或
Resource Base URL 已手写版本/平台错误。

**处理：**

1. 确认服务器路径为 `<remoteBaseUrl>/<PlayerVersion>/<packageName>.version`。
2. 确认 Windows/Android Profile 没混用。
3. 用浏览器直接打开 URL；大小应是几字节版本文本，不是 HTML。
4. 重新构建并完整上传该版本目录。

## StreamingAssets 缺少内置首包

Host/Offline 初始化请求 `StreamingAssets/yoo/...version` 404，表示客户端没有 Full
Package 复制的首包。编辑器开发切回 EditorSimulateMode；正式客户端重新执行 Full
Package，确认 `copyBuiltinPackageManifest` 开启。

## 找不到 EditorSimulate 清单

不要复用另一项目/平台的 `Bundles/.../Simulate` 路径。切换目标平台后重新运行
Boot，自动准备会生成模拟清单。`editorSimulatePackageRoot` 只用于诊断。

## 未保存场景 ErrorCode101

YooAsset 拒绝构建 Dirty Scene。保存全部场景；如果不想保留 Untitled 默认场景，
直接关闭且不保存。发布流程会在构建前检查并给出明确错误。

## Game.HotUpdate.dll Address 无效

检查：

- `Game.HotUpdate.asmdef` 存在且代码无编译错误。
- Release 构建已生成 `Assets/Game/Generated/HotUpdate/*.bytes`。
- Collector 的 HotUpdate 组收集该目录并启用 Addressable。
- 设置中的 Address 是 `Game.HotUpdate.dll`，与 Build Report 一致。

EditorSimulateMode 也需要可收集的生成文件；从 Release 窗口构建会自动编译和复制。

## AOT 变化警告

当前工程 AOT 输出与已发布客户端不同只会警告，因为 HotUpdateOnly 不读取它。AOT
Metadata Bundle Added/Changed 本身也不再直接阻断：若恢复后的全部元数据文件集合、长度和
SHA-256 与客户端快照一致，说明只是 Bundle 外壳、布局或依赖变化，可以继续发布。

只有快照校验失败、文件新增/缺失、未被当前 Manifest 收集或实际字节变化才会阻断。不要复制当前
`AssembliesPostIl2CppStrip` 覆盖快照；热更代码若依赖旧客户端不存在的新 AOT API，或确实
需要新 AOT 原生能力，仍必须执行 Full Package 建立新客户端基线。

如果明确确认变化不影响旧客户端，可在检测对话框点击 **强制发布**，流水线会从当前阶段继续。
该按钮只授权当前一次发布，命令行仍默认阻止；结果可通过 `release-report.json` 中的
`aotMetadataForcePublished` 与 `aotMetadataMismatchReason` 审计。强制发布只绕过这一项 AOT
负载不匹配，不绕过其他发布校验。无法确认旧客户端兼容时不要使用。

## 取消构建后 GenerateStripedAOTDlls failed

QHY 会清理 `StrippedAOTDllsTempProj/<Target>` 与 `Temp/StagingArea`，重置
`buildScriptsOnly`。等待 Unity 完成重新编译后直接重试。仍失败时关闭残留 Unity/Bee
进程，确认磁盘空间和 IL2CPP 模块完整。

## YooAsset 输出目录已存在 ErrorCode115

当前发布系统将 ResourceVersion 视为不可变发布单元，不再自动删除同版本结果。将
`v1.0.4-r0007` 递增为 `v1.0.4-r0008` 后重建。不要删除已经上传的旧 Manifest 或
Bundle；它们用于在线旧客户端和回滚。

## 修改一个 UI 却下载十几 MiB

这通常不是客户端重复下载全部资源，而是新依赖被 YooAsset 合并进较大的共享 Bundle。
查看 `release-report.json` 的 `changedBundles`、`mainAssets`、`dependencyAssets` 和
`referencedBy`，以及 Console 的前 10 大源资源。按面板/图集/功能目录调整 Collector，
不要把所有 UI 图片固定为一个 `PackDirectory`。完整 CDN 快照大小也不等于 FTP 或
客户端下载量，以 Release 窗口显示的三种大小为准。

## Collector 修改被覆盖或出现重复 Address

确认 `collectorManagementMode`。`InitializeOnly` 不改已有 Package；`External` 只校验。
切换 `ManagedGroupsOnly` 前，旧默认 Collector 必须经人工确认并添加
`QHYFramework.Managed:v2`。框架检测到同路径未标记 Collector 会主动失败，以避免按
组名误删用户配置。

## FTP 150 Accepted data connection

150 是中间状态。升级到包含最终响应处理的 QHY 版本；同时检查 Passive Mode、防火墙
被动端口、目录 755/写权限和空间。不要仅凭日志中的 150 判断服务端文件完整。

## FTP 每次传全部文件或内容哈希冲突

必须使用当前发布系统生成的 `upload-plan.json`。远端已存在的内容寻址 Bundle 会校验长度和
SHA-256 后跳过；同名不同内容会停止发布，不能覆盖。若实际上传仍接近完整快照，检查
`previousResourceVersion` 是否为空——首次升级需要完整种子上传，成功后才建立增量基线。

当前版本优先下载 `.qhy-bundle-hashes.json`，不会重复下载完整 Bundle。只有从旧版本迁移、
远端索引不存在或缺少条目时，才进行一次兼容性完整校验；观察“下载远端哈希索引”或“分析
FTP 远端文件”进度，必要时点击取消。索引建立后仍反复下载 Bundle 时，检查索引文件是否被
服务器清理，以及 FTP 目录是否被其他工具手工修改。

全部 FTP 字段都不再存在于 Settings。Host、Port、Username 等只在 Release 窗口保存；
Windows 密码默认进入凭据管理器，也可设置 `QHY_FTP_PASSWORD`。不要把密码加入命令行、
URL、Assets、ProjectSettings 或 UserSettings。

## 什么都没改却还能发布热更新

当前版本会在 YooAsset 构建后比较真实 Bundle Delta。没有 Added、Changed、Removed 时显示
“无需发布”，清理本次派生产物且不消耗自动资源版本；上传器也会拒绝旧的空上传计划。
若仍检测到 Changed，请在 `release-report.json` 查看具体 Bundle 和 `mainAssets`，确认是否有
导入器、生成 DLL 或依赖内容发生了实际字节变化。

## GitHub Permission denied (publickey)

SSH URL `git@github.com:...` 需要本机私钥及 GitHub 公钥。最简单是改用
`https://github.com/...git` 并让 Git Credential Manager 登录。用
`ssh -T git@github.com` 单独验证 SSH。

## Android Keystore

框架不强制自定义 Keystore。默认 Debug 签名可用于内部测试，但后续若换证书，已安装
用户不能覆盖更新。生产首次发布前固定自定义 Keystore，并安全备份。

## IL2CPP、长路径与 LocalIl2CppData

HybridCLR 8.12.0 在 Unity 2022.3 Windows Editor 的编译器通常位于：

```text
HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp/build/deploy/il2cpp.exe
```

`localRoot` 已经以 `il2cpp` 结尾，不应再拼成 `il2cpp/il2cpp/bin`。QHY Framework
1.1.3 已修复 1.1.2 的该项误判，并同时检查 `libil2cpp/hybridclr`、编译器和
`libil2cpp-version.txt`。

如果 1.1.3 仍报告本地工具链不完整：

1. 用 Unity Hub 确认当前目标平台的 IL2CPP Build Support 已安装。
2. 确认 HybridCLR 包版本与 `libil2cpp-version.txt` 相同。
3. 不要复制其他 Unity 版本或其他操作系统生成的 `HybridCLRData`。
4. 关闭占用目录的 Unity/Bee 进程，再执行 Full Package 让框架自动重装。
5. 启用 Git 长路径并缩短项目目录，检查磁盘空间和 Git 下载错误。

## LoadMetadataForAOTAssembly MissingMethodException

**现象：** 构建成功，但客户端启动加载 AOT 补充元数据时出现：

```text
MissingMethodException: HybridCLR.RuntimeApi::LoadMetadataForAOTAssembly(...)
```

这不是资源 Address 或 DLL 下载问题。它表示托管侧存在 `HybridCLR.RuntimeApi`，但
原生 Player 没有 HybridCLR 对应的 InternalCall，常见原因是构建期间
`useGlobalIl2cpp` 被改回 `true` 或 `UNITY_IL2CPP_PATH` 被清空。

处理方法：

1. 升级 QHY Framework 到 1.1.3 或更新版本。
2. 不要修改 HybridCLR 源码，也不要只重新发布 HotUpdateOnly。
3. 执行 Full Package；日志应出现“已在 BuildPlayer 前锁定 HybridCLR 本地 IL2CPP”。
4. Windows 应出现 `GameAssembly.dll` 校验通过；Android 应出现包含 ABI 数量的
   `libil2cpp.so` 校验通过。
5. 如果报告“未包含 HybridCLR 解释器标记”，保留完整 Editor log，并检查实际
   `UNITY_IL2CPP_PATH` 和 HybridCLR 本地版本；框架已经阻止该错误客户端继续发布。

:::warning 不要用错误字符串做校验
`HybridCLR.RuntimeApi::LoadMetadataForAOTAssembly` 可能只是托管元数据，不能证明原生
运行时存在。框架检查的是解释器特有标记 `InterpreterImage::GetClassFromToken`。
:::

## 发布前校验把 Documentation~ 解析成 Documentation\\~

**现象：** QHYFrameworkDeveloper 执行 `npm.cmd run check` 时报告找不到：

```text
Documentation\~\node_modules\npm\bin\npm-prefix.js
```

这是 Unity 在 Windows 上直接启动批处理文件时的工作目录解析问题，不是缺少项目内的
`npm-prefix.js`。当前发布器会从 PATH 解析绝对 Node/npm 安装位置，并通过 `node.exe`
直接执行 `npm-cli.js`，不会再让 `npm.cmd` 解释 `Documentation~`。如果仍报告找不到
Node/npm，请确认 `node.exe`、`node_modules/npm/bin/npm-cli.js` 位于同一 Node 安装目录，
并在修改 PATH 后重启 Unity。共享 Staging 工作区会自动执行一次 `npm ci` 后再检查；
GitHub 和 Gitee 不会分别重复安装。

## 客户端更新下载后卡住或闪退

1. 检查 `latest.json` 的 Size/SHA/EntryExecutable。
2. Windows ZIP 根目录必须含客户端标记和真实 exe，不要多套一层目录。
3. 查看 updater 日志，确认安装目录可写，旧进程已退出。
4. 新程序启动失败应从 backup 回滚；安全软件拦截时给 updater/exe 合法签名。
5. UI 中 `Success` 不应作为红色 error；升级到已区分安装成功/失败状态的版本。

若仍无法定位，保留 `release-report.json`、完整 Unity Editor log、Boot 日志、
`latest.json` 和失败 URL（删除密码后）再提交 Issue。
