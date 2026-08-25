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

日常开发不得修改 UPM 版本，也不得提前生成稳定快照。将变更写入 CHANGELOG
`[Unreleased]`，并同步维护中英文 Next 文档。真正发布时使用框架开发工程的
**QHY Framework Developer/UPM 包发布**选择主版本、次版本或修订版本。

发布器会固化 `[Unreleased]`、更新 README 与全部版本文件、生成中英文稳定快照并强制
运行 `npm run check`，然后使用相同分支和标签发布 GitHub 与 Gitee。只有两个远端全部
成功后才把新版本写回本地工程。

发布窗口首次打开时会先立即绘制界面和读取状态，再在后续 Editor 回调中轻量读取当前
稳定版本。完整包扫描和文档生产构建只在点击“校验未发布内容”或正式发布时执行。

以下命令只用于发布工具维护或故障诊断，不得在日常开发中提前生成快照：

1. 将 UPM 根 `package.json` 更新到目标版本。
2. 确保中文/英文 Next 对应且 `npm run check` 通过。
3. 运行：

```bash
npm run docs:release -- 1.1.0
```

脚本快照 Next、更新 `versions.json`、文档包和锁文件版本，但不会修改框架版本。
再次执行同一版本会拒绝覆盖。审查后提交源码和 `package-lock.json`。

## GitHub Pages

Pull Request 自动执行 `npm ci` 与完整检查；`main` 的文档变更构建后上传 Pages
artifact。仓库首次设置：**Settings → Pages → Source → GitHub Actions**。部署不需要
个人令牌，仅使用 `pages: write` 与 OIDC `id-token: write`。

## UPM 发布边界

开发者发布器保留文档源码、锁文件和静态素材，但排除 `node_modules`、`build`、
`.docusaurus` 与缓存。窗口重新聚焦或项目变化时会从 UPM `package.json` 刷新最后稳定
版本。发布前还应确认没有宿主 `Assets`、FTP 凭据或 `QHYFrameworkDeveloper`。
