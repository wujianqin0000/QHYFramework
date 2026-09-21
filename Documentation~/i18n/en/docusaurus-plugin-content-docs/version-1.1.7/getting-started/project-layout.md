---
title: Project layout
---

# Project layout {#project-layout}

```text
Assets/Game/{Config,Content,Scripts/HotUpdate,Generated}
Releases/{gameDirectory}/{android,windows}/{cdn/{bundles,versions,clients},origin/{current,qhy.json}}
QHYBuilds/{platform}/{clientVersion}/{resourceVersion}
ProjectSettings/QHYFramework/ReleaseState/{platform}
```

`Releases/{gameDirectory}` is this game's uploadable server mirror. Distinct game directories let
multiple games share one server root without collisions. `QHYBuilds` contains Players, reports, plans, hashes,
and two-phase Upload views. Ignore both in Plastic. Commit `ProjectSettings/QHYFramework/ReleaseState`
so other machines and CI share the Published baseline and its frozen per-ClientVersion AOT metadata.

On project initialization and before each publication, QHY idempotently maintains a marked block
in the project-root Plastic `ignore.conf`; when Git is detected it also maintains `.gitignore`.
`Releases` and `QHYBuilds` are excluded without replacing user rules. Ignore rules affect only
untracked files, so directories committed earlier must be untracked manually while keeping their
local files. Never ignore `ReleaseState`.

Boot is the only required built-in scene; content, hot-update DLLs, and AOT metadata belong to
`GamePackage`. Schema v5 rejects the former root-level platform layout and legacy
`Releases/Server`, `Revisions`, and `SharedBundles`
instead of migrating them.
