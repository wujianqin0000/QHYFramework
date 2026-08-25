---
title: FTP 与 CDN
---

# FTP 上传与服务器目录 {#ftp-cdn}

## 目录约定

```text
/CDN/PC/v1.0.1/
  GamePackage.version
  GamePackage_v1.0.1-r0001.bytes
  GamePackage_v1.0.1-r0001.hash
  GamePackage_v1.0.1-r0002.bytes
  GamePackage_v1.0.1-r0002.hash
  <内容寻址 Bundle>
/Client/PC/v1.0.1/
  Client_Windows64_v1.0.1.zip
/Client/PC/latest.json
```

Android 使用 `/CDN/Android` 与 `/Client/Android`，互不混用。

## 增量上传计划

每次构建都会写入 `upload-plan.json`。框架用 YooAsset Build Report 按逻辑 Bundle 名
比较上一已发布资源版本，分类为 Added、Changed、Unchanged、Removed，并同时显示：

```text
完整资源快照：38.75 MiB
FTP 实际上传：11.93 MiB
预计客户端更新：11.90 MiB
```

完整 CDN 快照包含全部 Bundle 是正常现象；它不等于上传量。FTP 预检只处理新增/变化
Bundle 和新清单。若同名内容寻址 Bundle 已存在，会读取远端长度和 SHA-256：完全一致
则跳过；长度或哈希不同立即停止，以免污染内容库。Removed Bundle 不立即删除，因为旧
Manifest 和未更新客户端仍可能使用它。

## 上传模式

- **Upload Hot Update**：仅上传发布目录的 CDN 内容。
- **Upload Hot Update and Client**：先资源，再 ZIP/APK，最后临时上传并原子覆盖
  `latest.json`。

上传顺序固定为：新 Bundle → Manifest bytes/json → hash → 构建报告与审计文件 →
`GamePackage.version.uploading` → 原子替换 `GamePackage.version`。替换前先把旧指针
暂存为 `.previous`，失败时恢复，因此上传中断不会暴露半成品 Manifest。

上传客户端前会读取服务器 `latest.json`，只允许更高客户端版本替换指针。资源回滚
按钮仅把 `GamePackage.version` 切回 `previousResourceVersion`，不重新上传或删除 Bundle。

## FTP 配置

FTP Host、Port（默认 21）和 Username 可保存在 `QHYFrameworkSettings`。Password
本次变更后绝不序列化；只可在 Release 窗口为当前 Editor 会话临时输入，或使用环境
变量 `QHY_FTP_PASSWORD`。CI 也可用 `QHY_FTP_USERNAME` 覆盖账号。成功、失败、取消
或关闭窗口都会清空会话密码，并扫描 Assets、ProjectSettings、UserSettings 中的残留。

`Remote Directory` 来自 HTTP URL 的路径；FTP Host 不要包含 `/CDN/...`。默认
Passive Mode，服务器要求 FTPS 时开启 SSL。

:::warning 安全
普通 FTP 明文传输凭据。生产环境优先 FTPS、SFTP 或 HTTPS，并限制账号只可写发布目录。
:::

## FTP 150 不是最终失败

`150 Accepted data connection` 表示数据通道已接受。上传器必须等待最终的
`226 Transfer complete`，不能把 150 当错误。如果仍失败，检查服务器被动端口范围、
防火墙、文件权限与磁盘空间。

**预期结果：** 浏览器能直接打开 `.version`，并下载随机一个 Bundle，响应不是
HTML 404 页面。
