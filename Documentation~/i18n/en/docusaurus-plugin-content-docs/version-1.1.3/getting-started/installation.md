---
title: Installation
---

# Install through Git UPM {#installation}

Open **Window → Package Manager**, click **+**, choose **Add package from git
URL...**, and enter:

```text
https://github.com/wujianqin0000/QHYFramework.git#v1.1.3
```

![QHY Framework installed in Unity Package Manager](/img/screenshots/upm-install.webp)

Or edit `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.wjq.qhy-framework": "https://github.com/wujianqin0000/QHYFramework.git#v1.1.3"
  }
}
```

QHY installs QFramework, HybridCLR, and YooAsset automatically, then prepares
the Boot/Main scenes, editable BootUI prefab, settings asset, hot-update
assembly, and collectors. FTP host/user/password start empty; every new
platform starts at `v1.0.0`.

**Expected:** the **QHY Framework** menu appears, with
`Assets/Scenes/Boot.unity` and `Assets/Game/Content/Scenes/Main.unity`.

## Upgrading from 1.1.2 or earlier

HybridCLR 8.12.0 projects should upgrade to QHY Framework 1.1.3 or later. The
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
