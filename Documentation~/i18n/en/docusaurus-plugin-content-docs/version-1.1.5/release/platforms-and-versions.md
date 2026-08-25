---
title: Platforms and versions
---

# Platform configuration and versions {#platforms-versions}

The current release system uses two independent values:

| Value | Example | Changes when |
|---|---|---|
| `ClientVersion` | `v1.0.4` | Full Package changes the client/AOT/native configuration |
| `ResourceVersion` | `v1.0.4-r0007` | Every code or content hot update |

Client versions use `v<major>.<minor>.<patch>`; Android version code remains
`major × 1,000,000 + minor × 1,000 + patch`. A resource version must start with
its client version and an increasing, at-least-four-digit revision. Existing
resource versions are immutable.

Windows and Android retain independent URLs and baselines. HotUpdateOnly reads
the published Full Package client version, does not write Player Version or
Android version code, and advances only ResourceVersion.

Version Automation offers Major, Minor, and Patch Full Builds. The increment is
committed only after a successful full build. The first resource build defaults
to `-r0001`; a published resource baseline advances to the next revision.

Configure roots without a version:

```text
https://example.com/CDN/PC
https://example.com/Client/PC
```

QHY appends the client version (`/v1.0.1`) to resources and `/latest.json` to
the client root. The compatibility directory retains multiple versioned
manifests and all content-addressed bundles. `GamePackage.version` selects the
active resource revision, so rollback only changes this pointer.

Local immutable snapshots use:

```text
Releases/<channel>/<platform>/<ClientVersion>/<ResourceVersion>/
```
