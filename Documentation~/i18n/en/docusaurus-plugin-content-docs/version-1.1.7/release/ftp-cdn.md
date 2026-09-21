---
title: FTP, Manual Upload, and Optional CDN
---

# schema v5 multi-game CDN/Origin publication {#ftp-cdn}

QHY separates each platform by cache behavior: `cdn` contains immutable content and `origin` contains mutable pointers plus the publication index. QHY calls no CDN vendor API and does not gate publication on edge validation.

:::danger No legacy compatibility
Schema v5 neither reads nor migrates the former root-level platform layout, indexes, runtime configs, or state. Clear `Releases`, `QHYBuilds`, `ProjectSettings/QHYFramework/ReleaseState`, and this game's server resources, then create a new FullPackage.
:::

## Platform profiles

Switch Unity BuildTarget first; Settings displays only that target's profile. Enter only the resource root:

~~~text
BaseURL: https://aco.ai20.top
Game Resource Directory: my-game
~~~

A bare host such as `aco.ai20.top` is also accepted and the Inspector stores it as
`https://aco.ai20.top`. Publication validates URL syntax only for the target platform; an unfinished
inactive platform does not block the build, while duplicate profiles and `Unknown` still do.

For Android, QHY derives `https://aco.ai20.top/my-game/android/cdn` and `https://aco.ai20.top/my-game/android/origin`; Windows uses `/my-game/windows`. `gameDirectory` is one safe project-wide path segment. Give every game under a shared server root a different value. Switching BuildTarget displays the corresponding BaseURL while profiles remain independently stored. Duplicate platform keys and `Unknown` are rejected, and publication requires a profile for the active target.

## Server mirror

~~~text
Releases/
└─ {gameDirectory}/
   ├─ android/
   │  ├─ cdn/{bundles,versions,clients}
   │  └─ origin/{current,qhy.json}
   └─ windows/
      ├─ cdn/{bundles,versions,clients}
      └─ origin/{current,qhy.json}
~~~

Bundles use `cdn/bundles/{hash}.bundle`; manifests use `cdn/versions/{clientVersion}/{resourceVersion}.{bytes|hash}`; client packages use `cdn/clients/{clientVersion}.{apk|zip}`. Version pointers and `client.json` live under `origin/current`. `origin/qhy.json` is the schema v5 FTP index.

## Upload and rollback

FTP no longer has a RemoteRoot setting. The account login directory is the shared server resource root. The uploader reads `{gameDirectory}/{platform}/origin/qhy.json`, then runs lightweight parallel FTP `SIZE` probes before skipping indexed Bundles. A skip requires matching index SHA-256/length and an actual `{gameDirectory}/{platform}/cdn/bundles/{hash}.bundle` with the expected length. Missing or mismatched objects are uploaded again; a server without `SIZE` also falls back to a safe re-upload. QHY then uploads CDN files, Origin metadata, and finally `origin/current`. Only complete success records Published.

For example, with login root `/www/game-resources` and game directory `my-game`, Android files go to `/www/game-resources/my-game/android/cdn` and `/www/game-resources/my-game/android/origin`. Another game can use `other-game` without collisions. Do not repeat the game directory in BaseURL or FTP settings.

For manual updates, upload `Upload/1-Files` and then `Upload/2-Publish` before verification. **Open 1-Files** and **Open 2-Publish** are enabled only when the exact directories exist for the current ClientVersion/ResourceVersion. An unbuilt, incomplete, or no-change version creates no Upload directory and never passes an empty path to the system file manager. Verification selects CDN Root or Origin Root according to file class and cache-busts only current pointers. Rollback changes only `origin/current/{clientVersion}.version`.

## CDN rules

| Path | Rule |
|---|---|
| `*/cdn/bundles/*`, `*/cdn/versions/*` | long-lived, immutable |
| `*/cdn/clients/*` | long-lived, immutable, Range enabled |
| `*/origin/current/*` | no cache, TTL 0, or route bypass |
| `*/origin/qhy.json` | no cache or short TTL |
| 404 | no cache or very short TTL |

Origin means a bypass/no-cache path under the same platform hostname, not a separate origin hostname. Schema v5 does not support separate CDN and origin domains. Match CDN policies against `/{gameDirectory}/{platform}/cdn/*` and `/{gameDirectory}/{platform}/origin/*`.
