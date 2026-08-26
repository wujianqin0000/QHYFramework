---
title: Installation
---

# Install through Git UPM {#installation}

Open **Window → Package Manager**, click **+**, choose **Add package from git
URL...**, and enter:

```text
https://github.com/wujianqin0000/QHYFramework.git#v1.1.6
```

![QHY Framework installed in Unity Package Manager](/img/screenshots/upm-install.webp)

Or edit `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.wjq.qhy-framework": "https://github.com/wujianqin0000/QHYFramework.git#v1.1.6"
  }
}
```

QHY installs QFramework, HybridCLR, and YooAsset automatically, then prepares
the Boot/Main scenes, editable BootUI prefab, settings asset, hot-update
assembly, and collectors. FTP configuration appears only in the QHY Release
window and is not part of the Settings asset. Every new platform starts at `v1.0.0`.

FTP passwords are never serialized. On Windows the Release window remembers one
securely in Credential Manager by default, or CI can inject `QHY_FTP_PASSWORD`.
Never place it in a URL, command line, project asset, or log.

**Expected:** the **QHY Framework** menu appears, with
`Assets/Scenes/Boot.unity` and `Assets/Game/Content/Scenes/Main.unity`.

## Upgrading from 1.1.4 or earlier

This change defaults to `collectorManagementMode = InitializeOnly`, so an existing
YooAsset package is not rebuilt or overwritten. To adopt managed defaults,
back up the collector config, confirm legacy QHY collectors, add
`QHYFramework.Managed:v2`, then choose ManagedGroupsOnly. Groups are never
adopted by name alone.

The first publication without a resource baseline creates a complete
`<ClientVersion>-r0001` seed library. Later revisions use `upload-plan.json` for
incremental FTP. HotUpdateOnly requires the Full Package client/AOT snapshot.
When an old Settings asset once serialized `ftpPassword`, the first load after this change
automatically reserializes it without the orphaned YAML field. Scan the project
again after upgrading to confirm that no credential residue remains.

## HybridCLR upgrade from 1.1.2 or earlier

HybridCLR 8.12.0 projects should upgrade to current code or a later stable release containing the fix. The
1.1.2 dependency initializer could mistake a valid
`HybridCLRData/LocalIl2CppData-*/il2cpp/build/deploy` layout for a missing
installation and switch back to Unity's global IL2CPP after `Generate/All`.
That creates a Player which builds successfully but fails during startup.

Do not patch HybridCLR, YooAsset, or QFramework, and do not copy
`HybridCLRData` from another project. Upgrade the package and run one Full
Package Build. QHY validates or reinstalls the local toolchain and verifies the
native HybridCLR runtime after the Player build.

If SSH reports `Permission denied (publickey)`, use the HTTPS URL or configure
an SSH key. For long-path failures enable `core.longpaths`.

**Next:** [Automatic setup](automatic-setup.md).
