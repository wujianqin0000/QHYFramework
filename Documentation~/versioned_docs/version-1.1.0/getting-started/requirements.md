---
title: 环境准备
---

# 环境准备 {#requirements}

## 必需环境

| 项目 | 要求 | 用途 |
|---|---|---|
| Unity | `2022.3.62f2c1`，或已验证的 2022.3 LTS | 编辑、IL2CPP 构建 |
| Git | 2.30+，启用长路径 | 通过 Git URL 安装 UPM |
| Windows 模块 | Windows Build Support (IL2CPP) | Windows64 完整客户端 |
| Android 模块 | Android Build Support、SDK、NDK、OpenJDK | Android APK |
| Node.js | 22，仅文档维护者需要 | 本地文档站 |

在 Windows 上建议执行一次：

```bash
git config --global core.longpaths true
```

## Unity 模块检查

打开 Unity Hub → Installs → 目标 Unity 版本右侧齿轮 → Add modules。Windows
开发至少安装 Windows Build Support (IL2CPP)；Android 开发把 Android SDK &
NDK Tools 和 OpenJDK 一并勾选。缺少 IL2CPP 工具时，HybridCLR 可能报告
`LocalIl2CppData-.../il2cpp/bin does not exist`。

## 网络与服务器

只在发布 HostPlayMode 时需要 HTTP(S)/FTP 服务器。静态服务器必须：

- 可以直接 GET `.version`、`.json`、`.bytes`、`.bundle`、`.zip` 或 `.apk`。
- 不把未知扩展名重写为 HTML。
- 客户端包最好支持 HTTP Range，以便断点续传。
- 保留旧版本目录，旧客户端离线或检查失败时仍能使用对应资源。

**预期结果：** Unity 能正常切换目标平台，脚本编译无错。

**下一步：** [通过 UPM 安装 QHY Framework](installation.md)。
