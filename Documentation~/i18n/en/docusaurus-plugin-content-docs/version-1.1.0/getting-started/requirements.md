---
title: Requirements
---

# Requirements {#requirements}

| Item | Requirement | Purpose |
|---|---|---|
| Unity | `2022.3.62f2c1` or a verified 2022.3 LTS | Editing and IL2CPP builds |
| Git | 2.30+, long paths enabled | Install through a Git UPM URL |
| Windows module | Windows Build Support (IL2CPP) | Windows64 client |
| Android modules | Android SDK, NDK, OpenJDK | Android APK |
| Node.js | 22, maintainers only | Documentation site |

On Windows, run:

```bash
git config --global core.longpaths true
```

Install platform modules through Unity Hub. A missing IL2CPP module commonly
causes `LocalIl2CppData-.../il2cpp/bin does not exist`.

For HostPlayMode, your HTTP server must serve `.version`, `.json`, `.bytes`,
`.bundle`, `.zip`, and `.apk` as static files. Range requests are recommended
for resumable client downloads. Keep old version directories online.

**Expected:** switching the active build target succeeds with no compilation
errors. **Next:** [Install QHY Framework](installation.md).
