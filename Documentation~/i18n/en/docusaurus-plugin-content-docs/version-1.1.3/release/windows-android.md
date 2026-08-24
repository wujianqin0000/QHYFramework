---
title: Windows and Android
---

# Windows and Android release {#windows-android}

Windows Full Package produces a client folder and
`Client_Windows64_v1.0.1.zip`. Disable Development Build, script debugging,
Profiler connection, debug symbols, and unnecessary built-in assets to reduce
size. Keep most content in remote YooAsset bundles.

QHY Framework 1.1.3 scans `GameAssembly.dll` after the build and requires the
HybridCLR interpreter marker `InterpreterImage::GetClassFromToken`. If it is
missing, the Player probably used Unity's global IL2CPP and the release fails
before a complete uploadable report is produced.

Android Full Package produces an APK. In-place updates require the same
Application Identifier and signing certificate, plus a higher versionCode.
Android 8+ may require the user to allow “install unknown apps.”

For APK/AAB output, QHY opens the archive and validates `libil2cpp.so` for every
ABI. A missing interpreter marker in any ABI stops the release. Merely finding
`LoadMetadataForAOTAssembly` is insufficient because stock Unity IL2CPP output
may still contain that managed-method metadata.

A custom Keystore is recommended, not forced. Debug signing works for internal
testing but changing certificates later prevents existing users from installing
updates. Back up the production Keystore before the first public release.
