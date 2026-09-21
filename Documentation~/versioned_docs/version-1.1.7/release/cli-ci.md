---
title: CLI 与 CI
---

# 命令行与 CI 发布 {#cli-ci}

构建入口为 `GameIntegration.Editor.ReleaseCommandLine.Build`：

```powershell
Unity.exe -batchmode -quit `
  -projectPath "D:\Project" `
  -executeMethod GameIntegration.Editor.ReleaseCommandLine.Build `
  -buildTarget StandaloneWindows64 `
  -releaseMode FullPackage `
  -clientVersion v1.0.1 `
  -resourceVersion v1.0.1-r0001 `
  -development false `
  -logFile build.log
```

可用参数只有 `-buildTarget`、`-releaseMode`、`-clientVersion`、
`-resourceVersion` 和 `-development`。`-resourceVersion` 省略时自动选择下一修订。
旧 `-releaseVersion`、`-releaseChannel` 和 `-releaseOutput` 不再支持；输出固定为
`Releases` 和 `QHYBuilds`。

CI 应提交 `ProjectSettings/QHYFramework/ReleaseState`，并串行化同平台发布。FTP 密码通过
`QHY_FTP_PASSWORD` 注入，账号可用 `QHY_FTP_USERNAME`；不要写入命令行或工程。归档
`QHYBuilds` 中的 Player、报告和计划。外部同步工具上传时必须先处理
`Upload/1-Files`，最后处理 `Upload/2-Publish`，再通过普通资源地址完成发布检查。
