---
title: FTP、手动上传与可选 CDN
---

# schema v5 多游戏 CDN/Origin 发布协议 {#ftp-cdn}

QHY 将每个平台的文件按缓存性质物理分开：`cdn` 只放不可变文件，`origin` 只放可变指针和发布索引。框架不调用 CDN 厂商 API，也不强制验证 CDN 节点。

:::danger 不兼容旧版
schema v5 不读取或迁移此前的根目录直出平台结构、`qhy.json`、运行时配置和发布状态。启用前手工清空 `Releases`、`QHYBuilds`、`ProjectSettings/QHYFramework/ReleaseState` 和该游戏的服务器资源，然后重新 FullPackage。
:::

## 平台配置

先切换 Unity BuildTarget，`QHYFrameworkSettings` 只显示当前平台的配置。BaseURL 只填写资源总根：

~~~text
BaseURL: https://aco.ai20.top
游戏资源目录: my-game
~~~

BaseURL 也可以填写裸域名 `aco.ai20.top`，Inspector 会自动保存为
`https://aco.ai20.top`。发布只校验当前目标平台的 URL；另一个平台尚未填完不会阻止当前
构建，但重复平台项和 `Unknown` 仍会阻止。

QHY 按当前平台自动派生 `https://aco.ai20.top/my-game/android/cdn` 与 `https://aco.ai20.top/my-game/android/origin`，Windows 则使用 `/my-game/windows`。`gameDirectory` 是所有平台共用的单个安全路径段；同一服务器根下的不同游戏应使用不同值。切换 BuildTarget 后 Inspector 自动显示对应 BaseURL，底层仍按平台独立保存；同一平台只允许一项配置，`Unknown` 禁止使用。当前构建目标缺少 Profile 时阻止发布。

## 服务器镜像

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

- `cdn` 中所有 URL 不可变，同名同版本禁止覆盖。
- `origin/current` 是唯一激活入口，必须最后上传。
- `origin/qhy.json` 是 schema v5 FTP Bundle SHA-256 索引，不参与客户端启动。
- `Releases` 是唯一服务器镜像；`QHYBuilds` 仅保存 Player、报告和增量上传视图。

## 客户端请求

| 内容 | 地址 |
|---|---|
| ResourceVersion 指针 | `origin/current/{clientVersion}.version` |
| 客户端更新清单 | `origin/current/client.json` |
| Manifest/Hash | `cdn/versions/{clientVersion}/{resourceVersion}.*` |
| Bundle | `cdn/bundles/{hash}.bundle` |
| APK/ZIP | `cdn/clients/{clientVersion}.*` |

## FTP 自动上传

FTP 不再配置 RemoteRoot。FTP 账号登录后的根目录是多个游戏共用的服务器资源根。上传器读取 `{gameDirectory}/{platform}/origin/qhy.json`，再对准备跳过的 Bundle 并行执行轻量 FTP `SIZE` 检查；只有索引中的 SHA-256/长度一致，并且实际 `{gameDirectory}/{platform}/cdn/bundles/{hash}.bundle` 存在且长度一致时才跳过。索引残留但文件丢失或长度不符会自动补传；服务器不支持 `SIZE` 时也会安全地重新上传，而不会冒险激活缺文件的 Manifest。随后依次上传 CDN 文件、Origin 索引，最后串行上传 `origin/current`。任何前置步骤失败都不会激活版本；全部成功后才登记 Published。

例如登录根为 `/www/game-resources`、游戏目录为 `my-game`，Android 文件会上传到 `/www/game-resources/my-game/android/cdn` 与 `/www/game-resources/my-game/android/origin`。另一个游戏可使用 `other-game`，两者不会覆盖。不要在 BaseURL 或 FTP 配置中重复填写游戏目录。

## 手动上传

首次发布可把 `Releases/{gameDirectory}/{platform}` 上传到服务器的 `{gameDirectory}/{platform}`，但必须最后上传 `origin/current`。也可以把整个 `Releases` 的内容合并到服务器资源根；其中已包含游戏目录和平台层。增量发布严格执行：

1. 上传 `Upload/1-Files`，其中只有新增/变化的 `cdn` 文件和 `origin/qhy.json`。
2. 上传 `Upload/2-Publish`，其中只有 `origin/current`。
3. 点击 **检查上传并完成发布**。

“打开 1-Files”和“打开 2-Publish”只在当前 ClientVersion/ResourceVersion 的精确目录真实存在时启用。尚未构建、版本字段不完整或本次构建没有实际变化时，框架不会生成 Upload，按钮保持禁用，也不会把空路径交给系统文件管理器。

检查器分别通过 CDN Root 和 Origin Root 校验 SHA-256，只对 current 指针添加缓存破坏参数。回滚也只修改 `origin/current/{clientVersion}.version`。

## CDN 规则

| 路径 | 规则 |
|---|---|
| `*/cdn/bundles/*`、`*/cdn/versions/*` | 长期缓存、immutable |
| `*/cdn/clients/*` | 长期缓存、immutable、支持 Range |
| `*/origin/current/*` | 不缓存、TTL 0 或路径绕过 |
| `*/origin/qhy.json` | 不缓存或短缓存 |
| 404 | 不缓存或极短缓存 |

Origin 表示同一平台域名下的“不缓存/强制回源路径”，不是另一个源站域名。当前 schema v5 不支持 CDN 与源站使用不同域名。CDN 路径规则应匹配 `/{gameDirectory}/{platform}/cdn/*` 与 `/{gameDirectory}/{platform}/origin/*`。
