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
Bundle 和新清单。上传器首先下载远端 `.qhy-bundle-hashes.json`，用其中的长度和 SHA-256
直接判断同名内容寻址 Bundle：完全一致则跳过，长度或哈希不同立即停止。不会每次下载完整
远端 Bundle。

本机同时在 `Library/QHYFramework/FtpVerificationCache` 保存按服务器、账号和远端目录隔离的
验证缓存。旧服务器没有索引或索引缺少某项时，才会对该 Bundle 做一次完整下载校验并补齐
缓存/索引。新 Bundle 上传后先原子更新哈希索引，再切换 `GamePackage.version`，因此索引不会
指向尚未上传的内容。FTP 资源目录应只由发布工具维护；不要在服务器上手工替换同名 Bundle
却保留旧索引。Removed Bundle 不立即删除，因为旧 Manifest 和未更新客户端仍可能使用它。

索引下载及兼容校验会显示当前阶段、文件、完成数量和取消按钮。迁移旧服务器的第一次可能较
慢；索引建立后，正常发布只下载几 KB 的索引，不再下载旧 Bundle。

如果 Bundle Delta 没有 Added、Changed、Removed，HotUpdateOnly 会显示“无需发布”，删除
本次尚未发布的派生构建目录，并保留该自动 ResourceVersion 供下次真实修改使用。旧版本遗留的
空 `upload-plan.json` 也会在连接 FTP 前被拒绝，不能只上传新 Manifest 制造假更新。

## 上传模式

- **Upload Hot Update**：仅上传发布目录的 CDN 内容。
- **Upload Hot Update and Client**：先资源，再 ZIP/APK，最后临时上传并原子覆盖
  `latest.json`。

上传顺序固定为：新 Bundle → Manifest bytes/json → hash → 构建报告与审计文件 →
`GamePackage.version.uploading` → 原子替换 `GamePackage.version`。替换前先把旧指针
暂存为 `.previous`，失败时恢复，因此上传中断不会暴露半成品 Manifest。

上传客户端前会读取服务器 `latest.json`，只允许更高客户端版本替换指针。

## 线上资源版本恢复（回滚）

发布窗口把构建、FTP 上传和故障恢复分成独立区域。发生“新热更新上线后异常，需要恢复旧内容”
时，找到 **线上资源版本恢复（回滚）** 卡片。卡片会直接显示 **当前线上生效版本**，并说明该
操作只恢复线上资源、不会删除 Bundle。点击 **选择要恢复的线上资源版本…** 后，菜单会列出
当前客户端版本下所有满足以下条件的修订：

- 本地存在完整的 `upload-plan.json` 和唯一 `.version` 指针；
- 渠道、平台和 ClientVersion 与当前发布窗口一致；
- 已记录在成功上传版本账本中，且不高于“历史最高已发布版本”；
- 不是当前远端生效版本。

因此新流程中本地只构建、但从未成功上传的 revision 不会出现在菜单里。旧基线没有版本账本时，
会暂时按历史最高修订兼容列出候选，但仍必须通过下面的远端验证。回滚前，上传器会先下载
远端 SHA-256 索引，逐一确认目标 Manifest `.bytes`/`.hash` 和目标版本需要的全部 Bundle
确实存在且内容一致；旧服务器索引缺项时才会完整读取对应 Bundle 做兼容校验。验证过程显示
文件级进度并可取消。任一文件缺失或哈希不符都会停止，绝不会先切换指针。

验证通过后只原子替换 `GamePackage.version`，不重新上传或删除 Bundle。基线切换到所选版本，
同时保留成功上传账本与历史最高已发布修订。例如从 `r0005` 回滚到 `r0002` 后，菜单仍可选择 `r0003`、
`r0004` 或 `r0005`，因此既支持跨多级回滚，也支持撤销回滚。

如果尚未成功上传过资源版本，恢复按钮会禁用并显示“尚未记录已发布版本”。菜单为空则表示
当前没有其他同时满足“已发布、与当前平台和 ClientVersion 一致、本地快照完整”的版本。

资源上传成功后，本地基线会记录当前 ResourceVersion，“上传热更新包”随即禁用并显示
“已经上传”。若这是完整包且客户端尚未上传，组合按钮仍可只补传客户端；客户端也成功后两个
按钮全部禁用。网络中断或上传失败不会写成功基线，仍可直接重试；执行回滚后基线切到目标版本，
原版本允许重新发布，也可以直接通过历史菜单恢复该版本。

## FTP 配置

FTP Host、Port（默认 21）、Username、Passive、FTPS 和远端目录只在 QHY Framework
Release 窗口配置，按项目与平台保存到本机 EditorPrefs；它们不再是
`QHYFrameworkSettings` 字段，也不会随 Settings Asset 提交。

“安全记住 FTP 密码”默认勾选。Windows 编辑器把密码写入 Windows 凭据管理器，普通
EditorPrefs 只保存是否记住，不保存密码；取消勾选会删除对应凭据。环境变量
`QHY_FTP_PASSWORD`/`QHY_FTP_USERNAME` 始终优先，适用于 CI。发布后仍会扫描 Assets、
ProjectSettings、UserSettings，避免明文凭据残留。非 Windows 编辑器不提供持久密码，使用
当前会话或环境变量。

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
