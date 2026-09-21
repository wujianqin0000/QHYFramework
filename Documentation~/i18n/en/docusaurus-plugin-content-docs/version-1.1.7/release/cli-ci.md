---
title: CLI and CI
---

# CLI and CI release {#cli-ci}

Use `GameIntegration.Editor.ReleaseCommandLine.Build`:

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

Supported options are `-buildTarget`, `-releaseMode`, `-clientVersion`,
`-resourceVersion`, and `-development`. Omitting ResourceVersion selects the next revision.
Legacy `-releaseVersion`, `-releaseChannel`, and `-releaseOutput` are unsupported; output is
fixed to `Releases` and `QHYBuilds`.

Commit `ProjectSettings/QHYFramework/ReleaseState` and serialize publication per platform. Inject
FTP secrets through `QHY_FTP_PASSWORD` and optionally `QHY_FTP_USERNAME`. Archive Players,
reports, and plans from `QHYBuilds`. External deployment must upload `Upload/1-Files` before
`Upload/2-Publish`, then complete ordinary Resource BaseURL verification.
