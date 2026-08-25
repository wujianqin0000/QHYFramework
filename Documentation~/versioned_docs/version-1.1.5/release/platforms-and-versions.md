---
title: 平台与版本
---

# 平台配置与独立版本 {#platforms-versions}

## 两种版本，不再混用

当前发布系统明确区分：

| 概念 | 示例 | 什么时候改变 |
|---|---|---|
| `ClientVersion` | `v1.0.4` | Full Package，客户端/AOT/原生配置变化 |
| `ResourceVersion` | `v1.0.4-r0007` | 每次资源或代码热更新 |

`ClientVersion` 使用严格的 `v<major>.<minor>.<patch>`。Android
`versionCode = major × 1,000,000 + minor × 1,000 + patch`。资源版本必须以前者为前缀并
追加至少四位递增修订号；已生成的资源版本不可覆盖。

Windows 和 Android 分别保存客户端、资源基线和 URL。HotUpdateOnly 从 Full Package
基线读取只读 `ClientVersion`，不写 `PlayerSettings.bundleVersion` 或 Android
`versionCode`；只构建下一个 `ResourceVersion`。

## 版本自动化

在 Release 窗口勾选 Version Automation 后，完整客户端构建提供：

- **Major Build**：`v1.2.3 → v2.0.0`
- **Minor Build**：`v1.2.3 → v1.3.0`
- **Patch Build**：`v1.2.3 → v1.2.4`

版本只在完整构建成功后提交。取消或构建失败不会消耗客户端版本号。首次为某个客户端
构建资源时默认 `-r0001`；已有已发布资源基线时递增到下一修订号。

## 远端路径

设置中只填写不带版本的根：

```text
Resource Base URL: https://example.com/CDN/PC
Client Update URL: https://example.com/Client/PC
```

运行时只用客户端版本定位兼容目录：

```text
https://example.com/CDN/PC/v1.0.1
https://example.com/Client/PC/latest.json
```

该目录可同时保存 `GamePackage_v1.0.1-r0001.bytes`、`-r0002.bytes` 和全部内容寻址
Bundle。`GamePackage.version` 的文本决定当前生效的资源版本。回滚只切换这个指针，
不会删除 Bundle。

本地产物按不可变快照保存：

```text
Releases/<channel>/<platform>/<ClientVersion>/<ResourceVersion>/
  CDN/
  Client/             # 仅 Full Package
  release-report.json
  upload-plan.json
```

不要把客户端版本写进配置的根 URL，否则会出现重复路径。
