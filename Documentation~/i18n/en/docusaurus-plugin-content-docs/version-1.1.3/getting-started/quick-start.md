---
title: Ten-minute quick start
---

# Your first hot update in ten minutes {#quick-start}

## 1. Run Boot

Open `Assets/Scenes/Boot.unity`. Keep
`GameBootstrap.PlayMode = EditorSimulateMode` and press Play. Boot initializes
the simulated package, loads hot-update assemblies, and enters `Main`.

![Boot UI checking and downloading a full-client update](/img/screenshots/boot-update.webp)

## 2. Change hot-update code

Edit `Assets/Game/Scripts/HotUpdate/HotUpdateTextDemo.cs` and change only the
text assigned to `targetText.text`. Keep the script under the
`Game.HotUpdate.asmdef` boundary.

![Game.HotUpdate text running in the Main scene](/img/screenshots/main-hot-update.webp)

## 3. Run again

Start from Boot again. The Main scene text should show your new message.

## 4. Create the first release

Open **QHY Framework → Release**, select a target and `v1.0.0`, configure the
platform Resource Base URL, and click **Full Package Build**. Upload resources
and client. For later code/resource-only changes, use **HotUpdateOnly Build**.

**Expected:** Boot reaches Main, the hot-update component changes the UGUI Text,
and output appears under `Releases/default/<Platform>/v1.0.0`.

If `Game.HotUpdate.dll` is an invalid Address, build through the Release window
so generated binaries and collectors are prepared together.

**Next:** [QHY startup and update workflows](../concepts/qhy-workflows.md).
