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

The Release window has one **Automatic Versioning** master toggle for both values. On
the first load of this new rule, each project/platform preference migrates to enabled;
a later manual opt-out is remembered and is not forced back on.
When enabled, ClientVersion is a read-only baseline, Major/Minor/Patch Full Build
buttons show both candidate versions, and HotUpdateOnly uses the displayed next
resource revision. Resource selection skips published baselines, local `Releases`
snapshots, and immutable YooAsset output left by a failed attempt.

The client increment is committed only after a successful Full Package build. When
the toggle is disabled, both ClientVersion and ResourceVersion become manual inputs.
An empty, malformed, mismatched, or non-increasing manual resource version blocks the
build and is never silently replaced.

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
Releases/<channel>/<platform>/<ClientVersion>/
  Revisions/
    <ResourceVersion>/
      CDN/
      Client/             # Full Package only
      release-report.json
      upload-plan.json
  SharedBundles/
    <content-addressed bundle files>
```

`Revisions` keeps every `-rNNNN` snapshot together instead of scattering revision
directories across the client-version root. `SharedBundles` is an immutable local
content store. On Windows, each CDN snapshot uses NTFS hard links to the same bundle
bytes, while manifests, version pointers, reports, and client artifacts remain unique
to that revision. A same-name bundle with a different length or SHA-256 stops the
build. If hard links are unavailable, QHY safely falls back to regular copies; output
and upload semantics remain the same, but disk deduplication is unavailable.

This changes only the local `Releases` layout. FTP/CDN still uses the existing
`<remote-root>/<ClientVersion>` compatibility directory and does not create an
`-rNNNN` subdirectory per upload. Legacy local snapshots at
`<ClientVersion>/<ResourceVersion>` are neither moved nor deleted: they remain valid
baselines and rollback sources and are included in automatic revision discovery. New
builds always use `Revisions`.
