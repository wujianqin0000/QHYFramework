using System;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace GameIntegration.Editor
{
    internal static class ReleaseBuildTargetSwitcher
    {
        internal static BuildTargetGroup GetTargetGroupOrThrow(BuildTarget target)
        {
            if (target == BuildTarget.NoTarget)
                throw new InvalidOperationException(EditorLocalization.Text(
                    "不能切换到 NoTarget，请选择有效的构建平台。",
                    "Cannot switch to NoTarget. Select a valid build platform."));
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            if (group == BuildTargetGroup.Unknown)
                throw new InvalidOperationException(EditorLocalization.Format(
                    "目标平台 {0} 没有可用的 BuildTargetGroup。",
                    "Build target {0} has no valid BuildTargetGroup.", target));
            return group;
        }

        internal static void SwitchOrThrow(BuildTarget target)
        {
            if (EditorUserBuildSettings.activeBuildTarget == target) return;
            if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
                throw new InvalidOperationException(EditorLocalization.Text(
                    "Unity 正在构建、编译或刷新资源，暂时不能切换目标平台。",
                    "Unity is building, compiling, or refreshing assets; the build target cannot be switched yet."));

            BuildTargetGroup group = GetTargetGroupOrThrow(target);
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
                throw new InvalidOperationException(EditorLocalization.Format(
                    "未安装目标平台 {0} 的 Unity Build Support 模块。请先通过 Unity Hub 安装。",
                    "Unity Build Support for {0} is not installed. Install it from Unity Hub first.",
                    target));
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target) ||
                EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException(EditorLocalization.Format(
                    "Unity 未能切换到目标平台 {0}。请检查 Console 和 Build Support 安装。",
                    "Unity could not switch to {0}. Check the Console and Build Support installation.",
                    target));
        }
    }

    public sealed class ReleaseWindow : EditorWindow
    {
        private readonly ReleaseOptions _options = new ReleaseOptions();
        private readonly FtpUploadOptions _ftp = new FtpUploadOptions();
        private Vector2 _scroll;
        private bool _uploading;
        private CancellationTokenSource _uploadCancellation;
        private BuildTarget _lastTarget;
        private string _lastObservedPlayerVersion;
        private bool _automaticVersioning = true;
        private bool _rememberFtpPassword = true;
        private bool _automaticVersionBuildInProgress;
        private bool _manualUpload;
        private bool _switchingTarget;

        private const string FtpPrefsPrefix = "GameIntegration.ReleaseWindow.FTP.";
        private const string ReleasePrefsPrefix = "GameIntegration.ReleaseWindow.Release.";
        private const string InitialPackageVersion = "v1.0.0";

        internal static void Open()
        {
            GetWindow<ReleaseWindow>("QHY Framework Release");
        }

        private void OnEnable()
        {
            EditorLocalization.LanguageChanged += OnLanguageChanged;
            UpdateTitle();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.NoTarget)
                _options.target = EditorUserBuildSettings.activeBuildTarget;
            _lastTarget = _options.target;
            LoadReleaseSettings(_lastTarget);
            LoadFtpSettings(_lastTarget);
            _lastObservedPlayerVersion = PlayerSettings.bundleVersion;
        }

        private void OnDisable()
        {
            EditorLocalization.LanguageChanged -= OnLanguageChanged;
            SaveFtpSettings(_lastTarget);
            if (!_automaticVersionBuildInProgress)
                SaveReleaseSettings(_lastTarget);
            _uploadCancellation?.Cancel();
            PublishingCredentialStore.Clear(GetCredentialScope(_lastTarget));
            _ftp.Password = string.Empty;
            EditorUtility.ClearProgressBar();
        }

        private void OnGUI()
        {
            SynchronizeVersionFromPlayerSettings();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("QHY Framework", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("QFramework + HybridCLR + YooAsset", EditorStyles.miniLabel);
            int selectedLanguage = EditorGUILayout.Popup(L("编辑器语言", "Editor Language"),
                (int)EditorLocalization.Language, new[] { "中文", "English" });
            if (selectedLanguage != (int)EditorLocalization.Language)
                EditorLocalization.Language = (EditorToolLanguage)selectedLanguage;

            QHYFrameworkSettings settings = null;
            try { settings = IntegrationProjectPreparer.LoadSettings(); } catch { }
            if (settings)
                EditorGUILayout.HelpBox(L(
                    "Android 与 Windows BaseURL 请在 QHYFrameworkSettings 的平台资源分组中配置。",
                    "Configure Android and Windows BaseURLs in the Platform Resources section of QHYFrameworkSettings."),
                    MessageType.None);
            _automaticVersioning = EditorGUILayout.Toggle(
                L("版本自动化", "Automatic Versioning"), _automaticVersioning);
            _options.automaticResourceVersion = _automaticVersioning;
            if (_automaticVersioning)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField(L("当前平台版本（自动基线）",
                        "Current Platform Version (automatic baseline)"), _options.clientVersion);
                string suggestedResourceVersion = ResourceVersionResolver.SuggestNext(_options);
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField(L("热更新下一资源版本（自动）",
                            "Next Hot-Update Resource Version (automatic)"),
                        string.IsNullOrWhiteSpace(suggestedResourceVersion)
                            ? L("等待有效客户端版本", "Waiting for a valid client version")
                            : suggestedResourceVersion);
                EditorGUILayout.HelpBox(L(
                    "构建时会综合已发布基线、本地 Releases 产物和 YooAsset 遗留目录选择下一个可用的 -rNNNN，不覆盖旧版本。",
                    "At build time QHY selects the next available -rNNNN from the published baseline, local Releases, and leftover YooAsset output without overwriting an old version."),
                    MessageType.Info);
            }
            else
            {
                string editedVersion = EditorGUILayout.TextField(L("客户端版本（手动）",
                    "Client Version (manual)"), _options.clientVersion);
                if (!string.Equals(editedVersion, _options.clientVersion, StringComparison.Ordinal))
                    SetPackageVersion(editedVersion);
                _options.resourceVersion = EditorGUILayout.TextField(L("资源版本（例如 v1.0.0-r0002）",
                    "Resource Version (for example v1.0.0-r0002)"), _options.resourceVersion);
            }
            EditorGUILayout.HelpBox(L(
                "HotUpdateOnly 会锁定已发布客户端版本，不修改 PlayerSettings；只允许递增资源版本。",
                "HotUpdateOnly locks the published client version, does not modify PlayerSettings, and only advances ResourceVersion."),
                MessageType.Info);
            BuildTarget selectedTarget;
            using (new EditorGUI.DisabledScope(_uploading || _switchingTarget ||
                                                BuildPipeline.isBuildingPlayer ||
                                                EditorApplication.isCompiling ||
                                                EditorApplication.isUpdating))
                selectedTarget = (BuildTarget)EditorGUILayout.EnumPopup(
                    L("目标平台", "Build Target"), _options.target);
            if (selectedTarget != _options.target)
                SwitchReleaseTarget(selectedTarget);
            EditorGUILayout.HelpBox(L(
                "修改目标平台会立即切换 Unity Active Build Target；QHYFrameworkSettings Inspector 会同步显示该平台的 BaseURL。切换期间 Unity 可能重新导入资源或编译脚本。",
                "Changing Build Target immediately switches Unity's Active Build Target. The QHYFrameworkSettings Inspector follows it and shows that platform's BaseURL. Unity may reimport assets or compile scripts during the switch."),
                MessageType.None);
            _options.developmentBuild = EditorGUILayout.Toggle(L("开发构建", "Development Build"),
                _options.developmentBuild);
            _options.outputRoot = "Releases";
            using (new EditorGUI.DisabledScope(true))
            {
                IntegrationPlatform platform = ReleasePipeline.GetIntegrationPlatform(_options.target);
                DistributionRuntimeConfig config = null;
                string configError = string.Empty;
                try
                {
                    if (settings)
                    {
                        _options.gameDirectory = settings.GetGameDirectoryOrThrow();
                        config = settings.CreateRuntimeConfig(platform, _options.clientVersion);
                    }
                }
                catch (Exception exception) { configError = exception.GetBaseException().Message; }
                string missing = string.IsNullOrWhiteSpace(configError)
                    ? L("<缺少配置>", "<missing settings>") : configError;
                string platformBase = missing;
                try
                {
                    if (settings) platformBase = QHYFrameworkSettings.NormalizeResourceBaseUrl(
                        settings.GetPlatformResourceProfile(platform).baseUrl);
                }
                catch (Exception exception) { platformBase = exception.GetBaseException().Message; }
                EditorGUILayout.TextField(L("资源 BaseURL", "Resource BaseURL"), platformBase);
                EditorGUILayout.TextField(L("游戏资源目录", "Game Resource Directory"),
                    settings ? settings.gameDirectory : missing);
                EditorGUILayout.TextField(L("CDN Root", "CDN Root"), config?.cdnRoot ?? missing);
                EditorGUILayout.TextField(L("Origin Root", "Origin Root"), config?.originRoot ?? missing);
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField(L("1. 构建产物", "1. Build Artifacts"),
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(L(
                "完整客户端构建用于发布新客户端；仅热更新构建用于更新代码和资源。",
                "Full Package Build publishes a new client; HotUpdateOnly Build updates code and content."),
                MessageType.None);
            if (_automaticVersioning)
                DrawAutomatedFullPackageButtons();
            else if (GUILayout.Button(L("完整客户端构建", "Full Package Build"), GUILayout.Height(42)))
                Build(ReleaseMode.FullPackage);
            if (GUILayout.Button(L("仅热更新构建", "HotUpdateOnly Build"), GUILayout.Height(42)))
                Build(ReleaseMode.HotUpdateOnly);

            EditorGUILayout.Space(18);
            EditorGUILayout.LabelField(L("2. 发布产物", "2. Release Files"),
                EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(L("完整平台目录", "Complete Platform Directory"),
                    DistributionReleaseLayout.PlatformRoot(_options));
                EditorGUILayout.TextField(L("CDN 文件目录", "CDN Files"),
                    Path.Combine(DistributionReleaseLayout.PlatformRoot(_options), "cdn"));
                EditorGUILayout.TextField(L("Origin 文件目录", "Origin Files"),
                    Path.Combine(DistributionReleaseLayout.PlatformRoot(_options), "origin"));
                EditorGUILayout.TextField(L("本次增量文件", "Incremental Files"),
                    DistributionReleaseLayout.UploadFilesRoot(_options));
                EditorGUILayout.TextField(L("最后发布指针", "Publish Last"),
                    DistributionReleaseLayout.UploadPublishRoot(_options));
            }

            EditorGUILayout.Space(18);
            EditorGUILayout.LabelField(L("3. 发布", "3. Publish"), EditorStyles.boldLabel);
            GetCurrentPublicationState(out bool resourcePublished, out bool clientPublished);
            _manualUpload = GUILayout.Toolbar(_manualUpload ? 1 : 0,
                new[] { L("FTP 自动上传", "FTP Upload"), L("手动上传", "Manual Upload") }) == 1;
            if (!_manualUpload)
            {
            _ftp.Host = EditorGUILayout.TextField(L("FTP 主机", "FTP Host"), _ftp.Host);
            _ftp.Port = EditorGUILayout.IntField(L("FTP 端口", "FTP Port"), _ftp.Port);
            _ftp.UserName = EditorGUILayout.TextField(L("FTP 用户名", "FTP User Name"), _ftp.UserName);
            _ftp.Password = EditorGUILayout.PasswordField(L("FTP 密码", "FTP Password"), _ftp.Password);
            _rememberFtpPassword = EditorGUILayout.Toggle(
                L("安全记住 FTP 密码", "Remember FTP Password Securely"), _rememberFtpPassword);
            EditorGUILayout.HelpBox(L(
                "密码保存在 Windows 凭据管理器，不会写入工程、Settings Asset 或普通 EditorPrefs。取消勾选会删除已保存凭据。",
                "The password is stored in Windows Credential Manager, never in project files, Settings assets, or regular EditorPrefs. Clearing this option removes the saved credential."),
                MessageType.None);
            EditorGUILayout.HelpBox(L(
                "FTP 账号登录根目录就是服务器资源总根；QHY 会在其下上传 游戏资源目录/平台。不同游戏使用不同游戏资源目录。",
                "The FTP login directory is the server resource root. QHY uploads gameDirectory/platform beneath it, so each game stays isolated."),
                MessageType.None);
            _ftp.Concurrency = EditorGUILayout.IntSlider(L("并发上传", "Upload Concurrency"),
                _ftp.Concurrency, 1, 8);
            _ftp.UsePassive = EditorGUILayout.Toggle(L("被动模式", "Passive Mode"), _ftp.UsePassive);
            _ftp.EnableSsl = EditorGUILayout.Toggle(L("启用 SSL（FTPS）", "Enable SSL (FTPS)"),
                _ftp.EnableSsl);

            ResourceReleaseBaseline publication = null;
            try { publication = ReleaseBaselineStore.Load(_options); } catch { }
            EditorGUILayout.LabelField(L("发布阶段", "Publication Stage"),
                publication?.stage ?? L("未初始化", "Not initialized"));
            using (new EditorGUI.DisabledScope(_uploading || resourcePublished))
            {
                if (GUILayout.Button(L("上传更新", "Upload Update"), GUILayout.Height(38)))
                    Upload(_options.mode == ReleaseMode.FullPackage);
            }
            }
            else
            {
                EditorGUILayout.HelpBox(L(
                    "先把 1-Files 内容上传到服务器资源根，再上传 2-Publish，最后执行检查。",
                    "Upload 1-Files to the resource root first, then 2-Publish, then verify."),
                    MessageType.Info);
                bool hasFiles = DistributionReleaseLayout.TryGetExistingUploadRoot(
                    _options, false, out _);
                bool hasPublish = DistributionReleaseLayout.TryGetExistingUploadRoot(
                    _options, true, out _);
                using (new EditorGUI.DisabledScope(!hasFiles))
                    if (GUILayout.Button(L("打开 1-Files", "Open 1-Files")))
                        RevealUploadDirectory(false);
                using (new EditorGUI.DisabledScope(!hasPublish))
                    if (GUILayout.Button(L("打开 2-Publish", "Open 2-Publish")))
                        RevealUploadDirectory(true);
                if (!hasFiles || !hasPublish)
                    EditorGUILayout.HelpBox(L(
                        "当前构建尚未生成完整的手动上传目录。请先完成一次有实际变化的构建；无变化构建不会生成 Upload。",
                        "The current build has no complete manual-upload directories. Finish a build with actual changes first; no-change builds do not create Upload."),
                        MessageType.Warning);
                using (new EditorGUI.DisabledScope(_uploading || resourcePublished))
                    if (GUILayout.Button(L("检查上传并完成发布", "Verify Upload and Complete"),
                            GUILayout.Height(38))) VerifyManualUpload();
            }
            if (resourcePublished)
                EditorGUILayout.HelpBox(L(
                    $"资源版本 {_options.resourceVersion} 已发布，不能重复上传。请构建新版本。",
                    $"Resource version {_options.resourceVersion} is published and cannot be uploaded again. Build a new version."),
                    MessageType.Info);
            DrawUploadPlanSummary();
            DrawResourceRestoreSection();
            SaveFtpSettings(_options.target);
            EditorGUILayout.EndScrollView();
        }

        private void RevealUploadDirectory(bool publish)
        {
            if (!DistributionReleaseLayout.TryGetExistingUploadRoot(_options, publish, out string path))
            {
                EditorUtility.DisplayDialog(L("手动上传目录不存在", "Manual Upload Directory Missing"),
                    L("当前版本尚未生成该目录。请先完成一次有实际变化的完整客户端或热更新构建。",
                        "This directory has not been generated for the current version. Complete a Full Package or Hot Update build with actual changes first."),
                    L("确定", "OK"));
                return;
            }
            try
            {
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog(L("打开目录失败", "Failed to Open Directory"),
                    exception.GetBaseException().Message, L("确定", "OK"));
            }
        }

        private void DrawResourceRestoreSection()
        {
            EditorGUILayout.Space(16);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(L("4. 历史版本回滚",
                "4. Historical Version Rollback"), EditorStyles.boldLabel);
            ResourceReleaseBaseline baseline = null;
            try { baseline = ReleaseBaselineStore.Load(_options); }
            catch { }
            string activeVersion = string.IsNullOrWhiteSpace(baseline?.resourceVersion)
                ? L("尚未记录已发布版本", "No published version recorded")
                : baseline.resourceVersion;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField(L("当前线上生效版本", "Active Online Version"),
                    activeVersion);
            EditorGUILayout.HelpBox(L(
                "当新热更新出现问题时，可将线上资源恢复到任意一个此前上传成功的版本。恢复前会验证远端文件完整性，不会删除 Bundle。",
                "If a hot update has a problem, restore online resources to any previously uploaded version. Remote files are verified first and no Bundles are deleted."),
                MessageType.Info);
            using (new EditorGUI.DisabledScope(_uploading || baseline == null))
            {
                var label = new GUIContent(L("选择要恢复的线上资源版本…",
                        "Choose an Online Version to Restore…"),
                    L("查看已上传的历史版本，并选择一个版本恢复为当前线上版本。",
                        "View uploaded versions and restore one as the active online version."));
                if (GUILayout.Button(label, GUILayout.Height(36)))
                    ShowRollbackMenu();
            }
            EditorGUILayout.EndVertical();
        }

        private void GetCurrentPublicationState(out bool resourcePublished, out bool clientPublished)
        {
            resourcePublished = false;
            clientPublished = false;
            if (string.IsNullOrWhiteSpace(_options.resourceVersion)) return;
            try
            {
                string releaseRoot = ReleasePipeline.GetReleaseRoot(_options);
                string planPath = Path.Combine(releaseRoot, "publish-plan.json");
                if (!File.Exists(planPath)) return;
                ReleaseUploadPlan plan = JsonUtility.FromJson<ReleaseUploadPlan>(File.ReadAllText(planPath));
                resourcePublished = ReleaseBaselineStore.IsAlreadyPublished(releaseRoot, plan);
                clientPublished = ReleaseBaselineStore.IsClientAlreadyPublished(releaseRoot, plan);
            }
            catch
            {
                resourcePublished = false;
                clientPublished = false;
            }
        }

        private bool Build(ReleaseMode mode, bool saveSettingsBeforeBuild = true)
        {
            _options.mode = mode;
            _options.automaticResourceVersion = _automaticVersioning;
            if (_automaticVersioning)
                _options.resourceVersion = string.Empty;
            _options.confirmForceAotMetadataPublish = mode == ReleaseMode.HotUpdateOnly
                ? ConfirmForceAotMetadataPublish
                : null;
            if (saveSettingsBeforeBuild)
                SaveReleaseSettings(_options.target);
            try
            {
                // ReleasePipeline.Run 内部执行全部发布校验，按钮无需单独的预校验步骤。
                string path = ReleasePipeline.Run(_options);
                EditorUtility.DisplayDialog(L("发布完成", "Release completed"), path,
                    L("确定", "OK"));
                SaveReleaseSettings(_options.target);
                return true;
            }
            catch (AotMetadataPublishBlockedException exception)
            {
                Debug.LogWarning(exception.Message);
                return false;
            }
            catch (NoReleaseContentChangesException exception)
            {
                if (_automaticVersioning)
                    _options.resourceVersion = string.Empty;
                Debug.Log("[QHYFramework] " + exception.Message);
                EditorUtility.DisplayDialog(L("无需发布", "Nothing to Publish"),
                    exception.Message, L("确定", "OK"));
                return false;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(L("发布失败", "Release failed"),
                    exception.Message, L("确定", "OK"));
                return false;
            }
            finally
            {
                // Authorization applies to this single build attempt only and is never saved.
                _options.confirmForceAotMetadataPublish = null;
            }
        }

        private bool ConfirmForceAotMetadataPublish(string mismatchReason)
        {
            return EditorUtility.DisplayDialog(
                L("检测到 AOT 元数据变化", "AOT metadata changes detected"),
                string.Format(L(
                    "HotUpdateOnly 生成的 AOT 元数据与 {0} 客户端快照不一致：\n\n{1}\n\n" +
                    "强制发布不会把新的 AOT 原生代码注入旧客户端。若热更代码调用旧客户端不存在或签名已变化的 AOT API，可能出现 MissingMethodException、元数据加载失败或崩溃。\n\n" +
                    "建议取消并执行 Full Package。若你已经确认这次 AOT 变化不影响旧客户端，可强制继续；该决定和差异原因会写入 release-report.json。",
                    "The generated HotUpdateOnly AOT metadata differs from the {0} client snapshot:\n\n{1}\n\n" +
                    "Force publication does not inject new native AOT code into existing clients. If hot-update code calls an AOT API that is absent or has a changed signature, clients may encounter MissingMethodException, metadata loading failures, or crashes.\n\n" +
                    "Cancel and run Full Package is recommended. Continue only after confirming that this AOT change is safe for existing clients; the decision and reason are recorded in release-report.json."),
                    _options.clientVersion, mismatchReason),
                L("强制发布", "Force Publish"),
                L("取消发布", "Cancel Release"));
        }

        private void DrawAutomatedFullPackageButtons()
        {
            EditorGUILayout.HelpBox(L(
                "构建期间会临时使用候选版本；仅完整客户端构建成功后保存，失败或取消时恢复原版本。",
                "The candidate version is temporary during the build. It is saved only after a successful full client build and restored on failure or cancellation."),
                MessageType.Info);

            if (!ClientVersion.TryParse(_options.clientVersion, out ClientVersion current))
            {
                EditorGUILayout.HelpBox(L(
                    "版本自动化要求当前版本使用 v主版本.次版本.修订版本 格式，例如 v1.0.0。",
                    "Automatic versioning requires vMAJOR.MINOR.PATCH format, for example v1.0.0."),
                    MessageType.Error);
                using (new EditorGUI.DisabledScope(true))
                {
                    string invalid = L("版本格式无效", "invalid version format");
                    DrawVersionBuildButton(L("主版本构建", "Major Version Build"), null, invalid);
                    DrawVersionBuildButton(L("次版本构建", "Minor Version Build"), null, invalid);
                    DrawVersionBuildButton(L("修订版本构建", "Patch Version Build"), null, invalid);
                }
                return;
            }

            DrawVersionBuildButton(L("主版本构建", "Major Version Build"),
                current.Major < 2099 ? new ClientVersion(current.Major + 1, 0, 0).ToString() : null);
            DrawVersionBuildButton(L("次版本构建", "Minor Version Build"),
                current.Minor < 999 ? new ClientVersion(current.Major, current.Minor + 1, 0).ToString() : null);
            DrawVersionBuildButton(L("修订版本构建", "Patch Version Build"),
                current.Patch < 999 ? new ClientVersion(current.Major, current.Minor, current.Patch + 1).ToString() : null);
        }

        private void DrawVersionBuildButton(string label, string targetVersion, string unavailableReason = null)
        {
            bool valid = !string.IsNullOrWhiteSpace(targetVersion);
            string versionLabel = valid
                ? targetVersion + " / " + SuggestResourceVersion(targetVersion)
                : unavailableReason ?? L("已达到版本上限", "version limit reached");
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button($"{label} ({versionLabel})", GUILayout.Height(42)))
                    BuildFullPackageWithVersion(targetVersion);
            }
        }

        private string SuggestResourceVersion(string clientVersion)
        {
            var preview = new ReleaseOptions
            {
                gameDirectory = _options.gameDirectory,
                clientVersion = clientVersion,
                target = _options.target,
                outputRoot = _options.outputRoot,
                automaticResourceVersion = true
            };
            return ResourceVersionResolver.SuggestNext(preview);
        }

        private void BuildFullPackageWithVersion(string targetVersion)
        {
            string previousVersion = _options.clientVersion;
            int previousAndroidVersionCode = PlayerSettings.Android.bundleVersionCode;

            // 候选版本只在本次构建期间临时生效。构建成功后才写入平台偏好；
            // 取消或任意阶段失败时恢复版本和 Android versionCode。
            _automaticVersionBuildInProgress = true;
            try
            {
                ApplyPackageVersion(targetVersion, false);
                bool succeeded = Build(ReleaseMode.FullPackage, false);
                if (succeeded)
                {
                    SaveReleaseSettings(_options.target);
                    return;
                }

                ApplyPackageVersion(previousVersion, false);
                PlayerSettings.Android.bundleVersionCode = previousAndroidVersionCode;
                SaveReleaseSettings(_options.target);
                Debug.LogWarning(L(
                    $"[QHYFramework] 构建未成功，版本已恢复为 {previousVersion}。",
                    $"[QHYFramework] Build did not succeed; version restored to {previousVersion}."));
            }
            finally
            {
                _automaticVersionBuildInProgress = false;
            }
        }

        private async void Upload(bool includeClient)
        {
            if (_uploading)
                return;
            SaveFtpSettings(_options.target);
            _uploading = true;
            _uploadCancellation = new CancellationTokenSource();
            try
            {
                if (!ClientVersion.TryParse(_options.clientVersion, out _))
                    throw new FormatException(L(
                        "版本必须使用 v主版本.次版本.修订版本 格式，例如 v1.0.0。",
                        "Version must use vMAJOR.MINOR.PATCH format, for example v1.0.0."));
                if (includeClient)
                    ReleasePipeline.ValidateClientUpload(_options);
                string releaseRoot = ReleasePipeline.GetReleaseRoot(_options);
                if (EditorUtility.DisplayCancelableProgressBar(L("准备 FTP 上传", "Preparing FTP Upload"),
                    L("正在读取上传计划并分析远端文件，首次分析可能需要较长时间…",
                        "Reading the upload plan and analyzing remote files. The first analysis can take some time…"),
                    0f))
                    _uploadCancellation.Cancel();
                await FtpReleaseUploader.UploadAsync(releaseRoot, _ftp, includeClient,
                    (actualBytes, actualFiles) => ConfirmUpload(releaseRoot, actualBytes, actualFiles), progress =>
                {
                    bool cancel = EditorUtility.DisplayCancelableProgressBar(progress.Stage,
                        $"{progress.CompletedFiles}/{progress.TotalFiles}  {progress.FileName}", progress.Progress);
                    if (cancel)
                        _uploadCancellation.Cancel();
                }, _uploadCancellation.Token);
                EditorUtility.DisplayDialog(L("FTP 上传完成", "FTP Upload Completed"),
                    L("所有文件和 current 指针均已上传，版本已登记为 Published。",
                        "All files and current pointers were uploaded; the release is now Published."),
                    L("确定", "OK"));
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning(L("[QHYFramework] FTP 上传已取消。",
                    "[QHYFramework] FTP upload canceled."));
            }
            catch (NoReleaseContentChangesException exception)
            {
                Debug.Log("[QHYFramework] " + exception.Message);
                EditorUtility.DisplayDialog(L("无需上传", "Nothing to Upload"),
                    exception.Message, L("确定", "OK"));
            }
            catch (ReleaseAlreadyPublishedException exception)
            {
                Debug.Log("[QHYFramework] " + exception.Message);
                EditorUtility.DisplayDialog(L("已经上传", "Already Uploaded"),
                    exception.Message, L("确定", "OK"));
            }
            catch (Exception exception)
            {
                string safeMessage = PublishingCredentialStore.Redact(
                    exception.GetBaseException().Message, _ftp.Password);
                Debug.LogError("[QHYFramework] FTP upload failed: " + safeMessage);
                EditorUtility.DisplayDialog(L("FTP 上传失败", "FTP Upload failed"),
                    safeMessage, L("确定", "OK"));
            }
            finally
            {
                string password = _ftp.Password;
                PublishingCredentialStore.Clear(GetCredentialScope(_options.target));
                _ftp.Password = _rememberFtpPassword
                    ? PublishingCredentialStore.GetPassword(GetCredentialScope(_options.target))
                    : string.Empty;
                try { PublishingCredentialStore.ThrowIfSecretPersisted(password); }
                catch (Exception securityException)
                {
                    Debug.LogError("[QHYFramework] " + securityException.GetBaseException().Message);
                }
                EditorUtility.ClearProgressBar();
                _uploadCancellation.Dispose();
                _uploadCancellation = null;
                _uploading = false;
                Repaint();
            }
        }

        private async void VerifyManualUpload()
        {
            if (_uploading) return;
            _uploading = true;
            _uploadCancellation = new CancellationTokenSource();
            try
            {
                ResourceReleaseBaseline pending = null;
                try { pending = ReleaseBaselineStore.Load(_options); } catch { }
                string buildRoot = !string.IsNullOrWhiteSpace(pending?.pendingBuildRoot)
                    ? pending.pendingBuildRoot : ReleasePipeline.GetReleaseRoot(_options);
                DistributionVerificationResult result = await DistributionEndpointVerifier.VerifyAndPublishAsync(buildRoot,
                    progress =>
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(progress.Stage,
                                $"{progress.CompletedFiles}/{progress.TotalFiles}  {progress.FileName}",
                                progress.Progress)) _uploadCancellation.Cancel();
                    }, _uploadCancellation.Token);
                EditorUtility.DisplayDialog(L("发布已登记", "Publication Registered"),
                    L($"资源服务器检查通过，已校验 {result.VerifiedFiles} 个文件并写入 Published 基线。",
                        $"Resource server verification passed for {result.VerifiedFiles} files and the Published baseline was committed."),
                    L("确定", "OK"));
            }
            catch (OperationCanceledException) { Debug.LogWarning("[QHYFramework] Resource verification cancelled."); }
            catch (Exception exception)
            {
                Debug.LogError("[QHYFramework] Resource verification failed: " + exception.GetBaseException().Message);
                EditorUtility.DisplayDialog(L("资源服务器检查未通过", "Resource Verification Failed"),
                    exception.GetBaseException().Message, L("确定", "OK"));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _uploadCancellation.Dispose(); _uploadCancellation = null; _uploading = false; Repaint();
            }
        }

        private void ShowRollbackMenu()
        {
            if (_uploading) return;
            ReleaseRollbackTarget[] targets;
            try
            {
                targets = ReleaseBaselineStore.GetRollbackTargets(_options);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog(L("无法恢复线上资源版本", "Cannot Restore Online Version"),
                    exception.GetBaseException().Message,
                    L("确定", "OK"));
                return;
            }
            if (targets.Length == 0)
            {
                EditorUtility.DisplayDialog(L("无法恢复线上资源版本", "Cannot Restore Online Version"),
                    L("没有其他已发布且本地完整的历史资源版本。",
                        "No other published and locally complete resource revision is available."),
                    L("确定", "OK"));
                return;
            }

            ResourceReleaseBaseline baseline = ReleaseBaselineStore.Load(_options);
            var menu = new GenericMenu();
            menu.AddDisabledItem(new GUIContent(L($"当前线上版本：{baseline.resourceVersion}",
                $"Active online version: {baseline.resourceVersion}")));
            menu.AddSeparator(string.Empty);
            foreach (ReleaseRollbackTarget target in targets)
            {
                ReleaseRollbackTarget captured = target;
                menu.AddItem(new GUIContent(captured.ResourceVersion), false,
                    () => Rollback(captured));
            }
            menu.ShowAsContext();
        }

        private async void Rollback(ReleaseRollbackTarget target)
        {
            if (_uploading || target == null) return;
            if (!EditorUtility.DisplayDialog(L("确认恢复线上资源版本", "Confirm Online Version Restore"),
                    L($"将把 version 指针切换到 {target.ResourceVersion}，不会删除任何 Bundle。",
                        $"The version pointer will switch to {target.ResourceVersion}. No bundles will be deleted."),
                    L("开始恢复", "Restore"), L("取消", "Cancel")))
                return;

            if (_manualUpload)
            {
                try
                {
                    string root = FtpReleaseUploader.PrepareManualRollback(target.ReleaseRoot);
                    EditorUtility.RevealInFinder(root);
                    EditorUtility.DisplayDialog(L("手动回滚文件已生成", "Manual Rollback Ready"),
                        L("请把 Rollback 中的内容上传到资源服务器根，然后点击“检查上传并完成发布”。",
                            "Upload the Rollback contents to the resource server root, then click Verify Upload and Complete."),
                        L("确定", "OK"));
                }
                catch (Exception exception)
                {
                    EditorUtility.DisplayDialog(L("生成回滚文件失败", "Rollback Preparation Failed"),
                        exception.GetBaseException().Message, L("确定", "OK"));
                }
                return;
            }

            SaveFtpSettings(_options.target);
            _uploading = true;
            _uploadCancellation = new CancellationTokenSource();
            try
            {
                await FtpReleaseUploader.RollbackAsync(target.ReleaseRoot, _ftp, progress =>
                {
                    bool cancel = EditorUtility.DisplayCancelableProgressBar(progress.Stage,
                        $"{progress.CompletedFiles}/{progress.TotalFiles}  {progress.FileName}",
                        progress.Progress);
                    if (cancel) _uploadCancellation.Cancel();
                }, _uploadCancellation.Token);
                EditorUtility.DisplayDialog(L("回滚完成", "Rollback Completed"),
                    L($"资源指针已切换到 {target.ResourceVersion}，回滚历史已登记。",
                        $"The resource pointer now targets {target.ResourceVersion}; rollback history was recorded."),
                    L("确定", "OK"));
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning(L("[QHYFramework] 资源回滚已取消。",
                    "[QHYFramework] Resource rollback canceled."));
            }
            catch (Exception exception)
            {
                string safe = PublishingCredentialStore.Redact(exception.GetBaseException().Message, _ftp.Password);
                Debug.LogError("[QHYFramework] Rollback failed: " + safe);
                EditorUtility.DisplayDialog(L("恢复线上资源版本失败", "Online Version Restore Failed"),
                    safe, L("确定", "OK"));
            }
            finally
            {
                PublishingCredentialStore.Clear(GetCredentialScope(_options.target));
                _ftp.Password = _rememberFtpPassword
                    ? PublishingCredentialStore.GetPassword(GetCredentialScope(_options.target))
                    : string.Empty;
                _uploadCancellation.Dispose();
                _uploadCancellation = null;
                _uploading = false;
                Repaint();
            }
        }

        private void LoadFtpSettings(BuildTarget target)
        {
            string prefix = GetFtpPrefsPrefix(target);
            _ftp.Host = EditorPrefs.GetString(prefix + "Host", string.Empty);
            _ftp.Port = EditorPrefs.GetInt(prefix + "Port", 21);
            _ftp.UserName = PublishingCredentialStore.GetUserName(
                EditorPrefs.GetString(prefix + "UserName", string.Empty));
            _ftp.Password = PublishingCredentialStore.GetPassword(GetCredentialScope(target));
            EditorPrefs.DeleteKey(prefix + "RemoteRoot");
            _ftp.Concurrency = EditorPrefs.GetInt(prefix + "Concurrency", 4);
            _ftp.EnableSsl = EditorPrefs.GetBool(prefix + "EnableSsl", false);
            _ftp.UsePassive = EditorPrefs.GetBool(prefix + "UsePassive", true);
            _rememberFtpPassword = EditorPrefs.GetBool(prefix + "RememberPassword", true);
        }

        private void SwitchReleaseTarget(BuildTarget selectedTarget)
        {
            BuildTarget previousTarget = _options.target;
            _switchingTarget = true;
            try
            {
                SaveFtpSettings(previousTarget);
                SaveReleaseSettings(previousTarget);
                EditorUtility.DisplayProgressBar(L("切换目标平台", "Switch Build Target"),
                    string.Format(L("正在切换到 {0}…", "Switching to {0}…"), selectedTarget), 0.5f);
                ReleaseBuildTargetSwitcher.SwitchOrThrow(selectedTarget);

                _options.target = selectedTarget;
                _lastTarget = selectedTarget;
                LoadReleaseSettings(selectedTarget);
                LoadFtpSettings(selectedTarget);
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                Debug.Log(string.Format(L(
                    "[QHYFramework] 已切换 Unity 目标平台并同步 Settings：{0}",
                    "[QHYFramework] Switched Unity build target and synchronized Settings: {0}"),
                    selectedTarget));
            }
            catch (Exception exception)
            {
                _options.target = EditorUserBuildSettings.activeBuildTarget == BuildTarget.NoTarget
                    ? previousTarget
                    : EditorUserBuildSettings.activeBuildTarget;
                _lastTarget = _options.target;
                LoadReleaseSettings(_lastTarget);
                LoadFtpSettings(_lastTarget);
                string message = exception.GetBaseException().Message;
                Debug.LogError("[QHYFramework] Build target switch failed: " + message);
                EditorUtility.DisplayDialog(L("切换目标平台失败", "Build Target Switch Failed"),
                    message, L("确定", "OK"));
            }
            finally
            {
                _switchingTarget = false;
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        private void SaveFtpSettings(BuildTarget target)
        {
            string prefix = GetFtpPrefsPrefix(target);
            EditorPrefs.SetString(prefix + "Host", _ftp.Host?.Trim() ?? string.Empty);
            EditorPrefs.SetInt(prefix + "Port", Mathf.Max(1, _ftp.Port));
            EditorPrefs.SetString(prefix + "UserName", _ftp.UserName ?? string.Empty);
            EditorPrefs.DeleteKey(prefix + "RemoteRoot");
            EditorPrefs.SetInt(prefix + "Concurrency", _ftp.Concurrency);
            EditorPrefs.SetBool(prefix + "EnableSsl", _ftp.EnableSsl);
            EditorPrefs.SetBool(prefix + "UsePassive", _ftp.UsePassive);
            EditorPrefs.SetBool(prefix + "RememberPassword", _rememberFtpPassword);
            EditorPrefs.SetString(prefix + "BoundPlayerVersion", _options.clientVersion ?? string.Empty);
            PublishingCredentialStore.SetPassword(GetCredentialScope(target), _ftp.Password,
                _rememberFtpPassword);
        }

        private void DrawUploadPlanSummary()
        {
            if (string.IsNullOrWhiteSpace(_options.resourceVersion)) return;
            string path = Path.Combine(ReleasePipeline.GetReleaseRoot(_options), "publish-plan.json");
            if (!File.Exists(path)) return;
            try
            {
                ReleaseUploadPlan plan = JsonUtility.FromJson<ReleaseUploadPlan>(File.ReadAllText(path));
                if (plan == null) return;
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField(L("当前增量计划", "Current Incremental Plan"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(L("完整资源快照", "Full snapshot"),
                    GamePackageRuntime.FormatBytes(plan.snapshotBytes));
                EditorGUILayout.LabelField(L("计划 FTP 上传", "Planned FTP upload"),
                    GamePackageRuntime.FormatBytes(plan.uploadBytes));
                EditorGUILayout.LabelField(L("预计客户端更新", "Estimated client update"),
                    GamePackageRuntime.FormatBytes(plan.estimatedClientDownloadBytes));
                int added = (plan.added ?? Array.Empty<UploadArtifact>()).Count(item => item.contentAddressedBundle);
                int changed = (plan.changed ?? Array.Empty<UploadArtifact>()).Count(item => item.contentAddressedBundle);
                int unchanged = (plan.unchanged ?? Array.Empty<UploadArtifact>()).Count(item => item.contentAddressedBundle);
                EditorGUILayout.LabelField(L("Bundle 变化", "Bundle changes"),
                    $"+{added}  ~{changed}  ={unchanged}");
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(exception.GetBaseException().Message, MessageType.Warning);
            }
        }

        private bool ConfirmUpload(string releaseRoot, long actualBytes, int actualFiles)
        {
            string path = Path.Combine(releaseRoot, "publish-plan.json");
            if (!File.Exists(path)) return true;
            ReleaseUploadPlan plan = JsonUtility.FromJson<ReleaseUploadPlan>(File.ReadAllText(path));
            if (plan == null) return true;
            UploadArtifact[] largest = (plan.added ?? Array.Empty<UploadArtifact>())
                .Concat(plan.changed ?? Array.Empty<UploadArtifact>())
                .Where(item => item.contentAddressedBundle)
                .OrderByDescending(item => item.length).Take(10).ToArray();
            string details = string.Join("\n", largest.Select(item =>
                $"{item.relativePath}  {GamePackageRuntime.FormatBytes(item.length)}  {item.changeType}"));
            string message = L(
                $"完整资源快照：{GamePackageRuntime.FormatBytes(plan.snapshotBytes)}\n" +
                $"FTP 实际上传：{GamePackageRuntime.FormatBytes(actualBytes)}（{actualFiles} 个文件）\n" +
                $"预计客户端更新：{GamePackageRuntime.FormatBytes(plan.estimatedClientDownloadBytes)}\n\n{details}",
                $"Full snapshot: {GamePackageRuntime.FormatBytes(plan.snapshotBytes)}\n" +
                $"Actual FTP upload: {GamePackageRuntime.FormatBytes(actualBytes)} ({actualFiles} files)\n" +
                $"Estimated client update: {GamePackageRuntime.FormatBytes(plan.estimatedClientDownloadBytes)}\n\n{details}");
            return EditorUtility.DisplayDialog(L("确认增量上传", "Confirm Incremental Upload"), message,
                L("上传", "Upload"), L("取消", "Cancel"));
        }

        private static string GetCredentialScope(BuildTarget target)
        {
            return GetProjectPrefsPrefix(FtpPrefsPrefix) +
                   ReleasePipeline.GetIntegrationPlatform(target) + ".Password";
        }

        private static string GetFtpPrefsPrefix(BuildTarget target)
        {
            return GetProjectPrefsPrefix(FtpPrefsPrefix) +
                   ReleasePipeline.GetIntegrationPlatform(target) + ".";
        }

        private void LoadReleaseSettings(BuildTarget target)
        {
            string prefix = GetReleasePrefsPrefix(target);
            _options.mode = (ReleaseMode)EditorPrefs.GetInt(prefix + "ReleaseMode",
                (int)ReleaseMode.FullPackage);
            _options.developmentBuild = EditorPrefs.GetBool(prefix + "DevelopmentBuild", false);
            bool automaticVersioningInitialized = EditorPrefs.GetBool(
                prefix + "AutomaticVersioningInitialized", false);
            _automaticVersioning = automaticVersioningInitialized
                ? EditorPrefs.GetBool(prefix + "AutomaticVersioning", true)
                : true;
            if (!automaticVersioningInitialized)
            {
                // Migrate projects from the previous opt-in behavior to the new default once.
                EditorPrefs.SetBool(prefix + "AutomaticVersioning", true);
                EditorPrefs.SetBool(prefix + "AutomaticVersioningInitialized", true);
            }
            _options.outputRoot = "Releases";

            bool versionInitialized = EditorPrefs.GetBool(prefix + "PackageVersionInitialized", false);
            string platformVersion = versionInitialized
                ? EditorPrefs.GetString(prefix + "PackageVersion", string.Empty)
                : string.Empty;
            if (!versionInitialized)
                platformVersion = LoadPublishedClientVersion(target, _options.outputRoot);
            if (string.IsNullOrWhiteSpace(platformVersion))
                platformVersion = InitialPackageVersion;

            _options.clientVersion = platformVersion?.Trim() ?? string.Empty;
            _options.resourceVersion = EditorPrefs.GetString(prefix + "ResourceVersion", string.Empty);
            PlayerSettings.bundleVersion = _options.clientVersion;
            if (ReleasePipeline.GetIntegrationPlatform(target) == IntegrationPlatform.Android &&
                ClientVersion.TryParse(_options.clientVersion, out ClientVersion androidVersion))
                PlayerSettings.Android.bundleVersionCode = androidVersion.AndroidVersionCode;
            _lastObservedPlayerVersion = _options.clientVersion;
            EditorPrefs.SetString(prefix + "PackageVersion", _options.clientVersion);
            EditorPrefs.SetBool(prefix + "PackageVersionInitialized", true);
        }

        private void SaveReleaseSettings(BuildTarget target)
        {
            string prefix = GetReleasePrefsPrefix(target);
            EditorPrefs.SetInt(prefix + "ReleaseMode", (int)_options.mode);
            EditorPrefs.SetString(prefix + "PackageVersion", _options.clientVersion ?? string.Empty);
            EditorPrefs.SetString(prefix + "ResourceVersion", _options.resourceVersion ?? string.Empty);
            EditorPrefs.SetBool(prefix + "PackageVersionInitialized", true);
            EditorPrefs.SetBool(prefix + "DevelopmentBuild", _options.developmentBuild);
            EditorPrefs.SetBool(prefix + "AutomaticVersioning", _automaticVersioning);
            EditorPrefs.SetBool(prefix + "AutomaticVersioningInitialized", true);
        }

        private void SynchronizeVersionFromPlayerSettings()
        {
            string playerVersion = PlayerSettings.bundleVersion ?? string.Empty;
            if (string.Equals(playerVersion, _lastObservedPlayerVersion, StringComparison.Ordinal))
                return;
            _options.clientVersion = playerVersion;
            _options.resourceVersion = string.Empty;
            _lastObservedPlayerVersion = playerVersion;
            EditorPrefs.SetString(GetReleasePrefsPrefix(_options.target) + "PackageVersion", playerVersion);
            Repaint();
        }

        private void SetPackageVersion(string version)
        {
            ApplyPackageVersion(version, true);
        }

        private void ApplyPackageVersion(string version, bool persist)
        {
            _options.clientVersion = version?.Trim() ?? string.Empty;
            _options.resourceVersion = string.Empty;
            PlayerSettings.bundleVersion = _options.clientVersion;
            _lastObservedPlayerVersion = _options.clientVersion;
            if (persist)
                EditorPrefs.SetString(GetReleasePrefsPrefix(_options.target) + "PackageVersion",
                    _options.clientVersion);
        }

        private static string LoadPublishedClientVersion(BuildTarget target, string outputRoot)
        {
            if (string.IsNullOrWhiteSpace(outputRoot))
                return string.Empty;
            try
            {
                ResourceReleaseBaseline baseline = ReleaseBaselineStore.Load(new ReleaseOptions
                {
                    outputRoot = outputRoot, target = target
                });
                return baseline?.hasPublishedBaseline == true &&
                       ClientVersion.TryParse(baseline.clientVersion, out _)
                    ? baseline.clientVersion : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string GetReleasePrefsPrefix(BuildTarget target)
        {
            return GetProjectPrefsPrefix(ReleasePrefsPrefix) +
                   ReleasePipeline.GetIntegrationPlatform(target) + ".";
        }

        private static string GetProjectPrefsPrefix(string prefix)
        {
            // EditorPrefs is machine-global. Scope every release/FTP preference to the
            // current Unity project so importing this UPM package into another project
            // can never inherit credentials, URLs or platform versions from a test project.
            string projectIdentity = (Application.dataPath ?? string.Empty)
                .Replace('\\', '/')
                .TrimEnd('/')
                .ToLowerInvariant();
            return prefix + Hash128.Compute(projectIdentity) + ".";
        }

        private void OnLanguageChanged()
        {
            UpdateTitle();
            Repaint();
        }

        private void UpdateTitle()
        {
            titleContent = new GUIContent(L("QHY Framework 发布工具", "QHY Framework Release"));
        }

        private static string L(string chinese, string english)
        {
            return EditorLocalization.Text(chinese, english);
        }
    }
}
