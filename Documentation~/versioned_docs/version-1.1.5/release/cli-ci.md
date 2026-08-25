---
title: CLI 与 CI
---

# 命令行与 CI 发布 {#cli-ci}

编辑器构建入口为：

```text
GameIntegration.Editor.ReleaseCommandLine.Build
```

Windows 示例（把 Unity 路径替换为实际版本）：

```powershell
Unity.exe -batchmode -quit `
  -projectPath "D:\Project" `
  -executeMethod GameIntegration.Editor.ReleaseCommandLine.Build `
  -buildTarget StandaloneWindows64 `
  -releaseMode FullPackage `
  -releaseChannel default `
  -clientVersion v1.0.1 `
  -resourceVersion v1.0.1-r0001 `
  -releaseOutput Releases `
  -development false `
  -logFile build.log
```

Android 热更新：

```bash
Unity -batchmode -quit \
  -projectPath /workspace/project \
  -executeMethod GameIntegration.Editor.ReleaseCommandLine.Build \
  -buildTarget Android \
  -releaseMode HotUpdateOnly \
  -clientVersion v1.0.1 \
  -resourceVersion v1.0.1-r0002 \
  -releaseOutput Releases \
  -logFile -
```

## CI 建议

`-releaseVersion` 仍作为旧脚本的 ClientVersion 兼容别名；新流水线应显式传递两个参数。
`-resourceVersion` 省略时根据最近已发布基线生成下一修订，但生产 CI 推荐由发布编排器
明确分配，避免并发任务竞争同一个修订号。

- 固定 Unity、包标签和构建模块。
- Keystore、FTP 密码放 CI Secret，不提交仓库；FTP 使用 `QHY_FTP_PASSWORD`，账号可用 `QHY_FTP_USERNAME`。
- 缓存 `Library` 时以 Unity/Packages lock hash 分区；异常时允许无缓存重建。
- 构建后归档 `release-report.json`、`upload-plan.json`、SHA-256 清单、客户端和 CDN 目录。
- 上传是独立阶段，并要求人工批准生产环境。
- Full Package 成功且上传完成后再确认平台版本，失败不提前递增。

Unity CLI 非零退出时保留完整 `build.log`。取消构建残留会由工具清理；如果仍有 Bee
进程占用，结束旧 Unity 构建进程后重试。
