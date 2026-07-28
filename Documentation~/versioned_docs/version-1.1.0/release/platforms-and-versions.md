---
title: 平台与版本
---

# 平台配置与独立版本 {#platforms-versions}

## 每个平台独立保存

Windows 和 Android 的资源 URL、客户端 URL 与版本互不借用。例如 Android 已发布
`v1.2.0`，切回尚未升级的 Windows 时仍显示 Windows 自己的 `v1.1.1`。新平台首次
选择固定从 `v1.0.0` 开始，不复制最近平台版本。

版本必须是严格语义格式：

```text
v<major>.<minor>.<patch>
```

资源版本与 `Project Settings → Player → Version` 双向同步。Android
`versionCode = major × 1,000,000 + minor × 1,000 + patch`。

## 版本自动化

在 Release 窗口勾选 Version Automation 后，完整客户端构建提供：

- **Major Build**：`v1.2.3 → v2.0.0`
- **Minor Build**：`v1.2.3 → v1.3.0`
- **Patch Build**：`v1.2.3 → v1.2.4`

版本只在完整构建成功后提交。取消或构建失败不会消耗版本号。HotUpdateOnly 固定使用
已发布客户端版本，不自动递增。

## 远端路径

设置中只填写不带版本的根：

```text
Resource Base URL: https://example.com/CDN/PC
Client Update URL: https://example.com/Client/PC
```

运行时自动拼接：

```text
https://example.com/CDN/PC/v1.0.1
https://example.com/Client/PC/latest.json
```

不要把 `v1.0.1` 写进根 URL，否则会出现重复版本目录。
