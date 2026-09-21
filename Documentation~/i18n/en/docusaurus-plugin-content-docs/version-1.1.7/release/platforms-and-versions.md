---
title: Platforms and versions
---

# Platforms and independent versions {#platforms-versions}

ClientVersion changes with FullPackage Player/AOT/native changes. ResourceVersion changes for each real code or content update. Automatic versioning manages both; no-change builds consume no revision.

## Switching platform in the Release window

Changing **Build Target** now immediately switches Unity's Active Build Target instead of changing
only a release parameter. QHY first saves the old platform's client/resource versions, Development,
automatic-versioning, and FTP preferences, then loads the selected platform's preferences after a
successful switch. If `QHYFrameworkSettings` is selected, its Inspector refreshes to show the new
platform's BaseURL.

Unity may reimport assets and compile scripts during the switch. The selector is disabled while a
build, upload, compilation, or AssetDatabase update is active. Missing Unity Build Support or a
failed switch restores the target shown by the Release window to Unity's actual active platform, so
the two cannot silently diverge.

Settings displays only the active BuildTarget BaseURL and has one project-wide `gameDirectory`.
Enter only the server resource root; QHY appends the game and platform folders while retaining each
platform profile independently. Schema v5 uses:

~~~text
{resourceBaseUrl}/{gameDirectory}/{platform}/
├─ cdn/{bundles,versions,clients}
└─ origin/{current,qhy.json}
~~~

`gameDirectory` is one lowercase path segment containing letters, digits, dots, hyphens, or
underscores. Give each game under a shared server root a different value. Schema v5 is incompatible
with old output and state. Clear them and create a new FullPackage baseline. See [FTP and manual upload](ftp-cdn.md).
