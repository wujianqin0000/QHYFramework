using System;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace GameIntegration.Editor
{
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

            _options.channel = EditorGUILayout.TextField(L("发布渠道", "Channel"), _options.channel);
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
            BuildTarget selectedTarget = (BuildTarget)EditorGUILayout.EnumPopup(
                L("目标平台", "Build Target"), _options.target);
            if (selectedTarget != _options.target)
            {
                SaveFtpSettings(_lastTarget);
                SaveReleaseSettings(_lastTarget);
                _options.target = selectedTarget;
                _lastTarget = selectedTarget;
                LoadReleaseSettings(selectedTarget);
                LoadFtpSettings(selectedTarget);
            }
            _options.developmentBuild = EditorGUILayout.Toggle(L("开发构建", "Development Build"),
                _options.developmentBuild);
            _options.outputRoot = EditorGUILayout.TextField(L("输出目录", "Output Root"), _options.outputRoot);

            QHYFrameworkSettings settings = null;
            try { settings = IntegrationProjectPreparer.LoadSettings(); }
            catch { }
            using (new EditorGUI.DisabledScope(true))
            {
                IntegrationPlatform platform = ReleasePipeline.GetIntegrationPlatform(_options.target);
                EditorGUILayout.TextField(L("远端根地址", "Remote Base URL"), settings
                    ? settings.GetRemoteBaseUrl(platform)
                    : L("<缺少配置>", "<missing settings>"));
                EditorGUILayout.TextField(L("远端资源地址（自动拼接）", "Remote Package URL (generated)"), settings
                    ? settings.GetRemotePackageUrl(platform, _options.clientVersion)
                    : L("<缺少配置>", "<missing settings>"));
                string clientBase = settings ? settings.GetClientUpdateBaseUrl(platform) : string.Empty;
                string clientFile = GetClientArtifactName(platform, _options.clientVersion);
                EditorGUILayout.TextField(L("客户端包地址（自动拼接）", "Client Package URL (generated)"),
                    string.IsNullOrWhiteSpace(clientBase) ? L("<缺少配置>", "<missing settings>") :
                    $"{clientBase}/{_options.clientVersion}/{clientFile}");
                EditorGUILayout.TextField(L("最新客户端清单", "Latest Client Manifest"),
                    settings ? settings.GetClientManifestUrl(platform) : L("<缺少配置>", "<missing settings>"));
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField(L("构建发布包", "Build Release Packages"),
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
            EditorGUILayout.LabelField(L("FTP 上传", "FTP Upload"), EditorStyles.boldLabel);
            ApplyRemoteUrlDefaults();
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
            using (new EditorGUI.DisabledScope(true))
            {
                _ftp.HotUpdateDirectory = EditorGUILayout.TextField(
                    L("热更新远端目录（自动）", "Hot Update Remote Directory (generated)"),
                    _ftp.HotUpdateDirectory);
                _ftp.ClientDirectory = EditorGUILayout.TextField(
                    L("客户端远端目录（自动）", "Client Remote Directory (generated)"),
                    _ftp.ClientDirectory);
            }
            _ftp.UsePassive = EditorGUILayout.Toggle(L("被动模式", "Passive Mode"), _ftp.UsePassive);
            _ftp.EnableSsl = EditorGUILayout.Toggle(L("启用 SSL（FTPS）", "Enable SSL (FTPS)"),
                _ftp.EnableSsl);

            GetCurrentPublicationState(out bool resourcePublished, out bool clientPublished);
            using (new EditorGUI.DisabledScope(_uploading || resourcePublished))
            {
                if (GUILayout.Button(L("上传热更新包", "Upload Hot Update Package"), GUILayout.Height(36)))
                    Upload(false);
            }
            using (new EditorGUI.DisabledScope(_uploading || (resourcePublished && clientPublished)))
            {
                if (GUILayout.Button(L("上传热更新包和客户端包", "Upload Hot Update + Client Package"),
                        GUILayout.Height(36)))
                    Upload(true);
            }
            if (resourcePublished)
                EditorGUILayout.HelpBox(L(
                    clientPublished
                        ? $"资源版本 {_options.resourceVersion} 已上传成功，不能重复上传。请构建新版本；回滚后可重新发布。"
                        : $"资源版本 {_options.resourceVersion} 已上传；仍可用第二个按钮补传尚未发布的客户端包。",
                    clientPublished
                        ? $"Resource version {_options.resourceVersion} was uploaded successfully and cannot be uploaded twice. Build a new version; it may be republished after rollback."
                        : $"Resource version {_options.resourceVersion} is uploaded; the second button can still publish the pending client package."),
                    MessageType.Info);
            DrawUploadPlanSummary();
            DrawResourceRestoreSection();
            SaveFtpSettings(_options.target);
            EditorGUILayout.EndScrollView();
        }

        private void DrawResourceRestoreSection()
        {
            EditorGUILayout.Space(16);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(L("线上资源版本恢复（回滚）",
                "Restore an Online Resource Version (Rollback)"), EditorStyles.boldLabel);
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
                string planPath = Path.Combine(releaseRoot, "upload-plan.json");
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
                channel = _options.channel,
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
                EditorUtility.DisplayDialog(L("FTP 上传完成", "FTP Upload completed"),
                    includeClient
                        ? L("热更新包和客户端包上传完成。", "Hot update and client packages uploaded successfully.")
                        : L("热更新包上传完成。", "Hot update package uploaded successfully."),
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
                    L($"将先验证远端完整性，再把 version 指针切换到 {target.ResourceVersion}。不会删除任何 Bundle。",
                        $"Remote completeness will be verified before switching the version pointer to {target.ResourceVersion}. No bundles will be deleted."),
                    L("开始恢复", "Restore"), L("取消", "Cancel")))
                return;

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
                EditorUtility.DisplayDialog(L("线上资源版本已恢复", "Online Version Restored"),
                    L($"当前线上资源版本：{target.ResourceVersion}",
                        $"Active online resource version: {target.ResourceVersion}"),
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

        private void ApplyRemoteUrlDefaults()
        {
            try
            {
                QHYFrameworkSettings settings = IntegrationProjectPreparer.LoadSettings();
                IntegrationPlatform platform = ReleasePipeline.GetIntegrationPlatform(_options.target);
                string remoteUrl = settings.GetRemotePackageUrl(platform, _options.clientVersion);
                if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri uri))
                    return;
                _ftp.HotUpdateDirectory = uri.AbsolutePath;
                string clientBaseUrl = settings.GetClientUpdateBaseUrl(platform);
                _ftp.ClientDirectory = Uri.TryCreate(clientBaseUrl, UriKind.Absolute, out Uri clientUri)
                    ? clientUri.AbsolutePath.TrimEnd('/') + "/" + _options.clientVersion
                    : GetDefaultClientDirectory(uri.AbsolutePath, platform);
            }
            catch
            {
                // 配置缺失时保留空字段，由上传校验给出明确提示。
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
            _ftp.HotUpdateDirectory = EditorPrefs.GetString(prefix + "HotDirectory", string.Empty);
            _ftp.ClientDirectory = EditorPrefs.GetString(prefix + "ClientDirectory", string.Empty);
            _ftp.EnableSsl = EditorPrefs.GetBool(prefix + "EnableSsl", false);
            _ftp.UsePassive = EditorPrefs.GetBool(prefix + "UsePassive", true);
            _rememberFtpPassword = EditorPrefs.GetBool(prefix + "RememberPassword", true);
            string previousBoundVersion = EditorPrefs.GetString(prefix + "BoundPlayerVersion",
                _options.clientVersion);
            ReplaceTrailingVersion(ref _ftp.HotUpdateDirectory, previousBoundVersion,
                _options.clientVersion);
            ReplaceTrailingVersion(ref _ftp.ClientDirectory, previousBoundVersion,
                _options.clientVersion);
            ApplyRemoteUrlDefaults();
        }

        private void SaveFtpSettings(BuildTarget target)
        {
            string prefix = GetFtpPrefsPrefix(target);
            EditorPrefs.SetString(prefix + "Host", _ftp.Host?.Trim() ?? string.Empty);
            EditorPrefs.SetInt(prefix + "Port", Mathf.Max(1, _ftp.Port));
            EditorPrefs.SetString(prefix + "UserName", _ftp.UserName ?? string.Empty);
            EditorPrefs.SetString(prefix + "HotDirectory", _ftp.HotUpdateDirectory ?? string.Empty);
            EditorPrefs.SetString(prefix + "ClientDirectory", _ftp.ClientDirectory ?? string.Empty);
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
            string path = Path.Combine(ReleasePipeline.GetReleaseRoot(_options), "upload-plan.json");
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
            string path = Path.Combine(releaseRoot, "upload-plan.json");
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
            _options.channel = EditorPrefs.GetString(prefix + "Channel", "default");
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
            _options.outputRoot = EditorPrefs.GetString(GetProjectPrefsPrefix(ReleasePrefsPrefix) + "OutputRoot",
                "Releases");

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
            EditorPrefs.SetString(prefix + "Channel", _options.channel ?? string.Empty);
            EditorPrefs.SetString(prefix + "PackageVersion", _options.clientVersion ?? string.Empty);
            EditorPrefs.SetString(prefix + "ResourceVersion", _options.resourceVersion ?? string.Empty);
            EditorPrefs.SetBool(prefix + "PackageVersionInitialized", true);
            EditorPrefs.SetBool(prefix + "DevelopmentBuild", _options.developmentBuild);
            EditorPrefs.SetBool(prefix + "AutomaticVersioning", _automaticVersioning);
            EditorPrefs.SetBool(prefix + "AutomaticVersioningInitialized", true);
            EditorPrefs.SetString(GetProjectPrefsPrefix(ReleasePrefsPrefix) + "OutputRoot",
                _options.outputRoot ?? "Releases");
        }

        private void SynchronizeVersionFromPlayerSettings()
        {
            string playerVersion = PlayerSettings.bundleVersion ?? string.Empty;
            if (string.Equals(playerVersion, _lastObservedPlayerVersion, StringComparison.Ordinal))
                return;
            string previousVersion = _options.clientVersion;
            _options.clientVersion = playerVersion;
            _options.resourceVersion = string.Empty;
            _lastObservedPlayerVersion = playerVersion;
            EditorPrefs.SetString(GetReleasePrefsPrefix(_options.target) + "PackageVersion", playerVersion);
            ReplaceTrailingVersion(ref _ftp.HotUpdateDirectory, previousVersion, playerVersion);
            ReplaceTrailingVersion(ref _ftp.ClientDirectory, previousVersion, playerVersion);
            Repaint();
        }

        private void SetPackageVersion(string version)
        {
            ApplyPackageVersion(version, true);
        }

        private void ApplyPackageVersion(string version, bool persist)
        {
            string previousVersion = _options.clientVersion;
            _options.clientVersion = version?.Trim() ?? string.Empty;
            _options.resourceVersion = string.Empty;
            PlayerSettings.bundleVersion = _options.clientVersion;
            _lastObservedPlayerVersion = _options.clientVersion;
            if (persist)
                EditorPrefs.SetString(GetReleasePrefsPrefix(_options.target) + "PackageVersion",
                    _options.clientVersion);
            ReplaceTrailingVersion(ref _ftp.HotUpdateDirectory, previousVersion, _options.clientVersion);
            ReplaceTrailingVersion(ref _ftp.ClientDirectory, previousVersion, _options.clientVersion);
        }

        private static string LoadPublishedClientVersion(BuildTarget target, string outputRoot)
        {
            if (string.IsNullOrWhiteSpace(outputRoot))
                return string.Empty;

            string baselinePath = Path.Combine(outputRoot, "Baselines",
                ReleasePipeline.GetPlatformName(target) + ".client-version");
            if (!File.Exists(baselinePath))
                return string.Empty;

            try
            {
                string version = File.ReadAllText(baselinePath).Trim();
                return ClientVersion.TryParse(version, out _) ? version : string.Empty;
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        private static void ReplaceTrailingVersion(ref string path, string previousVersion, string nextVersion)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(previousVersion))
                return;
            string suffix = "/" + previousVersion.Trim().Trim('/');
            if (!path.TrimEnd('/').EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return;
            path = path.TrimEnd('/').Substring(0, path.TrimEnd('/').Length - suffix.Length) + "/" +
                   nextVersion.Trim().Trim('/');
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

        private static string GetDefaultClientDirectory(string hotDirectory, IntegrationPlatform platform)
        {
            int cdnIndex = hotDirectory.IndexOf("/CDN/", StringComparison.OrdinalIgnoreCase);
            if (cdnIndex >= 0)
                return hotDirectory.Substring(0, cdnIndex) + "/Client/" +
                       hotDirectory.Substring(cdnIndex + "/CDN/".Length);
            return $"/Client/{platform}";
        }

        private static string GetClientArtifactName(IntegrationPlatform platform, string version)
        {
            if (platform == IntegrationPlatform.Windows) return $"Client_Windows64_{version}.zip";
            if (platform == IntegrationPlatform.Android) return $"Client_Android_{version}.apk";
            return string.Empty;
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
