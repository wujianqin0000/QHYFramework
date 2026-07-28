---
title: Full-client update
---

# Full-client update {#client-update}

Full Package creates a ZIP/APK and `latest.json` containing schema, platform,
semantic version, Android version code, URL/type/name, size, SHA-256, executable,
publication time, and Mandatory.

Boot checks this manifest before YooAsset. Failure or malformed data logs a
warning and continues the current client. A higher mandatory version stays in
Boot, asks for confirmation, downloads to `persistentDataPath/ClientUpdates`,
resumes with HTTP Range when possible, and verifies length plus SHA-256.

On Windows, a standalone updater waits for the game, safely extracts without
ZIP path traversal, swaps staging/backup atomically, restarts, and rolls back
on launch failure. It requests elevation when required.

On Android, FileProvider passes the APK to the system installer. The user must
approve it; package identifier, certificate, and versionCode must be compatible.

Upload the client and resources first, and `latest.json` last. Keep old resource
directories. The first updater-capable baseline still requires manual delivery.
