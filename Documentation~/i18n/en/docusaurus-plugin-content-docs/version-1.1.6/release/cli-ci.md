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
  -clientVersion v1.0.1 `
  -resourceVersion v1.0.1-r0001 `
  -releaseOutput Releases `
  -development false `
  -logFile build.log
```

Use `HotUpdateOnly` and `Android` for the corresponding build. `-releaseVersion`
remains a legacy ClientVersion alias, but new pipelines should pass both
`-clientVersion` and `-resourceVersion`. Omitting the resource value enables automatic
resource selection across the published baseline, local Releases, and YooAsset output.
The Editor window's single Automatic Versioning toggle also controls ClientVersion;
CLI ClientVersion remains explicit. Production orchestration should allocate the
resource revision explicitly to prevent concurrent jobs from racing.

Pin Unity and package versions, inject FTP secrets through `QHY_FTP_PASSWORD`
(and optionally `QHY_FTP_USERNAME`), archive `release-report.json`,
`upload-plan.json`, hashes, client, and CDN snapshot, and make upload an approved
production stage. Failure/cancellation must not advance either published pointer.

Preserve full Unity logs on non-zero exit. QHY cleans common cancelled-build
artifacts; terminate stale Unity/Bee processes if files remain locked.
