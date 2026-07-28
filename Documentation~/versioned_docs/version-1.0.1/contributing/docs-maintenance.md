---
title: 文档维护指南
---

# 文档维护指南 {#docs-maintenance}

## 目录与语言

- `docs/`：中文 Next 源。
- `i18n/en/docusaurus-plugin-content-docs/current/`：英文 Next 镜像。
- `versioned_docs/version-x.y.z/`：中文稳定快照。
- `i18n/en/docusaurus-plugin-content-docs/version-x.y.z/`：英文稳定快照。
- `static/img/screenshots/`：裁剪压缩后的真实 WebP 截图。

两种语言使用完全相同的相对路径。显式标题锚点（如 `#installation`）也必须对应，
以便跨语言切换保持位置。

## 本地命令

```bash
npm ci
npm run start          # 中文 Next
npm run start:en       # 英文 Next
npm run check          # 页面对应、版本、链接和生产构建
npm run build          # 中英双语生产站
```

## 编写页面

关键教程统一包含：前置条件、操作步骤、预期结果、常见错误、下一步。代码使用当前
公开 API，不能只凭记忆。新页面同时提交英文镜像，并更新 `sidebars.ts`。图片必须有
描述性 alt，避免把文字只放在截图里。

## 发布文档版本

1. 先将 UPM 根 `package.json` 更新到目标版本。
2. 确保中文/英文 Next 对应且 `npm run check` 通过。
3. 运行：

```bash
npm run docs:release -- 1.1.0
```

脚本快照 Next、更新 `versions.json`，但不会修改框架版本。再次执行同一版本会拒绝
覆盖。审查后提交源码和 `package-lock.json`。

## GitHub Pages

Pull Request 自动执行 `npm ci` 与完整检查；`main` 的文档变更构建后上传 Pages
artifact。仓库首次设置：**Settings → Pages → Source → GitHub Actions**。部署不需要
个人令牌，仅使用 `pages: write` 与 OIDC `id-token: write`。

## UPM 发布边界

开发者发布器保留文档源码、锁文件和静态素材，但排除 `node_modules`、`build`、
`.docusaurus` 与缓存。发布前执行本地 smoke，确认没有宿主 `Assets`、FTP 凭据或
`QHYFrameworkDeveloper`。
