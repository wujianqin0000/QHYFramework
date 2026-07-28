using System;
using System.IO;
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
        private bool _automaticVersioning;
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
            string editedVersion = EditorGUILayout.TextField(L("当前平台版本（Player Version）",
                "Current Platform Version (Player Version)"), _options.packageVersion);
            if (!string.Equals(editedVersion, _options.packageVersion, StringComparison.Ordinal))
                SetPackageVersion(editedVersion);
            _automaticVersioning = EditorGUILayout.Toggle(
                L("版本自动化", "Automatic Versioning"), _automaticVersioning);
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
                    ? settings.GetRemotePackageUrl(platform, _options.packageVersion)
                    : L("<缺少配置>", "<missing settings>"));
                string clientBase = settings ? settings.GetClientUpdateBaseUrl(platform) : string.Empty;
                string clientFile = GetClientArtifactName(platform, _options.packageVersion);
                EditorGUILayout.TextField(L("客户端包地址（自动拼接）", "Client Package URL (generated)"),
                    string.IsNullOrWhiteSpace(clientBase) ? L("<缺少配置>", "<missing settings>") :
                    $"{clientBase}/{_options.packageVersion}/{clientFile}");
                EditorGUILayout.TextField(L("最新客户端清单", "Latest Client Manifest"),
                    settings ? settings.GetClientManifestUrl(platform) : L("<缺少配置>", "<missing settings>"));
            }

            EditorGUILayout.Space(12);
            if (_automaticVersioning)
                DrawAutomatedFullPackageButtons();
            else if (GUILayout.Button(L("完整客户端构建", "Full Package Build"), GUILayout.Height(42)))
                Build(ReleaseMode.FullPackage);
            if (GUILayout.Button(L("仅热更新构建", "HotUpdateOnly Build"), GUILayout.Height(42)))
                Build(ReleaseMode.HotUpdateOnly);

            EditorGUILayout.Space(18);
            EditorGUILayout.LabelField(L("FTP 上传", "FTP Upload"), EditorStyles.boldLabel);
            SynchronizeFtpConnectionFromSettings(settings);
            ApplyRemoteUrlDefaults();
            _ftp.Host = EditorGUILayout.TextField(L("FTP 主机", "FTP Host"), _ftp.Host);
            _ftp.Port = EditorGUILayout.IntField(L("FTP 端口", "FTP Port"), _ftp.Port);
            _ftp.UserName = EditorGUILayout.TextField(L("FTP 用户名", "FTP User Name"), _ftp.UserName);
            _ftp.Password = EditorGUILayout.PasswordField(L("FTP 密码", "FTP Password"), _ftp.Password);
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

            using (new EditorGUI.DisabledScope(_uploading))
            {
                if (GUILayout.Button(L("上传热更新包", "Upload Hot Update Package"), GUILayout.Height(36)))
                    Upload(false);
                if (GUILayout.Button(L("上传热更新包和客户端包", "Upload Hot Update + Client Package"),
                        GUILayout.Height(36)))
                    Upload(true);
            }
            SaveFtpConnectionToSettings(settings);
            EditorGUILayout.EndScrollView();
        }

        private bool Build(ReleaseMode mode, bool saveSettingsBeforeBuild = true)
        {
            _options.mode = mode;
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
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(L("发布失败", "Release failed"),
                    exception.Message, L("确定", "OK"));
                return false;
            }
        }

        private void DrawAutomatedFullPackageButtons()
        {
            EditorGUILayout.HelpBox(L(
                "构建期间会临时使用候选版本；仅完整客户端构建成功后保存，失败或取消时恢复原版本。",
                "The candidate version is temporary during the build. It is saved only after a successful full client build and restored on failure or cancellation."),
                MessageType.Info);

            if (!ClientVersion.TryParse(_options.packageVersion, out ClientVersion current))
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
                ? targetVersion
                : unavailableReason ?? L("已达到版本上限", "version limit reached");
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button($"{label} ({versionLabel})", GUILayout.Height(42)))
                    BuildFullPackageWithVersion(targetVersion);
            }
        }

        private void BuildFullPackageWithVersion(string targetVersion)
        {
            string previousVersion = _options.packageVersion;
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
                if (!ClientVersion.TryParse(_options.packageVersion, out _))
                    throw new FormatException(L(
                        "版本必须使用 v主版本.次版本.修订版本 格式，例如 v1.0.0。",
                        "Version must use vMAJOR.MINOR.PATCH format, for example v1.0.0."));
                if (includeClient)
                    ReleasePipeline.ValidateClientUpload(_options);
                string releaseRoot = ReleasePipeline.GetReleaseRoot(_options);
                await FtpReleaseUploader.UploadAsync(releaseRoot, _ftp, includeClient, progress =>
                {
                    bool cancel = EditorUtility.DisplayCancelableProgressBar(L("FTP 上传", "FTP Upload"),
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
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(L("FTP 上传失败", "FTP Upload failed"),
                    exception.GetBaseException().Message, L("确定", "OK"));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
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
                string remoteUrl = settings.GetRemotePackageUrl(platform, _options.packageVersion);
                if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri uri))
                    return;
                _ftp.HotUpdateDirectory = uri.AbsolutePath;
                string clientBaseUrl = settings.GetClientUpdateBaseUrl(platform);
                _ftp.ClientDirectory = Uri.TryCreate(clientBaseUrl, UriKind.Absolute, out Uri clientUri)
                    ? clientUri.AbsolutePath.TrimEnd('/') + "/" + _options.packageVersion
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
            try
            {
                SynchronizeFtpConnectionFromSettings(IntegrationProjectPreparer.LoadSettings());
            }
            catch
            {
                _ftp.Host = string.Empty;
                _ftp.Port = 21;
                _ftp.UserName = string.Empty;
                _ftp.Password = string.Empty;
            }
            _ftp.HotUpdateDirectory = EditorPrefs.GetString(prefix + "HotDirectory", string.Empty);
            _ftp.ClientDirectory = EditorPrefs.GetString(prefix + "ClientDirectory", string.Empty);
            _ftp.EnableSsl = EditorPrefs.GetBool(prefix + "EnableSsl", false);
            _ftp.UsePassive = EditorPrefs.GetBool(prefix + "UsePassive", true);
            string previousBoundVersion = EditorPrefs.GetString(prefix + "BoundPlayerVersion",
                _options.packageVersion);
            ReplaceTrailingVersion(ref _ftp.HotUpdateDirectory, previousBoundVersion,
                _options.packageVersion);
            ReplaceTrailingVersion(ref _ftp.ClientDirectory, previousBoundVersion,
                _options.packageVersion);
            ApplyRemoteUrlDefaults();
        }

        private void SaveFtpSettings(BuildTarget target)
        {
            string prefix = GetFtpPrefsPrefix(target);
            EditorPrefs.SetString(prefix + "HotDirectory", _ftp.HotUpdateDirectory ?? string.Empty);
            EditorPrefs.SetString(prefix + "ClientDirectory", _ftp.ClientDirectory ?? string.Empty);
            EditorPrefs.SetBool(prefix + "EnableSsl", _ftp.EnableSsl);
            EditorPrefs.SetBool(prefix + "UsePassive", _ftp.UsePassive);
            EditorPrefs.SetString(prefix + "BoundPlayerVersion", _options.packageVersion ?? string.Empty);
        }

        private void SynchronizeFtpConnectionFromSettings(QHYFrameworkSettings settings)
        {
            if (!settings)
                return;
            _ftp.Host = settings.ftpHost ?? string.Empty;
            _ftp.Port = settings.ftpPort > 0 ? settings.ftpPort : 21;
            _ftp.UserName = settings.ftpUserName ?? string.Empty;
            _ftp.Password = settings.ftpPassword ?? string.Empty;
        }

        private void SaveFtpConnectionToSettings(QHYFrameworkSettings settings)
        {
            if (!settings)
                return;

            string host = _ftp.Host?.Trim() ?? string.Empty;
            int port = Mathf.Max(1, _ftp.Port);
            string userName = _ftp.UserName ?? string.Empty;
            string password = _ftp.Password ?? string.Empty;
            if (string.Equals(settings.ftpHost, host, StringComparison.Ordinal) &&
                settings.ftpPort == port &&
                string.Equals(settings.ftpUserName, userName, StringComparison.Ordinal) &&
                string.Equals(settings.ftpPassword, password, StringComparison.Ordinal))
                return;

            settings.ftpHost = host;
            settings.ftpPort = port;
            settings.ftpUserName = userName;
            settings.ftpPassword = password;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
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
            _automaticVersioning = EditorPrefs.GetBool(prefix + "AutomaticVersioning", false);
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

            _options.packageVersion = platformVersion?.Trim() ?? string.Empty;
            PlayerSettings.bundleVersion = _options.packageVersion;
            if (ReleasePipeline.GetIntegrationPlatform(target) == IntegrationPlatform.Android &&
                ClientVersion.TryParse(_options.packageVersion, out ClientVersion androidVersion))
                PlayerSettings.Android.bundleVersionCode = androidVersion.AndroidVersionCode;
            _lastObservedPlayerVersion = _options.packageVersion;
            EditorPrefs.SetString(prefix + "PackageVersion", _options.packageVersion);
            EditorPrefs.SetBool(prefix + "PackageVersionInitialized", true);
        }

        private void SaveReleaseSettings(BuildTarget target)
        {
            string prefix = GetReleasePrefsPrefix(target);
            EditorPrefs.SetString(prefix + "Channel", _options.channel ?? string.Empty);
            EditorPrefs.SetString(prefix + "PackageVersion", _options.packageVersion ?? string.Empty);
            EditorPrefs.SetBool(prefix + "PackageVersionInitialized", true);
            EditorPrefs.SetBool(prefix + "DevelopmentBuild", _options.developmentBuild);
            EditorPrefs.SetBool(prefix + "AutomaticVersioning", _automaticVersioning);
            EditorPrefs.SetString(GetProjectPrefsPrefix(ReleasePrefsPrefix) + "OutputRoot",
                _options.outputRoot ?? "Releases");
        }

        private void SynchronizeVersionFromPlayerSettings()
        {
            string playerVersion = PlayerSettings.bundleVersion ?? string.Empty;
            if (string.Equals(playerVersion, _lastObservedPlayerVersion, StringComparison.Ordinal))
                return;
            string previousVersion = _options.packageVersion;
            _options.packageVersion = playerVersion;
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
            string previousVersion = _options.packageVersion;
            _options.packageVersion = version?.Trim() ?? string.Empty;
            PlayerSettings.bundleVersion = _options.packageVersion;
            _lastObservedPlayerVersion = _options.packageVersion;
            if (persist)
                EditorPrefs.SetString(GetReleasePrefsPrefix(_options.target) + "PackageVersion",
                    _options.packageVersion);
            ReplaceTrailingVersion(ref _ftp.HotUpdateDirectory, previousVersion, _options.packageVersion);
            ReplaceTrailingVersion(ref _ftp.ClientDirectory, previousVersion, _options.packageVersion);
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
