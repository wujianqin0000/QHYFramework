---
title: Ten-minute quick start
---

# Complete the first hot update {#quick-start}

Run `Assets/Scenes/Boot.unity` in `EditorSimulateMode`, edit
`Assets/Game/Scripts/HotUpdate/HotUpdateTextDemo.cs`, and run Boot again to confirm that
`Game.HotUpdate` changes are loaded.

:::danger New protocol
Schema v5 supports no earlier publication data. Manually clear `Releases`,
`QHYBuilds`, `ProjectSettings/QHYFramework/ReleaseState`, and the server resource root. The first
publication must be a Full Package Build.
:::

Switch Unity BuildTarget to Android or Windows first. Set one project-wide game directory such as
`my-game`; Settings shows only the active target's BaseURL. Enter a server resource root such as
`https://aco.ai20.top`; QHY appends `/my-game/android` or `/my-game/windows`. Then open **QHY Framework → Release**, choose a platform/version, and run
**Full Package Build**. FTP can upload automatically. For manual publication upload
`Releases/{gameDirectory}/{platform}` to the same relative server location, with `origin/current` last, then click
**Verify Upload and Complete Publication**.

For later hot updates, upload `Upload/1-Files` and then `Upload/2-Publish`. No actual change means
no new ResourceVersion and no upload action. See [FTP and manual upload](../release/ftp-cdn.md).
