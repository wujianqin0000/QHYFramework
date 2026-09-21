---
title: Troubleshooting
---

# Troubleshooting {#troubleshooting}

## HTTPS still reports that BaseURL is not absolute

The old behavior validated every saved platform, so a bare host or invalid value left in another
platform could block the current target. The current version validates only the publication target
and accepts `aco.ai20.top`, `https://aco.ai20.top`, an exact Markdown link, or an angle-bracket URL;
all are normalized to a plain HTTP(S) root. If validation still fails, the error identifies the
platform and original input. Remove queries, credentials, game/platform folders, `/cdn`, and
`/origin`.

## Plastic shows many Releases/QHYBuilds files

QHY automatically maintains the project-root `ignore.conf` during initialization and before
publication. Verify that the file is writable and that its marked block is intact:

~~~text
# QHY Framework generated outputs - BEGIN
/Releases
/releases
/QHYBuilds
/qhybuilds
# QHY Framework generated outputs - END
~~~

If those directories were committed before the rules existed, Plastic continues tracking them;
ignore rules cannot untrack existing items. Remove `Releases` and `QHYBuilds` from version control
while keeping local files, then commit `ignore.conf`. Do not remove or ignore
`ProjectSettings/QHYFramework/ReleaseState`.

## Remote 404 / package mismatch

If the version request returns 404, verify
`{resourceBaseUrl}/{gameDirectory}/{platform}/origin/current/{ClientVersion}.version`. Manifests and Bundles must resolve below
`{resourceBaseUrl}/{gameDirectory}/{platform}/cdn`; server-relative paths must match `Releases`. Upload `Upload/1-Files` first and
`Upload/2-Publish` last, then inspect `origin/current/*` caching.

## Missing StreamingAssets first package

Host/Offline 404 under `StreamingAssets/yoo` means the client was not built by
Full Package with `copyBuiltinPackageManifest`. Use EditorSimulateMode for
iteration or rebuild the complete client.

## Open 1-Files/2-Publish selects an unrelated file or leaves Unity at Hold On

The old UI called Unity's file-manager reveal API even when the current version had no Upload directory. The operating system could fall back to a parent, select an unrelated file, and leave the Editor at `Hold On`. The current UI validates ClientVersion, ResourceVersion, path containment, and exact directory existence before enabling either button. Complete a Full Package or HotUpdateOnly build with actual changes first. If QHY reports no update to publish, no Upload directory is expected.

## Missing simulation manifest

Switch to the intended build target and run Boot again. Do not reuse another
project/platform's `Bundles/.../Simulate` path.

## Unsaved scene ErrorCode101

Save every open scene or close Untitled without saving. YooAsset intentionally
refuses dirty-scene builds.

## Invalid Game.HotUpdate.dll address

Verify the asmdef compiles, generated `.bytes` exists, the HotUpdate collector
includes it as Addressable, and settings match the Build Report. Rebuild through
the Release window rather than copying an old DLL.

## AOT change warning

A different local AOT output only warns because HotUpdateOnly excludes it. An
Added/Changed carrier bundle no longer blocks by itself: publication continues
when the restored metadata file set, lengths, and SHA-256 values still match the
client snapshot. Missing, added, uncollected, or byte-changed metadata remains blocked.
Never replace the snapshot with current AssembliesPostIl2CppStrip output; run
Full Package when hot-update code needs an AOT API absent from the old client or
when new native AOT capability must ship.

If compatibility with existing clients has been established, select **Force Publish**
in the detection dialog to continue the current pipeline. The authorization is valid
for one attempt only and command-line builds remain blocked by default. Audit the result
through `aotMetadataForcePublished` and `aotMetadataMismatchReason` in
`release-report.json`. The button bypasses only this AOT payload mismatch, never other
release validations. Do not use it when old-client compatibility is uncertain.

## Cancelled GenerateStripedAOTDlls

Wait for Unity's clean recompilation and retry. QHY removes common temporary
directories. Terminate stale Unity/Bee processes and verify disk space and the
IL2CPP module if files stay locked.

## YooAsset ErrorCode115

ResourceVersion is immutable in the current release system. Advance `v1.0.4-r0007` to
`v1.0.4-r0008`; do not delete old manifests or bundles needed by clients and rollback.

## One UI change downloads many MiB

The client is usually downloading one newly referenced shared bundle, not the
entire snapshot. Inspect `changedBundles`, `mainAssets`, `dependencyAssets`, and
`referencedBy` in `release-report.json`, plus the ten-largest-assets Console
diagnostic. Split by panel, atlas, feature, or update correlation instead of one
UI-wide PackDirectory. Snapshot, FTP transfer, and client download are separate sizes.

## Collector changes are overwritten or addresses duplicate

InitializeOnly leaves an existing package untouched and External only validates.
Before ManagedGroupsOnly, manually confirm legacy default collectors and add
`QHYFramework.Managed:v2`. An unmarked same-path conflict intentionally fails
instead of deleting a user group by name.

## FTP 150

150 is intermediate; success ends with 226. Check passive ports, firewall,
write permissions, and free disk space.

## FTP sends everything / content hash collision

Build with schema v5 so `publish-plan.json` exists. Existing content-addressed bundles
are first checked against the platform `origin/qhy.json` index and then probed with FTP `SIZE`; they are skipped only when the indexed SHA-256/length and the actual remote file length match. Same-name different
content aborts and is never overwritten. An empty `previousResourceVersion`
means the first upgraded publication is a full seed upload; later revisions are
incremental.

The uploader downloads only `origin/qhy.json`, never complete remote Bundles. A missing index is accepted
only for an empty server. A non-empty directory without schema v5 is blocked; there is no legacy
compatibility or migration path.

## Client reports `Failed to download file: gamepackage_...bundle`

YooAsset shows the logical Bundle name in this message, not the server filename. QHY's detailed
error also includes the actual `{hash}.bundle`, complete URL, and HTTP/SSL transport error. Test that
URL directly with a browser or `curl`:

- For 404/403, retry FTP for the same candidate or upload `Upload/1-Files`; verify
  `{gameDirectory}/{platform}/cdn/bundles/{hash}.bundle` exists before `2-Publish`.
- For a length mismatch, remove the damaged remote hash object and upload the correct immutable file.
- If origin succeeds but CDN fails, avoid caching 404 and refresh that hash URL.
- For SSL or HttpCode 0, use the reported URL to inspect the certificate chain, TLS 1.2/1.3, and Android UnityWebRequest compatibility.

The current FTP uploader no longer trusts a stale `qhy.json` alone: it probes the actual file and
repairs a missing object. After repairing the server, the client can use **Retry** without reinstalling.

No FTP field remains in Settings. Host, Port, Username, and related options belong
only to the Release window. Windows remembers the password in Credential Manager
by default; `QHY_FTP_PASSWORD` remains available. Never put it in command lines,
URLs, Assets, ProjectSettings, or UserSettings.

## Nothing changed but a hot update can still publish

The current release compares the actual YooAsset bundle delta after building. With
no Added, Changed, or Removed bundle it reports **Nothing to Publish**, discards the
new derived output, and does not consume the automatic resource revision. The uploader
also rejects legacy empty plans. If a bundle is still Changed, inspect its `mainAssets`
in `release-report.json` for an importer, generated DLL, or dependency byte change.

## SSH publickey

Use an HTTPS repository URL with Git Credential Manager, or configure and test
your SSH key using `ssh -T git@github.com`.

## Android Keystore

Custom signing is recommended, not mandatory. Debug signing is only suitable
for internal tests; changing the certificate later blocks in-place updates.

## IL2CPP / long path

HybridCLR 8.12.0 on Unity 2022.3 Windows Editor normally uses:

```text
HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp/build/deploy/il2cpp.exe
```

The local root already ends in `il2cpp`; it must not be checked as
`il2cpp/il2cpp/bin`. QHY Framework 1.1.3 fixes that 1.1.2 detection bug and
checks `libil2cpp/hybridclr`, the compiler, and `libil2cpp-version.txt`.

If 1.1.3 still reports an incomplete toolchain, install the target platform's
IL2CPP Build Support in Unity Hub, verify the package/local version match, close
processes locking Unity/Bee files, and rerun Full Package so QHY can reinstall.
Never copy `HybridCLRData` from another Unity version or operating system. Also
enable Git long paths, shorten the project path, and check disk space/download
errors.

## LoadMetadataForAOTAssembly MissingMethodException

If the Player builds but fails at startup with:

```text
MissingMethodException: HybridCLR.RuntimeApi::LoadMetadataForAOTAssembly(...)
```

the managed API exists but the native Player lacks HybridCLR's InternalCall.
Usually `useGlobalIl2cpp` was switched to `true`, or `UNITY_IL2CPP_PATH` was
cleared during the build.

Upgrade QHY Framework to 1.1.3 or later and run Full Package—not
HotUpdateOnly. The log should confirm the local toolchain was locked immediately
before BuildPlayer and that Windows `GameAssembly.dll` or every Android
`libil2cpp.so` passed native verification. If the interpreter marker is absent,
keep the full Editor log and inspect the effective environment path and local
HybridCLR version; QHY has correctly blocked that broken client.

:::warning Do not validate the wrong string
`HybridCLR.RuntimeApi::LoadMetadataForAOTAssembly` may be managed metadata and
does not prove that the native runtime exists. QHY checks the interpreter-only
marker `InterpreterImage::GetClassFromToken`.
:::

## Pre-publication validation resolves Documentation~ as Documentation\\~

If QHYFrameworkDeveloper runs `npm.cmd run check` and reports a missing path such
as `Documentation\~\node_modules\npm\bin\npm-prefix.js`, Unity has misinterpreted
the Windows batch working directory; the project is not expected to contain that
file there. The publisher now resolves the absolute Node/npm installation from
PATH and invokes `npm-cli.js` through `node.exe`, bypassing `npm.cmd` path parsing.
If Node/npm is still missing, verify that `node.exe` and
`node_modules/npm/bin/npm-cli.js` share the same Node installation directory and
restart Unity after changing PATH. The shared Staging workspace runs one
`npm ci` before the documentation check; GitHub and Gitee do not reinstall
dependencies independently.

## Android animation stays on its first frame after bundle load

When controllers, clips, or animated models exist only in YooAsset bundles and Boot
references no animation, inspect `release-report.json`: `stripEngineCode` may be true,
`externalAnimationAssetCount` should be nonzero, `externalAnimationAssets` should list
the expected controller, clip, or model paths,
`engineStrippingProtectedAssemblies` must contain `UnityEngine.AnimationModule`, and
`engineStrippingLinkPath` should be `Assets/Game/Generated/QHYLink/link.xml`.
That file must contain:

```xml
<assembly fullname="UnityEngine.AnimationModule" preserve="all" />
```

Do not repair this with HotUpdateOnly or by editing
`Assets/HybridCLRGenerate/link.xml`. Run FullPackage again; QHY re-analyzes collected
assets and dependencies, overwrites its own link file, and imports it synchronously.
If validation reports incomplete animation-module preservation, confirm the Collector
actually includes the animation content and the generated directory is writable.
Adding `UnityEngine.AnimationModule.dll` as supplemental AOT metadata cannot restore
Engine code already stripped from the Player.

## Client updater stalls or exits

Verify manifest size/SHA/executable, ensure the ZIP contains the marked client
at its root, inspect updater logs and permissions, and check security software.
Launch failure should restore backup.

## DistributionRuntimeConfig.cdnRoot is incorrectly rejected as a platform root

If Full Package or Hot Update build reports that `cdnRoot` must point to a platform root and cannot
end in `/cdn` or `/origin`, an older validator has applied the user BaseURL rule to QHY's derived
runtime URL. The rules are now separate: the Settings resource BaseURL contains no platform folder or either suffix,
while generated `cdnRoot` must end in `/cdn` and `originRoot` must end in `/origin`. Upgrade and
rebuild without changing the server layout or the configured platform BaseURL.

When filing an issue, attach the sanitized manifest, release report, complete
Editor/Boot logs, and failing URL—never the FTP password.
