---
title: FTP 与 CDN
---

# FTP 上传与服务器目录 {#ftp-cdn}

## 目录约定

```text
/CDN/PC/v1.0.1/
  GamePackage.version
  GamePackage_1.0.1.bytes
  *.bundle
/Client/PC/v1.0.1/
  Client_Windows64_v1.0.1.zip
/Client/PC/latest.json
```

Android 使用 `/CDN/Android` 与 `/Client/Android`，互不混用。

## 上传模式

- **Upload Hot Update**：仅上传发布目录的 CDN 内容。
- **Upload Hot Update and Client**：先资源，再 ZIP/APK，最后临时上传并原子覆盖
  `latest.json`。

上传客户端前会读取服务器 `latest.json`，只允许更高版本替换指针。同版本资源热更
可以覆盖对应 CDN 目录，但不会更新客户端指针。

## FTP 配置

FTP Host、Port（默认 21）、Username、Password 与 `QHYFrameworkSettings` 双向
绑定。`Remote Directory` 来自 HTTP URL 的路径；FTP Host 不要包含 `/CDN/...`。
默认 Passive Mode，服务器要求 FTPS 时再开启 SSL。

:::warning 安全
普通 FTP 明文传输凭据。生产环境优先 FTPS/SFTP 或在可信内网使用，并限制账号只可写发布目录。
:::

## FTP 150 不是最终失败

`150 Accepted data connection` 表示数据通道已接受。上传器必须等待最终的
`226 Transfer complete`，不能把 150 当错误。如果仍失败，检查服务器被动端口范围、
防火墙、文件权限与磁盘空间。

**预期结果：** 浏览器能直接打开 `.version`，并下载随机一个 Bundle，响应不是
HTML 404 页面。
