# QHY Framework

QHY Framework 是面向 Unity 2022.3 LTS 的标准 UPM 包，将 QFramework、HybridCLR
和 YooAsset 无侵入地整合为一条完整的开发、资源热更新、代码热更新、客户端整包升级
与发布工作流。

## 安装

在 Unity Package Manager 中选择 **Add package from git URL...**：

```text
https://github.com/wujianqin0000/QHYFramework.git#v1.1.3
```

只需导入 `com.wjq.qhy-framework`。框架会自动安装 HybridCLR 8.12.0 与
YooAsset 3.0.4，QFramework 1.0.246 已包含在包中；依赖完成后自动创建 Boot、
Main、可编辑 BootUI Prefab、设置、热更新程序集和资源收集配置，无需执行
Prepare Project。

1.1.3 修复了 HybridCLR 8.12 本地 IL2CPP 目录误判：完整发布会在 `BuildPlayer` 前
重新锁定本地工具链，并校验 Windows `GameAssembly.dll` / Android `libil2cpp.so`
确实包含 HybridCLR 解释器，防止生成构建成功但启动时报 `MissingMethodException` 的客户端。

完整的零基础教程、发布运维、API 和排障文档：

- [在线文档（默认中文）](https://wujianqin0000.github.io/QHYFramework/)
- [Online documentation (English)](https://wujianqin0000.github.io/QHYFramework/en/)
- [离线 Markdown 文档](Documentation~/README.md)

## Installation

In Unity Package Manager, choose **Add package from git URL...** and enter:

```text
https://github.com/wujianqin0000/QHYFramework.git#v1.1.3
```

Import only `com.wjq.qhy-framework`. Dependencies and the host project skeleton
are prepared automatically. See the [English documentation](https://wujianqin0000.github.io/QHYFramework/en/)
for the quick start, development guide, release operations, API reference, and
troubleshooting.

Version 1.1.3 fixes HybridCLR 8.12 local-IL2CPP layout detection, reasserts the
patched toolchain immediately before `BuildPlayer`, and verifies the native
HybridCLR interpreter in Windows and Android artifacts before publishing.
