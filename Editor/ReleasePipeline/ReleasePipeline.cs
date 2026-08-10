using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Installer;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace GameIntegration.Editor
{
    internal static class BuildRecovery
    {
        public static void PrepareForHybridClr(BuildTarget target)
        {
            if (BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Unity 正在执行另一个 Player 构建，请等待其结束后再试。");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Unity 正在编译或刷新资源，请等待右下角任务结束后再试。");

            EditorUtility.ClearProgressBar();
            EditorUserBuildSettings.buildScriptsOnly = false;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CleanupCancelledBuild(target);

            if (!EditorUtility.scriptCompilationFailed)
                return;

            // A cancelled BuildPipeline can leave this flag set even when Bee produced the
            // latest player assemblies successfully. A clean compilation resets that state.
            CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
            throw new InvalidOperationException(
                "检测到 Unity 仍保留上次取消构建后的脚本编译失败状态。已自动请求一次干净重编译；" +
                "请等待 Unity 编译完成后再次点击构建。如果 Console 出现具体的 CS 编译错误，请先修复该错误。");
        }

        public static void CleanupAfterFailure(BuildTarget target)
        {
            EditorUtility.ClearProgressBar();
            EditorUserBuildSettings.buildScriptsOnly = false;
            CleanupCancelledBuild(target);
        }

        private static void CleanupCancelledBuild(BuildTarget target)
        {
            DeleteProjectDirectory(Path.Combine(SettingsUtil.HybridCLRDataDir,
                "StrippedAOTDllsTempProj", target.ToString()));
            DeleteProjectDirectory(Path.Combine("Temp", "StagingArea"));
        }

        private static void DeleteProjectDirectory(string path)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("拒绝清理项目目录之外的构建路径：" + fullPath);
            if (!Directory.Exists(fullPath))
                return;

            try
            {
                Directory.Delete(fullPath, true);
            }
            catch (Exception exception)
            {
                throw new IOException("无法清理取消构建留下的临时目录：" + fullPath +
                                      "。请确认没有其他 Unity 构建进程占用该目录。", exception);
            }
        }
    }

    public static class ReleasePipeline
    {
        public static string Run(ReleaseOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            SynchronizePlayerVersion(options);
            ValidateOptions(options);
            IntegrationProjectPreparer.SaveDirtyScenesOrThrow();
            EnsureActiveBuildTarget(options.target);
            BootUIPrefabGenerator.EnsureExists();
            QHYFrameworkSettings settings = IntegrationProjectPreparer.LoadSettings();
            IntegrationProjectPreparer.ConfigureBuildSettings();
            IntegrationProjectPreparer.ConfigureCollectors(settings);
            ReleaseValidation.ThrowIfInvalid(settings, false, options.target);

            if (options.mode == ReleaseMode.FullPackage)
            {
                BuildRecovery.PrepareForHybridClr(options.target);
                try
                {
                    EnsureHybridClrInstalled();
                    PrebuildCommand.GenerateAll();
                }
                catch (Exception exception)
                {
                    BuildRecovery.CleanupAfterFailure(options.target);
                    throw new InvalidOperationException(F(
                        "HybridCLR 生成失败。已清理取消构建残留，可直接重试。原始错误：{0}",
                        "HybridCLR generation failed. Cancelled-build artifacts were cleaned; you can retry directly. Original error: {0}",
                        exception.GetBaseException().Message), exception);
                }
                // HybridCLR Generate/All 可能触发程序集重载，旧的 UnityEngine.Object 引用会失效。
                settings = IntegrationProjectPreparer.LoadSettings();
                AotMetadataAutomation.Synchronize(settings, options.target, true, true);
                settings = IntegrationProjectPreparer.LoadSettings();
            }
            string[] effectiveAotMetadata = IntegrationProjectPreparer.CompileAndCopyHotUpdateAssemblies(
                settings, options.target, options.developmentBuild,
                options.mode == ReleaseMode.FullPackage);

            // CompileDll 和 AssetDatabase.Refresh 后必须重新加载 ScriptableObject。
            settings = IntegrationProjectPreparer.LoadSettings();
            IntegrationProjectPreparer.ConfigureCollectors(settings);
            ReleaseValidation.ThrowIfInvalid(settings, true, options.target, effectiveAotMetadata);

            string platform = GetPlatformName(options.target);
            string baseline = string.Empty;
            string baselinePath = Path.Combine(options.outputRoot, "Baselines", platform + ".sha256");
            string clientVersionBaselinePath = Path.Combine(options.outputRoot, "Baselines",
                platform + ".client-version");
            string androidSigningBaselinePath = Path.Combine(options.outputRoot, "Baselines",
                "Android.signing.sha256");
            string androidSigningBaseline = options.mode == ReleaseMode.FullPackage
                ? ValidateAndroidSigning(options, androidSigningBaselinePath)
                : string.Empty;
            if (options.mode == ReleaseMode.HotUpdateOnly)
            {
                ValidateHotUpdateClientVersion(clientVersionBaselinePath, options.packageVersion, platform);
                // HotUpdateOnly 面向的是已经发布的客户端。即使当前工程的 AOT 输出发生变化，
                // 也不能用它否定旧客户端，更不能把新 AOT 元数据混入旧客户端的热更包。
                // 基线只用于记录兼容目标，不再作为阻断热更新发布的条件。
                baseline = ReadPublishedAotBaseline(baselinePath);
                WarnIfLocalAotDiffers(options.target, baseline);
            }

            YooAsset.Editor.BuildResult yooResult = BuildYooAssetPackage(settings, options);
            string releaseRoot = GetReleaseRoot(options);
            string cdnRoot = Path.Combine(releaseRoot, "CDN");
            RecreateDirectory(cdnRoot);
            // IRemoteService 接收的是纯文件名，CDN 根目录必须直接包含 version、manifest 和 bundle。
            CopyDirectory(yooResult.OutputPackageDirectory, cdnRoot);

            if (options.mode == ReleaseMode.FullPackage)
            {
                string clientRoot = Path.Combine(releaseRoot, "Client");
                RecreateDirectory(clientRoot);
                BuildPlayer(options, clientRoot);

                // Player 构建会重新生成 AssembliesPostIl2CppStrip，必须在构建完成后记录真实客户端基线。
                baseline = ComputeAotBaseline(options.target);
                Directory.CreateDirectory(Path.GetDirectoryName(baselinePath) ?? options.outputRoot);
                File.WriteAllText(baselinePath, baseline, Encoding.UTF8);
                ClientArtifactBuilder.Build(releaseRoot, clientRoot, settings, options);
                File.WriteAllText(clientVersionBaselinePath, options.packageVersion, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(androidSigningBaseline))
                    File.WriteAllText(androidSigningBaselinePath, androidSigningBaseline, Encoding.UTF8);
            }

            string[] artifacts = WriteHashes(releaseRoot);
            var report = new ReleaseReportData
            {
                channel = options.channel,
                version = options.packageVersion,
                platform = platform,
                mode = options.mode.ToString(),
                unityVersion = Application.unityVersion,
                clientVersion = PlayerSettings.bundleVersion,
                remoteBaseUrl = settings.GetRemotePackageUrl(GetIntegrationPlatform(options.target),
                    options.packageVersion),
                clientManifestUrl = settings.GetClientManifestUrl(GetIntegrationPlatform(options.target)),
                timestampUtc = DateTime.UtcNow.ToString("O"),
                aotBaselineSha256 = baseline,
                aotMetadataAssemblies = effectiveAotMetadata,
                artifacts = artifacts
            };
            File.WriteAllText(Path.Combine(releaseRoot, "release-report.json"),
                JsonUtility.ToJson(report, true), Encoding.UTF8);
            AssetDatabase.Refresh();
            Debug.Log(F("[QHYFramework] 发布完成：{0}",
                "[QHYFramework] Release completed: {0}", releaseRoot));
            return releaseRoot;
        }

        private static void EnsureHybridClrInstalled()
        {
            var installer = new InstallerController();
            bool requiresInstall = !installer.HasInstalledHybridCLR() ||
                                   !string.Equals(installer.PackageVersion,
                                       installer.InstalledLibil2cppVersion,
                                       StringComparison.Ordinal);
            if (requiresInstall)
            {
                Debug.Log(L(
                    "[QHYFramework] 首次完整构建正在自动安装 HybridCLR 本地 il2cpp，请稍候…",
                    "[QHYFramework] Installing HybridCLR local il2cpp automatically for the first full build. Please wait..."));
                installer.InstallDefaultHybridCLR();
                installer = new InstallerController();
                if (!installer.HasInstalledHybridCLR() ||
                    !string.Equals(installer.PackageVersion, installer.InstalledLibil2cppVersion,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(L(
                        "HybridCLR 本地 il2cpp 自动安装未完成，请检查网络与上方 Git 错误。",
                        "HybridCLR local il2cpp installation did not complete. Check the network connection and Git errors above."));
            }

            HybridCLR.Editor.Settings.HybridCLRSettings hybridSettings = SettingsUtil.HybridCLRSettings;
            if (hybridSettings.useGlobalIl2cpp)
            {
                hybridSettings.useGlobalIl2cpp = false;
                HybridCLR.Editor.Settings.HybridCLRSettings.Save();
            }
            Environment.SetEnvironmentVariable("UNITY_IL2CPP_PATH", SettingsUtil.LocalIl2CppDir);
        }

        private static YooAsset.Editor.BuildResult BuildYooAssetPackage(QHYFrameworkSettings settings,
            ReleaseOptions options)
        {
            // HybridCLR 生成和程序集刷新可能发生在发布预检之后；进入 YooAsset 前再次保证场景已落盘。
            IntegrationProjectPreparer.SaveDirtyScenesOrThrow();
            var parameters = new ScriptableBuildParameters
            {
                BuildOutputRoot = BundleBuilderHelper.GetDefaultBuildOutputRoot(),
                BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = EBuildPipeline.ScriptableBuildPipeline.ToString(),
                BuildBundleType = (int)EBundleType.AssetBundle,
                BuildTarget = options.target,
                PackageName = settings.packageName,
                PackageVersion = options.packageVersion,
                PackageNote = DateTime.UtcNow.ToString("O"),
                EnableSharePackRule = true,
                SingleReferencedPackAlone = true,
                VerifyBuildingResult = true,
                FileNameStyle = EFileNameStyle.HashName,
                BundledCopyOption = options.mode == ReleaseMode.FullPackage
                    ? EBundledCopyOption.ClearAndCopyAll
                    : EBundledCopyOption.None,
                CompressOption = ECompressOption.LZ4,
                UseAssetDependencyDB = true,
                WriteLinkXML = true,
                BuiltinShadersBundleName = GetBuiltinShaderBundleName(settings.packageName)
            };
            DeleteExistingPackageVersion(parameters);
            var pipeline = new ScriptableBuildPipeline();
            YooAsset.Editor.BuildResult result = pipeline.Run(parameters, true);
            if (!result.Success)
                throw new InvalidOperationException(F("YooAsset 构建失败 [{0}]：{1}",
                    "YooAsset build failed [{0}]: {1}", result.FailedTask, result.ErrorInfo));
            return result;
        }

        private static void DeleteExistingPackageVersion(BuildParameters parameters)
        {
            string outputDirectory = Path.GetFullPath(parameters.GetPackageOutputDirectory());
            string packageRoot = Path.GetFullPath(parameters.GetPackageRootDirectory())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!outputDirectory.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(outputDirectory + Path.DirectorySeparatorChar, packageRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(F("拒绝清理包版本目录之外的路径：{0}",
                    "Refusing to clean a path outside the package version directory: {0}", outputDirectory));
            }

            if (Directory.Exists(outputDirectory))
            {
                Debug.Log(F("[QHYFramework] 同版本重建，清理旧构建产物：{0}",
                    "[QHYFramework] Rebuilding the same version; cleaning old output: {0}", outputDirectory));
                Directory.Delete(outputDirectory, true);
                if (Directory.Exists(outputDirectory))
                    throw new IOException(F("同版本旧产物目录删除失败：{0}",
                        "Failed to delete the previous output for the same version: {0}", outputDirectory));
            }
        }

        private static void BuildPlayer(ReleaseOptions options, string clientRoot)
        {
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(options.target);
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(group, options.developmentBuild
                ? Il2CppCompilerConfiguration.Debug
                : Il2CppCompilerConfiguration.Release);
            string location = GetPlayerLocation(clientRoot, options.target);
            BuildOptions buildOptions = options.developmentBuild ? BuildOptions.Development : BuildOptions.None;
            var playerOptions = new BuildPlayerOptions
            {
                scenes = new[] { IntegrationProjectPaths.BootScene },
                target = options.target,
                locationPathName = location,
                options = buildOptions
            };
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(playerOptions);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException(F("Player 构建失败：{0}，错误 {1}",
                    "Player build failed: {0}, errors: {1}", report.summary.result, report.summary.totalErrors));
            RemoveDoNotShipArtifacts(clientRoot);
        }

        private static void RemoveDoNotShipArtifacts(string clientRoot)
        {
            string root = Path.GetFullPath(clientRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Directory.Exists(root))
                return;

            string[] directories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                .Where(path => IsDoNotShipDirectory(Path.GetFileName(path)))
                .OrderByDescending(path => path.Length)
                .ToArray();
            long removedBytes = 0;
            foreach (string directory in directories)
            {
                string full = Path.GetFullPath(directory);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full))
                    continue;
                removedBytes += Directory.GetFiles(full, "*", SearchOption.AllDirectories)
                    .Sum(file => new FileInfo(file).Length);
                Directory.Delete(full, true);
            }

            if (removedBytes > 0)
                Debug.Log(F("[QHYFramework] 已从客户端发布目录移除 DoNotShip 构建产物：{0}",
                    "[QHYFramework] Removed DoNotShip artifacts from the client output: {0}",
                    GamePackageRuntime.FormatBytes(removedBytes)));
        }

        private static bool IsDoNotShipDirectory(string name)
        {
            return name.IndexOf("BackUpThisFolder_ButDontShipItWithYourGame",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("BurstDebugInformation_DoNotShip", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("DoNotShip", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetBuiltinShaderBundleName(string packageName)
        {
            BundlePackRuleResult result = DefaultBundlePackRule.CreateShadersPackRuleResult();
            return result.GetBundleName(packageName, BundleCollectorSettingData.Setting.UniqueBundleName);
        }

        private static string ComputeAotBaseline(BuildTarget target)
        {
            string root = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException(F("缺少裁剪 AOT 目录：{0}。请先执行 HybridCLR Generate/All。",
                    "Stripped AOT directory is missing: {0}. Run HybridCLR Generate/All first.", root));
            using SHA256 sha = SHA256.Create();
            foreach (string file in Directory.GetFiles(root, "*.dll", SearchOption.TopDirectoryOnly)
                         .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(file).ToLowerInvariant());
                sha.TransformBlock(name, 0, name.Length, name, 0);
                byte[] bytes = File.ReadAllBytes(file);
                sha.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return ToHex(sha.Hash);
        }

        private static string ReadPublishedAotBaseline(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning(F("[QHYFramework] 未找到已发布客户端的 AOT 基线：{0}。" +
                                   "HotUpdateOnly 将继续构建，但发布报告无法标记目标客户端基线。",
                    "[QHYFramework] Published client AOT baseline was not found: {0}. " +
                    "HotUpdateOnly will continue, but the release report cannot identify the target client baseline.",
                    path));
                return string.Empty;
            }

            return File.ReadAllText(path).Trim();
        }

        private static void WarnIfLocalAotDiffers(BuildTarget target, string publishedBaseline)
        {
            if (string.IsNullOrWhiteSpace(publishedBaseline))
                return;

            string root = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            if (!Directory.Exists(root))
                return;

            string localBaseline = ComputeAotBaseline(target);
            if (!string.Equals(publishedBaseline, localBaseline, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning(L("[QHYFramework] 当前工程 AOT 输出与已发布客户端不同。" +
                                   "本次 HotUpdateOnly 仍会继续，并保留已发布客户端对应的 AOT 元数据；" +
                                   "当前 AOT 代码改动不会在旧客户端中生效。",
                    "[QHYFramework] The local AOT output differs from the published client. " +
                    "HotUpdateOnly will continue and retain metadata for the published client; " +
                    "local AOT code changes will not take effect in existing clients."));
            }
        }

        private static string[] WriteHashes(string releaseRoot)
        {
            string hashPath = Path.Combine(releaseRoot, "artifacts.sha256");
            var files = Directory.GetFiles(releaseRoot, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(path, hashPath, StringComparison.OrdinalIgnoreCase) &&
                               !path.EndsWith("release-report.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            var lines = new List<string>(files.Length);
            using SHA256 sha = SHA256.Create();
            foreach (string file in files)
            {
                string relative = file.Substring(releaseRoot.Length).TrimStart(Path.DirectorySeparatorChar)
                    .Replace('\\', '/');
                using FileStream stream = File.OpenRead(file);
                lines.Add($"{ToHex(sha.ComputeHash(stream))}  {relative}");
            }
            File.WriteAllLines(hashPath, lines, Encoding.UTF8);
            return files.Select(path => path.Substring(releaseRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/')).ToArray();
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(directory.Replace(source, destination));
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(source, destination), true);
        }

        private static void RecreateDirectory(string path)
        {
            string full = Path.GetFullPath(path);
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!full.StartsWith(project, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(F("拒绝清理项目外目录：{0}",
                    "Refusing to clean a directory outside the project: {0}", full));
            if (Directory.Exists(full)) Directory.Delete(full, true);
            Directory.CreateDirectory(full);
        }

        private static void ValidateOptions(ReleaseOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.channel))
                throw new ArgumentException(L("发布渠道不能为空。", "Channel cannot be empty."));
            if (string.IsNullOrWhiteSpace(options.packageVersion))
                throw new ArgumentException(L("资源版本不能为空。", "Package Version cannot be empty."));
            if (options.packageVersion.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException(L("资源版本不能包含路径分隔符。",
                    "Package Version cannot contain path separators."));
            if (options.target == BuildTarget.NoTarget)
                throw new ArgumentException(L("必须指定目标平台。", "A Build Target must be specified."));
        }

        private static void SynchronizePlayerVersion(ReleaseOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.packageVersion))
                options.packageVersion = PlayerSettings.bundleVersion;
            else
                options.packageVersion = options.packageVersion.Trim();

            if (!string.Equals(PlayerSettings.bundleVersion, options.packageVersion, StringComparison.Ordinal))
                PlayerSettings.bundleVersion = options.packageVersion;
            ClientVersion version = ClientVersion.Parse(options.packageVersion);
            if (options.target == BuildTarget.Android)
                PlayerSettings.Android.bundleVersionCode = version.AndroidVersionCode;
        }

        public static void ValidateClientUpload(ReleaseOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.target != BuildTarget.Android || options.developmentBuild)
                return;
            string baselinePath = Path.Combine(options.outputRoot, "Baselines", "Android.signing.sha256");
            ValidateAndroidSigning(options, baselinePath);
        }

        private static string ValidateAndroidSigning(ReleaseOptions options, string baselinePath)
        {
            if (options.target != BuildTarget.Android || options.developmentBuild)
                return string.Empty;
            if (!PlayerSettings.Android.useCustomKeystore)
            {
                Debug.LogWarning(L(
                    "[QHYFramework] 当前 Android 客户端使用 Unity 默认调试签名，允许继续构建和上传；同一应用后续必须保持使用兼容的签名，否则 Android 将拒绝覆盖安装。",
                    "[QHYFramework] This Android client uses Unity's default debug signing. Build and upload are allowed, but future versions of the same app must keep a compatible signature or Android will reject the update."));
                return string.Empty;
            }
            string keystore = PlayerSettings.Android.keystoreName;
            if (string.IsNullOrWhiteSpace(keystore) || !File.Exists(keystore))
                throw new FileNotFoundException(L("Android Keystore 不存在。",
                    "Android Keystore does not exist."), keystore);
            if (string.IsNullOrWhiteSpace(PlayerSettings.Android.keyaliasName))
                throw new InvalidOperationException(L("Android Keystore 的 Key Alias 不能为空。",
                    "The Android Keystore Key Alias cannot be empty."));
            using var stream = File.OpenRead(keystore);
            using SHA256 sha = SHA256.Create();
            string current = ToHex(sha.ComputeHash(stream)) + ":" + PlayerSettings.Android.keyaliasName;
            if (File.Exists(baselinePath))
            {
                string published = File.ReadAllText(baselinePath).Trim();
                if (!string.Equals(published, current, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(L(
                        "Android Keystore 或签名别名与已发布客户端不一致，无法覆盖安装。",
                        "The Android Keystore or key alias differs from the published client."));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath) ?? options.outputRoot);
            return current;
        }

        private static void ValidateHotUpdateClientVersion(string baselinePath, string version, string platform)
        {
            if (!File.Exists(baselinePath))
                throw new InvalidOperationException(F(
                    "{0} 尚未建立支持整包更新的客户端版本基线，请先执行 Full Package Build。",
                    "No full-client update baseline exists for {0}. Run Full Package Build first.", platform));
            string publishedVersion = File.ReadAllText(baselinePath).Trim();
            if (!string.Equals(publishedVersion, version, StringComparison.Ordinal))
                throw new InvalidOperationException(F(
                    "HotUpdateOnly 不能改变客户端版本。当前完整客户端为 {0}，发布窗口为 {1}；请恢复版本或改用 Full Package Build。",
                    "HotUpdateOnly cannot change the client version. Full client: {0}, release window: {1}. " +
                    "Restore the version or use Full Package Build.", publishedVersion, version));
        }

        private static void EnsureActiveBuildTarget(BuildTarget target)
        {
            if (EditorUserBuildSettings.activeBuildTarget == target)
                return;
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException(L("Unity 正在编译，暂时不能切换构建平台。",
                    "Unity is compiling; the build target cannot be switched yet."));

            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            Debug.Log(F("[QHYFramework] 正在切换构建平台：{0} -> {1}",
                "[QHYFramework] Switching build target: {0} -> {1}",
                EditorUserBuildSettings.activeBuildTarget, target));
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                throw new InvalidOperationException(F("切换构建平台失败：{0}。请确认已安装对应 Unity Build Support 模块。",
                    "Failed to switch build target to {0}. Verify that the corresponding Unity Build Support module is installed.",
                    target));
        }

        public static string GetPlatformName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows: return "Windows32";
                case BuildTarget.StandaloneWindows64: return "Windows64";
                case BuildTarget.StandaloneOSX: return "MacOS";
                case BuildTarget.StandaloneLinux64: return "Linux64";
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iOS";
                case BuildTarget.WebGL: return "WebGL";
                default: return target.ToString();
            }
        }

        public static string GetReleaseRoot(ReleaseOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return Path.GetFullPath(Path.Combine(options.outputRoot, options.channel,
                GetPlatformName(options.target), options.packageVersion));
        }

        public static IntegrationPlatform GetIntegrationPlatform(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: return IntegrationPlatform.Windows;
                case BuildTarget.Android: return IntegrationPlatform.Android;
                case BuildTarget.iOS: return IntegrationPlatform.IOS;
                case BuildTarget.StandaloneOSX: return IntegrationPlatform.MacOS;
                case BuildTarget.StandaloneLinux64: return IntegrationPlatform.Linux;
                case BuildTarget.WebGL: return IntegrationPlatform.WebGL;
                default: return IntegrationPlatform.Unknown;
            }
        }

        private static string GetPlayerLocation(string root, BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: return Path.Combine(root, PlayerSettings.productName + ".exe");
                case BuildTarget.Android: return Path.Combine(root, PlayerSettings.productName + ".apk");
                case BuildTarget.StandaloneOSX: return Path.Combine(root, PlayerSettings.productName + ".app");
                case BuildTarget.iOS:
                case BuildTarget.WebGL:
                case BuildTarget.StandaloneLinux64: return root;
                default: return root;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            return bytes == null ? string.Empty : BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string L(string chinese, string english)
        {
            return EditorLocalization.Text(chinese, english);
        }

        private static string F(string chinese, string english, params object[] args)
        {
            return EditorLocalization.Format(chinese, english, args);
        }
    }
}
