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
            SessionState.SetBool(HybridClrBuildGuard.ReleaseSessionKey, true);
            try
            {
                return RunCore(options);
            }
            finally
            {
                SessionState.EraseBool(HybridClrBuildGuard.ReleaseSessionKey);
            }
        }

        private static string RunCore(ReleaseOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.target == BuildTarget.NoTarget)
                throw new ArgumentException(L("必须指定目标平台。", "A Build Target must be specified."));
            VersionControlIgnoreManager.EnsureForCurrentProjectOrThrow();
            string platform = GetPlatformName(options.target);
            QHYFrameworkSettings settings = IntegrationProjectPreparer.LoadSettings();
            options.gameDirectory = settings.GetGameDirectoryOrThrow();
            if (string.IsNullOrWhiteSpace(options.clientVersion))
                options.clientVersion = PlayerSettings.bundleVersion;
            ResourceReleaseBaseline resourceBaseline = ReleaseBaselineStore.Load(options);
            if (resourceBaseline != null && !string.Equals(resourceBaseline.gameDirectory,
                    options.gameDirectory, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"发布基线的游戏资源目录为 '{resourceBaseline.gameDirectory}'，当前配置为 " +
                    $"'{options.gameDirectory}'。schema v5 不迁移目录；请清空 ReleaseState、Releases、" +
                    "QHYBuilds 和该游戏的服务器资源后重新 FullPackage。");
            if (!string.IsNullOrWhiteSpace(resourceBaseline?.pendingBuildRoot))
                throw new InvalidOperationException($"存在尚未完成上传登记的发布（{resourceBaseline.pendingResourceVersion}，{resourceBaseline.stage}）。请先完成或重试该版本，不要创建新候选版本。");
            string publishedClientVersion = resourceBaseline?.hasPublishedBaseline == true
                ? resourceBaseline.clientVersion : string.Empty;
            if (options.mode == ReleaseMode.HotUpdateOnly)
            {
                if (string.IsNullOrWhiteSpace(publishedClientVersion))
                    throw new InvalidOperationException("尚未建立 schema v5 Published FullPackage 基线，HotUpdateOnly 已禁用。");
                options.clientVersion = publishedClientVersion;
            }
            ResourceVersionResolver.Resolve(options, publishedClientVersion,
                resourceBaseline?.resourceVersion, resourceBaseline?.highestResourceVersion);
            string stateRoot = DistributionReleaseLayout.StateRoot(options);
            string baselinePath = Path.Combine(stateRoot, "aot-baseline.sha256");
            if (options.mode == ReleaseMode.FullPackage)
                SynchronizePlayerVersion(options);
            ValidateOptions(options);
            settings.CreateRuntimeConfig(GetIntegrationPlatform(options.target), options.clientVersion);
            DistributionReleaseLayout.ValidateCleanOrV5(options);
            IntegrationProjectPreparer.SaveDirtyScenesOrThrow();
            EnsureActiveBuildTarget(options.target);
            BootUIPrefabGenerator.EnsureExists();
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
                options.mode == ReleaseMode.FullPackage, options.clientVersion);

            // CompileDll 和 AssetDatabase.Refresh 后必须重新加载 ScriptableObject。
            settings = IntegrationProjectPreparer.LoadSettings();
            IntegrationProjectPreparer.ConfigureCollectors(settings);
            EngineStrippingAnalysis engineStrippingAnalysis = options.mode == ReleaseMode.FullPackage
                ? EngineStrippingLinkerAutomation.Synchronize(settings)
                : null;
            ReleaseValidation.ThrowIfInvalid(settings, true, options.target, effectiveAotMetadata,
                engineStrippingAnalysis);

            string baseline = string.Empty;
            string androidSigningBaselinePath = Path.Combine(stateRoot, "android-signing.sha256");
            string androidSigningBaseline = options.mode == ReleaseMode.FullPackage
                ? ValidateAndroidSigning(options, androidSigningBaselinePath)
                : string.Empty;
            if (options.mode == ReleaseMode.HotUpdateOnly)
            {
                // HotUpdateOnly 面向的是已经发布的客户端。即使当前工程的 AOT 输出发生变化，
                // 也不能用它否定旧客户端，更不能把新 AOT 元数据混入旧客户端的热更包。
                // 本地 AOT 差异只用于提示；冻结快照 SHA 和最终 AOT Bundle 差异会在后续阶段阻断发布。
                baseline = ReadPublishedAotBaseline(baselinePath);
                WarnIfLocalAotDiffers(options.target, baseline);
            }

            YooAsset.Editor.BuildResult yooResult = BuildYooAssetPackage(settings, options);
            string releaseRoot = DistributionReleaseLayout.BuildRoot(options);
            if (Directory.Exists(releaseRoot))
                throw new InvalidOperationException("同一 ClientVersion/ResourceVersion 的 Builds 产物已存在，禁止覆盖：" + releaseRoot);
            Directory.CreateDirectory(releaseRoot);
            BundleDeltaAnalysis delta = BundleDeltaAnalyzer.AnalyzeV3(yooResult.OutputPackageDirectory,
                settings.packageName, options, resourceBaseline, null);
            if (!settings.ignoreTypeTreeChangesForIncrementalBuild)
            {
                foreach (BundleDeltaRecord record in delta.Changed)
                    record.suspectedTypeTreeChange = false;
            }
            else
            {
                Debug.LogWarning(L(
                    "[QHYFramework] 已启用 TypeTree 高级诊断。YooAsset 3.0.4 SBP 不支持直接忽略 TypeTree 变化；报告中的标记仅为启发式判断。",
                    "[QHYFramework] Advanced TypeTree diagnostics are enabled. YooAsset 3.0.4 SBP cannot ignore TypeTree changes directly; report flags are heuristic."));
            }
            if (options.mode == ReleaseMode.HotUpdateOnly &&
                !ReleaseContentChangeDetector.HasChanges(delta))
            {
                DiscardNoChangeOutput(yooResult.OutputPackageDirectory, releaseRoot, "QHYBuilds");
                throw new NoReleaseContentChangesException(L(
                    "未检测到任何热更 DLL 或 YooAsset 资源变化，本次为空更新，已取消构建且不会生成可上传版本。",
                    "No hot-update DLL or YooAsset content changes were detected. This empty update was canceled and no uploadable release was produced."));
            }
            delta.UploadPlan = ReleaseSnapshotMaterializer.MaterializeV3(
                yooResult.OutputPackageDirectory, releaseRoot, settings, options, resourceBaseline);
            BundleSizeAnalyzer.Analyze(delta.CurrentReport, settings);
            bool aotMetadataBundleChanged = delta.Changed.Concat(delta.Added).Concat(delta.Removed).Any(record =>
                record.mainAssets.Any(path => path.IndexOf("/Generated/AOTMetadata/",
                    StringComparison.OrdinalIgnoreCase) >= 0));
            bool aotMetadataPayloadMatched = options.mode != ReleaseMode.HotUpdateOnly;
            bool aotMetadataForcePublished = false;
            string aotMetadataMismatchReason = string.Empty;
            if (options.mode == ReleaseMode.HotUpdateOnly)
            {
                string snapshotRoot = AotMetadataSnapshotStore.GetRoot(options.target,
                    options.clientVersion);
                aotMetadataPayloadMatched = AotMetadataSnapshotStore.MatchesSnapshot(options.target,
                    Path.GetFullPath(IntegrationProjectPaths.GeneratedAotMetadata), snapshotRoot,
                    out string mismatchReason);
                if (aotMetadataPayloadMatched)
                {
                    string[] currentAotAssets = delta.Added.Concat(delta.Changed)
                        .Concat(delta.Unchanged)
                        .SelectMany(record => record.mainAssets ?? Array.Empty<string>())
                        .Where(path => path.IndexOf("/Generated/AOTMetadata/",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(path => Path.GetFileName(path.Replace('/', Path.DirectorySeparatorChar)))
                        .ToArray();
                    string[] missingCollectedAssets = effectiveAotMetadata
                        .Select(name => name + ".bytes")
                        .Where(fileName => !currentAotAssets.Contains(fileName,
                            StringComparer.OrdinalIgnoreCase))
                        .ToArray();
                    if (missingCollectedAssets.Length > 0)
                    {
                        aotMetadataPayloadMatched = false;
                        mismatchReason = "Restored AOT metadata is not collected by the current YooAsset manifest: " +
                                         string.Join(", ", missingCollectedAssets);
                    }
                }
                if (!aotMetadataPayloadMatched)
                {
                    aotMetadataMismatchReason = mismatchReason;
                    aotMetadataForcePublished = options.confirmForceAotMetadataPublish?.Invoke(
                        mismatchReason) == true;
                    if (!aotMetadataForcePublished)
                        throw new AotMetadataPublishBlockedException(F(
                            "HotUpdateOnly 的 AOT 元数据负载与已发布客户端快照不一致，且未授权强制发布：{0}。请恢复客户端快照或建立新的 FullPackage 基线。",
                            "The HotUpdateOnly AOT metadata payload differs from the published client snapshot and force publication was not authorized: {0}. Restore the client snapshot or create a new FullPackage baseline.",
                            mismatchReason));

                    Debug.LogError(F(
                        "[QHYFramework] 用户已强制发布不匹配的 AOT 元数据。旧客户端不包含当前工程新增或变化的 AOT 原生代码，运行时可能出现 MissingMethodException、元数据加载失败或崩溃。差异：{0}",
                        "[QHYFramework] The user force-published mismatched AOT metadata. Existing clients do not contain newly added or changed native AOT code and may encounter MissingMethodException, metadata loading failures, or crashes. Difference: {0}",
                        mismatchReason));
                }

                if (aotMetadataBundleChanged)
                    Debug.LogWarning(L(
                        "[QHYFramework] AOT Metadata Bundle 发生变化，但其中所有元数据文件的名称、长度和 SHA-256 均与已发布客户端快照一致，因此允许继续发布。Bundle 变化可能来自打包布局或依赖变化；当前工程的 AOT 代码不会在旧客户端中生效。",
                        "[QHYFramework] The AOT Metadata bundle changed, but every metadata file name, length, and SHA-256 still matches the published client snapshot, so publication is allowed. The bundle may have changed because of packing or dependency changes; local AOT code will not take effect in existing clients."));
            }
            File.WriteAllText(Path.Combine(releaseRoot, "publish-plan.json"),
                JsonUtility.ToJson(delta.UploadPlan, true), new UTF8Encoding(false));

            if (options.mode == ReleaseMode.FullPackage)
            {
                DistributionRuntimeConfigGenerator.Synchronize(settings, options);
                string clientRoot = Path.Combine(releaseRoot, "Player");
                RecreateDirectory(clientRoot);
                IReadOnlyList<string> strippingErrors =
                    EngineStrippingLinkerAutomation.ValidateProtection(engineStrippingAnalysis,
                        PlayerSettings.stripEngineCode);
                if (strippingErrors.Count > 0)
                    throw new InvalidOperationException(L("Player 构建前 Engine Stripping 校验失败：\n- ",
                        "Pre-Player Engine Stripping validation failed:\n- ") +
                                                        string.Join("\n- ", strippingErrors));
                BuildPlayer(options, clientRoot);

                if (File.Exists(IntegrationProjectPaths.GeneratedQhyLinkXml))
                    File.Copy(IntegrationProjectPaths.GeneratedQhyLinkXml,
                        Path.Combine(releaseRoot, "Reports", "qhy-link.xml"), false);

                // Player 构建会重新生成 AssembliesPostIl2CppStrip，必须在构建完成后记录真实客户端基线。
                baseline = ComputeAotBaseline(options.target);
                Directory.CreateDirectory(Path.GetDirectoryName(baselinePath) ?? options.outputRoot);
                File.WriteAllText(baselinePath, baseline, Encoding.UTF8);
                ClientArtifactResult clientArtifact = ClientArtifactBuilder.Build(releaseRoot, clientRoot,
                    settings, options);
                if (clientArtifact != null)
                {
                    delta.UploadPlan.added = delta.UploadPlan.added.Concat(clientArtifact.Artifacts).ToArray();
                    delta.UploadPlan.uploadBytes += clientArtifact.Artifacts.Sum(x => x.length);
                    File.WriteAllText(Path.Combine(releaseRoot, "publish-plan.json"),
                        JsonUtility.ToJson(delta.UploadPlan, true), new UTF8Encoding(false));
                }
                if (!string.IsNullOrWhiteSpace(androidSigningBaseline))
                    File.WriteAllText(androidSigningBaselinePath, androidSigningBaseline, Encoding.UTF8);
            }

            IncrementalUploadMaterializer.Synchronize(options, delta.UploadPlan);
            string[] artifacts = WriteHashes(releaseRoot);
            var report = new ReleaseReportData
            {
                gameDirectory = options.gameDirectory,
                version = options.clientVersion,
                resourceVersion = options.resourceVersion,
                previousResourceVersion = resourceBaseline?.resourceVersion ?? string.Empty,
                platform = platform,
                mode = options.mode.ToString(),
                unityVersion = Application.unityVersion,
                clientVersion = options.clientVersion,
                cdnRoot = delta.UploadPlan.cdnRoot,
                originRoot = delta.UploadPlan.originRoot,
                clientManifestUrl = DistributionPathResolver.CombineUrl(delta.UploadPlan.originRoot,
                    "current/client.json"),
                timestampUtc = DateTime.UtcNow.ToString("O"),
                aotBaselineSha256 = baseline,
                aotMetadataAssemblies = effectiveAotMetadata,
                artifacts = artifacts,
                snapshotBytes = delta.UploadPlan.snapshotBytes,
                uploadBytes = delta.UploadPlan.uploadBytes,
                estimatedClientDownloadBytes = delta.UploadPlan.estimatedClientDownloadBytes,
                bundleCount = delta.CurrentReport.BundleInfos.Count,
                addedBundleCount = delta.Added.Length,
                changedBundleCount = delta.Changed.Length,
                unchangedBundleCount = delta.Unchanged.Length,
                removedBundleCount = delta.Removed.Length,
                hotUpdateDllChanged = delta.Added.Concat(delta.Changed).Any(record => record.mainAssets.Any(path =>
                    path.IndexOf("/Generated/HotUpdate/", StringComparison.OrdinalIgnoreCase) >= 0)),
                aotMetadataChanged = options.mode == ReleaseMode.HotUpdateOnly &&
                                     !aotMetadataPayloadMatched,
                aotMetadataBundleChanged = aotMetadataBundleChanged,
                aotMetadataPayloadMatched = aotMetadataPayloadMatched,
                aotMetadataForcePublished = aotMetadataForcePublished,
                aotMetadataMismatchReason = aotMetadataMismatchReason,
                aotClientBaselineMatched = options.mode == ReleaseMode.FullPackage ||
                                           (aotMetadataPayloadMatched &&
                                            !string.IsNullOrWhiteSpace(baseline)),
                stripEngineCode = PlayerSettings.stripEngineCode,
                engineStrippingLinkPath = options.mode == ReleaseMode.FullPackage
                    ? IntegrationProjectPaths.GeneratedQhyLinkXml
                    : string.Empty,
                engineStrippingProtectedAssemblies = engineStrippingAnalysis?.RequiredAssemblies ??
                                                      Array.Empty<string>(),
                externalAnimationAssetCount = engineStrippingAnalysis?.AnimationAssetPaths.Length ?? 0,
                externalAnimationAssets = engineStrippingAnalysis?.AnimationAssetPaths ??
                                          Array.Empty<string>(),
                addedBundles = delta.Added,
                changedBundles = delta.Changed,
                unchangedBundles = delta.Unchanged,
                removedBundles = delta.Removed
            };
            File.WriteAllText(Path.Combine(releaseRoot, "release-report.json"),
                JsonUtility.ToJson(report, true), Encoding.UTF8);
            ReleaseBaselineStore.SaveBuilt(options, releaseRoot, delta.UploadPlan);
            AssetDatabase.Refresh();
            Debug.Log(F("[QHYFramework] 发布完成：{0}\n完整资源快照：{1}；实际上传计划：{2}；预计客户端下载：{3}",
                "[QHYFramework] Release completed: {0}\nSnapshot: {1}; planned upload: {2}; estimated client download: {3}",
                releaseRoot, GamePackageRuntime.FormatBytes(delta.UploadPlan.snapshotBytes),
                GamePackageRuntime.FormatBytes(delta.UploadPlan.uploadBytes),
                GamePackageRuntime.FormatBytes(delta.UploadPlan.estimatedClientDownloadBytes)));
            return releaseRoot;
        }

        private static void DiscardNoChangeOutput(string yooOutput, string releaseRoot, string outputRoot)
        {
            TryDeleteGeneratedDirectory(yooOutput, BundleBuilderHelper.GetDefaultBuildOutputRoot());
            TryDeleteGeneratedDirectory(releaseRoot, outputRoot);
        }

        private static void TryDeleteGeneratedDirectory(string path, string allowedRoot)
        {
            try
            {
                string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                string fullRoot = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                string prefix = fullRoot + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Refused to delete a generated directory outside its root: " +
                                                        fullPath);
                if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QHYFramework] Failed to discard empty release output: " +
                                 exception.GetBaseException().Message);
            }
        }

        private static void EnsureHybridClrInstalled()
        {
            var installer = new InstallerController();
            bool requiresInstall = !installer.HasInstalledHybridCLR() ||
                                   !HybridClrBuildGuard.IsLocalIl2CppLayoutValid(
                                       SettingsUtil.LocalIl2CppDir, out _) ||
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
                    !HybridClrBuildGuard.IsLocalIl2CppLayoutValid(
                        SettingsUtil.LocalIl2CppDir, out _) ||
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
            HybridClrBuildGuard.EnsureInstalledAndVersionMatches();
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
                PackageVersion = options.resourceVersion,
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
            if (options.automaticResourceVersion)
            {
                string originalVersion = parameters.PackageVersion;
                while (Directory.Exists(Path.GetFullPath(parameters.GetPackageOutputDirectory())))
                {
                    parameters.PackageVersion = ResourceVersionResolver.Next(options.clientVersion,
                        parameters.PackageVersion);
                    options.resourceVersion = parameters.PackageVersion;
                }
                if (!string.Equals(originalVersion, parameters.PackageVersion, StringComparison.Ordinal))
                    Debug.Log(F(
                        "[QHYFramework] 自动资源版本 {0} 已存在，已顺延为 {1}。",
                        "[QHYFramework] Automatic resource version {0} already exists; advanced to {1}.",
                        originalVersion, parameters.PackageVersion));
            }
            ThrowIfResourceVersionExists(parameters);
            var pipeline = new ScriptableBuildPipeline();
            YooAsset.Editor.BuildResult result = pipeline.Run(parameters, true);
            if (!result.Success)
                throw new InvalidOperationException(F("YooAsset 构建失败 [{0}]：{1}",
                    "YooAsset build failed [{0}]: {1}", result.FailedTask, result.ErrorInfo));
            return result;
        }

        private static void ThrowIfResourceVersionExists(BuildParameters parameters)
        {
            string outputDirectory = Path.GetFullPath(parameters.GetPackageOutputDirectory());
            if (Directory.Exists(outputDirectory))
                throw new InvalidOperationException(F(
                    "资源版本 {0} 已存在，禁止同版本覆盖。请递增 ResourceVersion 后重试：{1}",
                    "Resource version {0} already exists and is immutable. Increment ResourceVersion and retry: {1}",
                    parameters.PackageVersion, outputDirectory));
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
            // GenerateAll、CompileDll、AssetDatabase.Refresh 和 YooAsset 构建都可能触发
            // InitializeOnLoad。必须在最终 BuildPlayer 紧邻位置重新锁定本地工具链。
            HybridClrBuildGuard.ReassertLocalToolchain();
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(playerOptions);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException(F("Player 构建失败：{0}，错误 {1}",
                    "Player build failed: {0}, errors: {1}", report.summary.result, report.summary.totalErrors));
            HybridClrBuildGuard.ValidateNativePlayer(options.target, location);
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
            string uploadRoot = Path.GetFullPath(Path.Combine(releaseRoot, "Upload")) +
                                Path.DirectorySeparatorChar;
            var files = Directory.GetFiles(releaseRoot, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(path, hashPath, StringComparison.OrdinalIgnoreCase) &&
                               !path.EndsWith("release-report.json", StringComparison.OrdinalIgnoreCase) &&
                               !Path.GetFullPath(path).StartsWith(uploadRoot, StringComparison.OrdinalIgnoreCase))
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
            if (string.IsNullOrWhiteSpace(options.clientVersion))
                throw new ArgumentException(L("客户端版本不能为空。", "Client Version cannot be empty."));
            if (string.IsNullOrWhiteSpace(options.resourceVersion))
                throw new ArgumentException(L("资源版本不能为空。", "Resource Version cannot be empty."));
            if (options.clientVersion.IndexOfAny(new[] { '/', '\\' }) >= 0 ||
                options.resourceVersion.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException(L("资源版本不能包含路径分隔符。",
                    "Package Version cannot contain path separators."));
            if (options.target == BuildTarget.NoTarget)
                throw new ArgumentException(L("必须指定目标平台。", "A Build Target must be specified."));
        }

        private static void SynchronizePlayerVersion(ReleaseOptions options)
        {
            options.clientVersion = options.clientVersion.Trim();

            if (!string.Equals(PlayerSettings.bundleVersion, options.clientVersion, StringComparison.Ordinal))
                PlayerSettings.bundleVersion = options.clientVersion;
            ClientVersion version = ClientVersion.Parse(options.clientVersion);
            if (options.target == BuildTarget.Android)
                PlayerSettings.Android.bundleVersionCode = version.AndroidVersionCode;
        }

        public static void ValidateClientUpload(ReleaseOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.target != BuildTarget.Android || options.developmentBuild)
                return;
            string baselinePath = Path.Combine(DistributionReleaseLayout.StateRoot(options),
                "android-signing.sha256");
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
            return DistributionReleaseLayout.BuildRoot(options);
        }

        internal static string GetClientVersionRoot(ReleaseOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return Path.GetFullPath(Path.Combine(
                string.IsNullOrWhiteSpace(options.buildRootOverride) ? "QHYBuilds" : options.buildRootOverride,
                DistributionReleaseLayout.Platform(options), options.clientVersion));
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
