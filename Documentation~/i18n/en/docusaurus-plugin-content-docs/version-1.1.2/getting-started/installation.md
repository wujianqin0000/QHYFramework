---
title: Installation
---

# Install through Git UPM {#installation}

Open **Window → Package Manager**, click **+**, choose **Add package from git
URL...**, and enter:

```text
https://github.com/wujianqin0000/QHYFramework.git#v1.0.1
```

![QHY Framework 1.0.1 installed in Unity Package Manager](/img/screenshots/upm-install.webp)

Or edit `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.wjq.qhy-framework": "https://github.com/wujianqin0000/QHYFramework.git#v1.0.1"
  }
}
```

QHY installs QFramework, HybridCLR, and YooAsset automatically, then prepares
the Boot/Main scenes, editable BootUI prefab, settings asset, hot-update
assembly, and collectors. FTP host/user/password start empty; every new
platform starts at `v1.0.0`.

**Expected:** the **QHY Framework** menu appears, with
`Assets/Scenes/Boot.unity` and `Assets/Game/Content/Scenes/Main.unity`.

If SSH reports `Permission denied (publickey)`, use the HTTPS URL or configure
an SSH key. For long-path failures enable `core.longpaths`.

**Next:** [Automatic setup](automatic-setup.md).
