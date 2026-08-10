---
title: FTP and CDN
---

# FTP upload and server layout {#ftp-cdn}

```text
/CDN/PC/v1.0.1/{GamePackage.version, manifest, bundles}
/Client/PC/v1.0.1/Client_Windows64_v1.0.1.zip
/Client/PC/latest.json
```

Android uses separate `/CDN/Android` and `/Client/Android` trees.

“Upload Hot Update” uploads CDN content only. “Upload Hot Update and Client”
uploads resources, then ZIP/APK, then atomically replaces `latest.json`.
Client upload requires a version newer than the remote manifest.

FTP Host, Port (21), Username, and Password bind bidirectionally to
`QHYFrameworkSettings`. The Host should not include `/CDN/...`. Passive mode is
the default.

`150 Accepted data connection` is an intermediate success; the uploader waits
for `226 Transfer complete`. Persistent failures usually indicate passive port,
firewall, permission, or disk-space problems.
