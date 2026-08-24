---
title: CLI and CI
---

# CLI and CI release {#cli-ci}

```powershell
Unity.exe -batchmode -quit `
  -projectPath "D:\Project" `
  -executeMethod GameIntegration.Editor.ReleaseCommandLine.Build `
  -buildTarget StandaloneWindows64 `
  -releaseMode FullPackage `
  -releaseChannel default `
  -releaseVersion v1.0.1 `
  -releaseOutput Releases `
  -development false `
  -logFile build.log
```

Use `HotUpdateOnly` and `Android` for the corresponding build. Pin Unity and
package versions, store signing/FTP secrets in the CI secret store, archive the
release report and hashes, and separate upload into an approved production
stage. A failed or cancelled build must not advance the platform version.

Preserve full Unity logs on non-zero exit. QHY cleans common cancelled-build
artifacts; terminate stale Unity/Bee processes if files remain locked.
