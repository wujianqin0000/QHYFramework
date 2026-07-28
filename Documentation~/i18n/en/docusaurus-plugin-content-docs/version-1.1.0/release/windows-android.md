---
title: Windows and Android
---

# Windows and Android release {#windows-android}

Windows Full Package produces a client folder and
`Client_Windows64_v1.0.1.zip`. Disable Development Build, script debugging,
Profiler connection, debug symbols, and unnecessary built-in assets to reduce
size. Keep most content in remote YooAsset bundles.

Android Full Package produces an APK. In-place updates require the same
Application Identifier and signing certificate, plus a higher versionCode.
Android 8+ may require the user to allow “install unknown apps.”

A custom Keystore is recommended, not forced. Debug signing works for internal
testing but changing certificates later prevents existing users from installing
updates. Back up the production Keystore before the first public release.
