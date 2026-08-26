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

Release 窗口只有一个 **版本自动化** 总开关，同时控制客户端版本和资源版本。升级后每个
项目/平台首次加载新规则时默认勾选；用户之后手动取消会记住该偏好，不会再次强制开启。

勾选后，客户端版本输入变为只读基线，完整客户端构建提供：

- **Major Build**：`v1.2.3 → v2.0.0`
- **Minor Build**：`v1.2.3 → v1.3.0`
- **Patch Build**：`v1.2.3 → v1.2.4`

每个按钮同时显示候选 ClientVersion 和自动 ResourceVersion。HotUpdateOnly 显示并使用当前
客户端的下一资源修订。资源修订会综合已发布基线、`Releases` 本地产物以及失败遗留的
YooAsset 不可变输出，自动跳过所有已占用的 `-rNNNN`。

客户端版本只在完整构建成功后提交。取消或构建失败不会消耗客户端版本号。取消勾选后，
ClientVersion 与 ResourceVersion 都恢复为手工输入；此时资源版本留空、格式错误、前缀不匹配
或不大于历史最高修订都会直接阻止构建，不会暗中自动补值。

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
Releases/<channel>/<platform>/<ClientVersion>/
  Revisions/
    <ResourceVersion>/
      CDN/
      Client/             # 仅 Full Package
      release-report.json
      upload-plan.json
  SharedBundles/
    <content-addressed bundle files>
```

`Revisions` 集中归档全部 `-rNNNN` 快照，因此版本根目录不会被大量修订目录铺满。
`SharedBundles` 是本机的不可变 Bundle 内容库：Windows 上每个 `CDN` 快照通过 NTFS
硬链接引用同一份 Bundle 字节，Manifest、版本指针、报告和客户端产物仍是各修订独立文件。
同名 Bundle 若长度或 SHA-256 不同会立即终止构建，不能覆盖内容库。若编辑器所在文件系统
不支持硬链接，框架自动退回普通复制，发布结果和上传行为不变，只是不节省磁盘空间。

这项整理只改变本地 `Releases` 布局。FTP/CDN 仍使用原来的
`<remote-root>/<ClientVersion>` 兼容目录，不会在服务器上为每个 `-rNNNN` 创建子目录。
升级前已经存在的旧布局
`<ClientVersion>/<ResourceVersion>` 不会被搬迁或删除，仍可作为已发布基线、回滚来源，
也会参与自动资源版本号扫描；新构建统一写入 `Revisions`。

不要把客户端版本写进配置的根 URL，否则会出现重复路径。
