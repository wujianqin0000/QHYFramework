---
title: QHYFrameworkSettings fields
---

# QHYFrameworkSettings reference {#settings}

The asset normally lives at `Assets/Game/Config/QHYFrameworkSettings.asset`.

## Platform resources

`gameDirectory` is the project-wide server directory, for example `my-game`. It is shared by every
platform and accepts only lowercase letters, digits, dots, hyphens, and underscores. It must be one
path segment. Give every game under the same server resource root a distinct value.

The Inspector has no manual platform selector and does not expand every profile. It displays only the profile for Unity's active BuildTarget. Switching BuildTarget automatically shows the new platform while every `baseUrl` remains independently stored. Use **Create Active Platform Profile** if that target has no profile.

Changing **Build Target** in the QHY Release window performs a real Unity Active Build Target
switch and refreshes this Inspector; it is not merely a one-build parameter. Asset import and script
compilation may run during the switch.

Enter only the server resource root, for example `aco.ai20.top` or `https://aco.ai20.top`, without the game directory, `/android`, `/windows`, `/cdn`, or `/origin`. A bare host is normalized to HTTPS; an exact Markdown link or angle-bracket URL copied from rich text is unwrapped and stored canonically. QHY derives `cdnRoot = baseUrl/{gameDirectory}/{platform}/cdn` and `originRoot = baseUrl/{gameDirectory}/{platform}/origin`. Windows, Android, iOS, macOS, Linux, and WebGL may store profiles. Publication validates URL syntax only for its target platform, so an unfinished inactive platform does not block it; duplicate platform keys and `Unknown` remain global errors. One-click Player publication remains limited to Windows and Android.

URLs must be absolute HTTP(S) addresses without query, fragment, credentials, a platform folder, `/cdn`, or `/origin`. Legacy `resourceBaseUrl` is removed without migration.

## Download and startup

Download settings control request timeout, concurrency, retry count, per-frame request creation, watchdog timeout, built-in Manifest copying, unused-cache cleanup, and startup pointer refresh. The package is fixed to `GamePackage`. `startupSceneAddress` defaults to `Main` and `hotUpdateAssemblies` defaults to `Game.HotUpdate`.

FTP does not belong to Settings. Host, Port, user, FTPS, and passive mode remain target-platform-scoped Release-window preferences; the password may use Windows Credential Manager. FTP has no RemoteRoot setting: the account login directory is the shared resource root and uploaded paths begin with `{gameDirectory}/{platform}`.
