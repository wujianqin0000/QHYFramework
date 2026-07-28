---
title: Platforms and versions
---

# Platform configuration and versions {#platforms-versions}

Windows and Android keep independent URLs and versions. If Android reaches
`v1.2.0` while Windows is still `v1.1.1`, switching back restores Windows
`v1.1.1`. A platform with no history starts at `v1.0.0`.

Versions must be `v<major>.<minor>.<patch>` and synchronize bidirectionally with
Player Version. Android version code is
`major × 1,000,000 + minor × 1,000 + patch`.

Version Automation offers Major, Minor, and Patch Full Builds. The increment is
committed only after a successful full build; cancellation/failure does not
consume it. HotUpdateOnly retains the published client version.

Configure roots without a version:

```text
https://example.com/CDN/PC
https://example.com/Client/PC
```

QHY appends `/v1.0.1` to resources and `/latest.json` to the client root.
