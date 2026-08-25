# Changelog

All notable changes to this package are documented here.

## [Unreleased]

## [1.1.5] - 2026-08-25

- Fixed Windows publication validation resolving `npm.cmd` relative to a `Documentation~` working
  directory and incorrectly looking under `Documentation\\~`. The publisher now resolves absolute
  Node/npm paths, invokes `npm-cli.js` through `node.exe`, and runs `npm ci` in clean publication
  workspaces before documentation checks.
- Fixed the QHYFrameworkDeveloper publisher window appearing blank on first open. The window now
  paints immediately, displays a loading state, and defers lightweight package inspection until the
  next Editor callback; full package and documentation validation only runs on explicit validation
  or publication.

- Aligned the framework-developer UPM publisher with release-time version selection. Development
  keeps the last stable version unchanged and records changes under `Unreleased`/Next. At publish
  time the tool promotes the changelog, updates README and version files, creates bilingual stable
  snapshots, runs `npm run check`, publishes both remotes, and only then writes the version locally.

- Stopped release builds from clearing the global YooAsset Collector configuration. Added
  `InitializeOnly` (default), `ManagedGroupsOnly`, and `External` ownership modes, with stable
  `QHYFramework.Managed:v2` collector markers and collision-safe legacy migration.
- Replaced the directory-wide default packing policy with update-correlated defaults: root assets
  and scenes are independent, nested UI/Common/Audio content groups by first-level feature folder,
  and HotUpdate/AOT metadata use separate collector bundles.
- Split immutable client versions (`v1.0.4`) from monotonically increasing resource versions
  (`v1.0.4-r0007`). HotUpdateOnly now reads the published client baseline, never changes
  `PlayerSettings.bundleVersion`, and refuses same-resource-version overwrite.
- Added YooAsset build-report delta analysis, `upload-plan.json`, expanded `release-report.json`,
  TypeTree suspicion diagnostics, and configurable 4 MiB warning / 16 MiB error bundle-size checks.
- Made FTP publication incremental and content-address safe. Existing hashed bundles are verified by
  length and SHA-256, matching files are skipped, collisions stop publication, and the upload dialog
  separates snapshot, actual upload, and estimated client download sizes.
- Published manifests safely by uploading bundles and audit files first, then replacing
  `GamePackage.version` through an `.uploading` file with rollback backup. Added one-click rollback
  to the previous resource version without deleting bundles.
- Strengthened HotUpdateOnly AOT freezing with client-version-specific snapshots and per-file
  SHA-256 verification. Any AOT metadata bundle change blocks the hot update.
- Removed `ftpPassword` from `QHYFrameworkSettings`. Passwords now come from the current Editor
  session or `QHY_FTP_PASSWORD`, are redacted from errors, cleared after upload/cancel/failure, and
  scanned for project-file residue.

## [1.1.4] - 2026-08-24

- Added published-client AOT metadata snapshot restoration for HotUpdateOnly and versioned
  bilingual documentation snapshots.
- Preserved old-client compatibility when local AOT output changes while keeping the target client
  baseline visible in release reports.

## [1.1.3] - 2026-08-24

- Fixed the dependency bootstrap incorrectly checking `il2cpp/il2cpp/bin` and treating a valid
  HybridCLR 8.12 `build/deploy` installation as missing.
- Protected full releases from `InitializeOnLoad` callbacks switching back to Unity's global
  IL2CPP after HybridCLR generation or an asset refresh.
- Reasserted and verified HybridCLR settings plus `UNITY_IL2CPP_PATH` immediately before
  `BuildPipeline.BuildPlayer`, including package/local-runtime version consistency checks.
- Added post-build interpreter-marker validation for Windows `GameAssembly.dll` and every Android
  `libil2cpp.so` in APK/AAB output, preventing a stock-IL2CPP player from being published.

## [1.0.1] - 2026-07-21

- Renamed the settings type, source file, Inspector and generated asset directly to
  `QHYFrameworkSettings`; new projects use `Assets/Game/Config/QHYFrameworkSettings.asset`.
- Renamed the user-facing editor product and top-level menu from `Game Integration` to
  `QHY Framework`, including release-window titles, documentation and editor log prefixes, while
  preserving existing namespaces, command-line APIs, cache paths and preferences for compatibility.
- Removed publisher-specific CDN defaults and examples from the distributable package. New
  `IntegrationSettings` assets have empty platform URLs, FTP fields start empty, and release/FTP
  EditorPrefs are scoped by Unity project so credentials and platform versions cannot leak into a
  different project; every unpublished platform starts at `v1.0.0`.
- Moved FTP Host, Port, Username and Password into editor-only `IntegrationSettings` fields and
  added two-way synchronization with the release window. New projects use empty credentials and
  port 21; FTP credentials are excluded from the Player-side class layout.
- Replaced the empty `GameHotUpdateAssemblyMarker` with a visible `HotUpdateEntry` test. Fresh
  projects now create a `Main` root with that hot-update component plus a UGUI
  `HotUpdateStatusText`; entering Main proves that the hot-update assembly ran by changing its text.
- Made automatic scene initialization domain-reload safe: open Main/Boot scenes are detected through
  the scene manager and AssetDatabase, and no scene is modified until `HotUpdateEntry` has compiled.
  Populated unsaved template scenes are now safely reused as Main instead of attempting an invalid
  additive scene creation; their temporary template objects are cleared before the standard Main
  hierarchy is generated.
- Fixed fresh-project imports failing in Unity/Bee when HybridCLR settings referenced a missing
  `HybridCLRData/LocalIl2CppData-*` toolchain. The dependency bootstrap now switches to Unity's
  global IL2CPP before installing the remaining packages and clears a stale process override.
- Deferred hot-update DLL compilation from automatic project initialization to explicit release
  builds, so importing the package never requires HybridCLR's local IL2CPP installation.

## [1.0.0] - 2026-07-20

- Converted the framework integration into the standard UPM package
  `com.wjq.qhy-framework`.
- Bundled the compatible QFramework source distribution.
- Declared UGUI and the Unity built-in modules used by QFramework as package dependencies.
- Added a one-click offline dependency installer for HybridCLR 8.12.0 and
  YooAsset 3.0.4; users only need to add `com.wjq.qhy-framework`.
- Moved runtime, editor, tests, Android updater, Boot UI template and Windows
  updater into UPM package directories.
- Preserved host project content and generated output under `Assets/Game`.
- Added a generated `Main Camera` and `AudioListener` to new Boot scenes, including migration for
  previously generated Boot scenes that do not contain a camera.
- Added an independent perspective `Main Camera` and `AudioListener` to new Main scenes, including
  automatic migration for previously generated Main scenes without a camera.
- Editor simulation now uses Unity's already loaded hot-update assembly and no longer requires a
  generated `Game.HotUpdate.dll` YooAsset entry before entering Play Mode.
- Project preparation now creates a minimal source marker for the default `Game.HotUpdate`
  assembly and synchronizes configured hot-update assembly names into HybridCLR settings. Initial
  DLL generation is deferred to the release build because Editor simulation uses the loaded assembly.
- Relaxed release validation so `Assets/Scenes` may contain additional scenes while Build Settings
  still permits only the built-in Boot scene.
- Automatic semantic-version builds now commit the platform version only after the full client
  build succeeds and restore both Player Version and Android versionCode after failure or cancellation.
- Replaced the manual Prepare Project step with one-time automatic host-project initialization,
  recorded in `ProjectSettings/QHYFrameworkProjectState.json` after all setup stages succeed.
- Reduced the Game Integration menu to Release Window and Language; removed setup, prefab,
  StreamingAssets copy, and dependency-repair menu commands from the fresh-project workflow.
- Fresh-project initialization now uses Unity's global il2cpp toolchain until HybridCLR local
  il2cpp is installed. The lightweight dependency bootstrap now repairs stale HybridCLR settings
  before the main integration editor assembly loads and clears an invalid `UNITY_IL2CPP_PATH`,
  preventing missing `HybridCLRData/LocalIl2CppData-*` failures during Unity/Bee compilation.
- The first Full Package Build now installs or upgrades HybridCLR local il2cpp automatically before
  running Generate/All, then switches HybridCLR back to the patched local toolchain.
