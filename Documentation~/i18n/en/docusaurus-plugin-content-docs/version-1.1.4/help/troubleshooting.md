---
title: Troubleshooting
---

# Troubleshooting {#troubleshooting}

## Remote 404 / package mismatch

If QHY requests `GamePackage.version` but the server contains
`DefaultPackage.version`, align `packageName` and the YooAsset build. The exact
path is `<remoteBaseUrl>/<PlayerVersion>/<packageName>.version`; profiles must
not include a version or use another platform's root.

## Missing StreamingAssets first package

Host/Offline 404 under `StreamingAssets/yoo` means the client was not built by
Full Package with `copyBuiltinPackageManifest`. Use EditorSimulateMode for
iteration or rebuild the complete client.

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

HotUpdateOnly remains available. Keep metadata for the target published client;
use Full Package if new code requires AOT/native functionality it lacks.

## Cancelled GenerateStripedAOTDlls

Wait for Unity's clean recompilation and retry. QHY removes common temporary
directories. Terminate stale Unity/Bee processes and verify disk space and the
IL2CPP module if files stay locked.

## YooAsset ErrorCode115

Use QHY's release pipeline for safe same-version rebuilding. Do not manually
delete old server versions still needed by clients.

## FTP 150

150 is intermediate; success ends with 226. Check passive ports, firewall,
write permissions, and free disk space.

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

## Client updater stalls or exits

Verify manifest size/SHA/executable, ensure the ZIP contains the marked client
at its root, inspect updater logs and permissions, and check security software.
Launch failure should restore backup.

When filing an issue, attach the sanitized manifest, release report, complete
Editor/Boot logs, and failing URL—never the FTP password.
