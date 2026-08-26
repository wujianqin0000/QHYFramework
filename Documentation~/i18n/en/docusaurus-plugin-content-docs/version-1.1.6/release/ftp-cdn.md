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

The uploader first downloads `.qhy-bundle-hashes.json` and validates existing
content-addressed bundles by indexed length and SHA-256. An exact match is skipped;
a mismatch aborts publication without downloading the Bundle payload. A scoped
mirror under `Library/QHYFramework/FtpVerificationCache` supports migration. Only
an index gap on a legacy server requires a one-time full Bundle verification.
New bundles atomically update the index before `GamePackage.version` is switched.
The FTP content directory must be owned by the publisher; never manually replace a
same-name bundle while retaining the old index. Removed bundles remain available
for old manifests and clients.

Index download and legacy verification report the active stage, current file,
completed count, and a cancel button. Migration can make the first upload slow;
after the index exists, normal publication downloads only the small index.

HotUpdateOnly reports **Nothing to Publish** when its bundle delta has no Added,
Changed, or Removed entries, discards only the new unpublished derived output, and
keeps that automatic ResourceVersion available for the next real change. A legacy
empty upload plan is also rejected before any FTP connection.

Upload order is new bundles, manifest bytes/JSON, hash, reports/audit, then
`GamePackage.version.uploading`. QHY backs up the old pointer, atomically renames
the temporary pointer, and restores the old pointer on failure. Client
`latest.json` uses the same last-pointer principle and requires a newer client
version. Resource rollback uses the same atomic pointer replacement.

## Restore an online resource version (rollback)

The Release window separates building, FTP upload, and failure recovery. If a new hot
update causes a problem, use the dedicated **Restore an Online Resource Version
(Rollback)** card. It displays the **Active Online Version**, explains that Bundles are
not deleted, and provides **Choose an Online Version to Restore…**. The menu lists every
locally complete revision recorded
in the successful-publication ledger whose channel, platform, and ClientVersion match
and whose revision does not exceed the highest published baseline. The active remote
revision is excluded. This prevents a local build that was never uploaded successfully
from being offered. A legacy baseline without a ledger temporarily falls back to the
highest-revision boundary, but every selected target still requires remote verification.

Before switching the pointer, QHY downloads the remote SHA-256 index and verifies the
target manifest `.bytes`/`.hash` plus every Bundle required by that target. A legacy
index gap uses the existing one-time full verification fallback. Validation has
cancellable per-file progress, and a missing or mismatched artifact aborts before the
pointer changes. On success only `GamePackage.version` is atomically replaced; no
Bundle is uploaded or deleted.

The baseline keeps the publication ledger and highest published revision while making the selected target
active. After rolling `r0005` back to `r0002`, the menu can still select `r0003`,
`r0004`, or `r0005`, supporting both multi-step rollback and undo.

Before the first successful resource upload, the restore action is disabled and the card
shows **No published version recorded**. An empty menu means there is no other revision
that is published, matches the current platform and ClientVersion, and has a complete
local snapshot.

After a successful resource upload the current ResourceVersion is recorded, the
resource-only button disables, and **Already Uploaded** is shown. For a Full Package
whose client is still pending, the combined button remains available and uploads only
that client; it disables after client success. A failed or interrupted attempt remains
retryable. Explicit rollback permits republishing the rolled-back version.

FTP Host, Port (21), Username, Passive, FTPS, and remote paths exist only in the
QHY Release window. They are stored per project/platform in local EditorPrefs and
are no longer fields in `QHYFrameworkSettings` or its asset.

**Remember FTP Password Securely** defaults to enabled on Windows and stores the
secret in Windows Credential Manager, never regular EditorPrefs or project files.
Clearing the option deletes the credential. `QHY_FTP_PASSWORD` and
`QHY_FTP_USERNAME` retain priority for CI. Non-Windows editors use the current
session or environment variables. Errors remain redacted and project text is scanned
for credential residue. The Host should not include `/CDN/...`; passive mode is the
default. Prefer FTPS, SFTP, or HTTPS in production.

`150 Accepted data connection` is an intermediate success; the uploader waits
for `226 Transfer complete`. Persistent failures usually indicate passive port,
firewall, permission, or disk-space problems.
