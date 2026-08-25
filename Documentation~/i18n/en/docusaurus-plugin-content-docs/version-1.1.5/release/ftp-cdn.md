---
title: FTP and CDN
---

# FTP upload and server layout {#ftp-cdn}

```text
/CDN/PC/v1.0.1/{GamePackage.version, versioned manifests, content-addressed bundles}
/Client/PC/v1.0.1/Client_Windows64_v1.0.1.zip
/Client/PC/latest.json
```

Android uses separate `/CDN/Android` and `/Client/Android` trees.

Each build creates `upload-plan.json`. QHY compares logical bundles from the
current and previous YooAsset reports and separates Added, Changed, Unchanged,
and Removed entries. The window distinguishes the full snapshot, actual FTP
transfer after remote preflight, and estimated client download.

Existing content-addressed bundles are checked by remote length and SHA-256.
An exact match is skipped; a same-name mismatch aborts publication. Removed
bundles remain available for old manifests and clients.

Upload order is new bundles, manifest bytes/JSON, hash, reports/audit, then
`GamePackage.version.uploading`. QHY backs up the old pointer, atomically renames
the temporary pointer, and restores the old pointer on failure. Client
`latest.json` uses the same last-pointer principle and requires a newer client
version. Rollback switches only `GamePackage.version` to the previous resource
revision.

FTP Host, Port (21), and Username may be stored in `QHYFrameworkSettings`.
Passwords are never serialized after this change: enter one for the current Release
Window session or inject `QHY_FTP_PASSWORD`; CI may override the username with
`QHY_FTP_USERNAME`. Password memory is cleared after success, failure,
cancellation, or window close, and errors are redacted. The Host should not
include `/CDN/...`; passive mode is the default. Prefer FTPS, SFTP, or HTTPS in
production.

`150 Accepted data connection` is an intermediate success; the uploader waits
for `226 Transfer complete`. Persistent failures usually indicate passive port,
firewall, permission, or disk-space problems.
