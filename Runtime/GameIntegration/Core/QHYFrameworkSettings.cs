using System;
using System.Text.RegularExpressions;
using UnityEngine;
using YooAsset;

namespace GameIntegration
{
    [Serializable]
    public sealed class DistributionRuntimeConfig
    {
        public const int CurrentSchemaVersion = 5;
        public int schemaVersion = CurrentSchemaVersion;
        public string gameDirectory = string.Empty;
        public string platform = string.Empty;
        public string clientVersion = string.Empty;
        public string cdnRoot = string.Empty;
        public string originRoot = string.Empty;

        public void ValidateOrThrow()
        {
            if (schemaVersion != CurrentSchemaVersion) throw new InvalidOperationException($"不支持的 QHY 发布协议版本：{schemaVersion}。");
            DistributionPathResolver.ValidateSegment(gameDirectory, "GameDirectory", true);
            DistributionPathResolver.ValidateSegment(platform, "Platform", false);
            DistributionPathResolver.ValidateSegment(clientVersion, "ClientVersion", false);
            QHYFrameworkSettings.ValidateDistributionRoot(cdnRoot,
                "DistributionRuntimeConfig.cdnRoot", gameDirectory, platform, "cdn");
            QHYFrameworkSettings.ValidateDistributionRoot(originRoot,
                "DistributionRuntimeConfig.originRoot", gameDirectory, platform, "origin");
        }

        public string ClientManifestUrl => DistributionPathResolver.CombineUrl(originRoot, "current/client.json");
    }

    public sealed class DistributionPathResolver
    {
        private static readonly Regex GameIdPattern = new Regex("^[a-z0-9._-]+$", RegexOptions.CultureInvariant);
        private static readonly Regex SegmentPattern = new Regex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant);
        private readonly DistributionRuntimeConfig _config;
        public DistributionPathResolver(DistributionRuntimeConfig config) { _config = config ?? throw new ArgumentNullException(nameof(config)); _config.ValidateOrThrow(); }

        public string ResolveYooAssetUrl(string fileName)
        {
            string name = SafeFileName(fileName);
            if (name.EndsWith(".version", StringComparison.OrdinalIgnoreCase))
                return CombineUrl(_config.originRoot,
                    $"current/{Uri.EscapeDataString(_config.clientVersion)}.version");
            else if (name.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
            {
                string hash = name.Substring(0, name.Length - 7);
                if (hash.Length == 0 || !IsHex(hash)) throw new InvalidOperationException($"YooAsset Bundle 文件名不是内容哈希：{name}");
                return CombineUrl(_config.cdnRoot, $"bundles/{Uri.EscapeDataString(name)}");
            }
            else if (name.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = name.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase) ? ".bytes" : ".hash";
                string stem = name.Substring(0, name.Length - suffix.Length);
                string prefix = QHYFrameworkSettings.PackageName + "_";
                if (!stem.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidOperationException($"无法从 YooAsset 清单文件名解析 ResourceVersion：{name}");
                string resourceVersion = stem.Substring(prefix.Length);
                ValidateSegment(resourceVersion, "ResourceVersion", false);
                return CombineUrl(_config.cdnRoot,
                    $"versions/{Uri.EscapeDataString(_config.clientVersion)}/{Uri.EscapeDataString(resourceVersion)}{suffix}");
            }
            else throw new InvalidOperationException($"QHY schema v5 不允许请求未路由的 YooAsset 文件：{name}");
        }

        public static string GetPlatformSegment(IntegrationPlatform platform)
        {
            switch (platform)
            {
                case IntegrationPlatform.Windows: return "windows";
                case IntegrationPlatform.Android: return "android";
                case IntegrationPlatform.IOS: return "ios";
                case IntegrationPlatform.MacOS: return "macos";
                case IntegrationPlatform.Linux: return "linux64";
                case IntegrationPlatform.WebGL: return "webgl";
                default: throw new InvalidOperationException("当前平台不能映射到 QHY 发布目录。");
            }
        }

        public static void ValidateSegment(string value, string name, bool strictGameId)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { '/', '\\' }) >= 0 || value == "." || value == "..")
                throw new InvalidOperationException($"{name} 不是有效的发布路径段：'{value}'。");
            if (strictGameId && !GameIdPattern.IsMatch(value))
                throw new InvalidOperationException($"{name} 仅允许小写字母、数字、点、短横线和下划线。");
            if (!strictGameId && !SegmentPattern.IsMatch(value))
                throw new InvalidOperationException($"{name} 仅允许字母、数字、点、短横线和下划线。");
        }

        public static string CombineUrl(string root, string relative) => (root ?? string.Empty).Trim().TrimEnd('/') + "/" + (relative ?? string.Empty).TrimStart('/');
        private static string SafeFileName(string value) { string name = (value ?? string.Empty).Replace('\\', '/'); if (string.IsNullOrWhiteSpace(name) || name.Contains("/") || name.Contains("..")) throw new InvalidOperationException($"非法远端文件名：{value}"); return name; }
        private static bool IsHex(string value) { foreach (char c in value) if (!Uri.IsHexDigit(c)) return false; return true; }
    }

    public static class DistributionRuntimeConfigLoader
    {
        public const string ResourceName = "QHYDistributionConfig";
        public static DistributionRuntimeConfig Load(QHYFrameworkSettings settings, EPlayMode playMode)
        {
#if UNITY_EDITOR
            if (playMode != EPlayMode.HostPlayMode && playMode != EPlayMode.WebPlayMode)
            {
                IntegrationPlatform platform = QHYFrameworkSettings.GetCurrentPlatform();
                string gameDirectory = settings.GetGameDirectoryOrThrow();
                string platformRoot = $"https://editor-simulate.invalid/{gameDirectory}/{DistributionPathResolver.GetPlatformSegment(platform)}";
                return new DistributionRuntimeConfig
                {
                    gameDirectory = gameDirectory,
                    platform = DistributionPathResolver.GetPlatformSegment(platform),
                    clientVersion = Application.version,
                    cdnRoot = platformRoot + "/cdn",
                    originRoot = platformRoot + "/origin"
                };
            }
            return settings.CreateRuntimeConfig(QHYFrameworkSettings.GetCurrentPlatform(), Application.version);
#else
            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (!asset) throw new InvalidOperationException("客户端缺少构建生成的 QHYDistributionConfig；请重新执行 FullPackage。");
            DistributionRuntimeConfig config = JsonUtility.FromJson<DistributionRuntimeConfig>(asset.text);
            config?.ValidateOrThrow();
            return config ?? throw new InvalidOperationException("QHYDistributionConfig 内容无效。");
#endif
        }
    }

    public enum CollectorManagementMode
    {
        InitializeOnly,
        ManagedGroupsOnly,
        External
    }

    [Serializable]
    public sealed class HotUpdateAssemblySpec
    {
        public string assemblyName = "Game.HotUpdate";
        public string assetAddress = "Game.HotUpdate.dll";
    }

    [Serializable]
    public sealed class PlatformResourceProfile
    {
        public IntegrationPlatform platform;
        [Tooltip("只填写服务器资源总根；QHY 会自动追加游戏目录、平台目录以及 /cdn、/origin。")]
        public string baseUrl = string.Empty;
    }

    public enum IntegrationPlatform
    {
        Windows,
        Android,
        IOS,
        MacOS,
        Linux,
        WebGL,
        Unknown
    }

    [CreateAssetMenu(fileName = "QHYFrameworkSettings", menuName = "QHY Framework/Settings")]
    public sealed class QHYFrameworkSettings : ScriptableObject
    {
        private static readonly Regex MarkdownUrlPattern = new Regex(
            @"^\[[^\]\r\n]*\]\(\s*(https?://[^()\s]+)\s*\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        public const string DefaultAssetPath = "Assets/Game/Config/QHYFrameworkSettings.asset";
        public const string PackageName = "GamePackage";

        [Header("Platform Resources")]
        [Tooltip("服务器资源总根下的游戏目录。只允许小写字母、数字、点、短横线和下划线。")]
        public string gameDirectory = "game";
        public PlatformResourceProfile[] platformResourceProfiles =
        {
            new PlatformResourceProfile { platform = IntegrationPlatform.Android },
            new PlatformResourceProfile { platform = IntegrationPlatform.Windows }
        };
        public string packageName => PackageName;
        [Tooltip("最近一次编辑器模拟构建目录，仅用于诊断；EditorSimulateMode 启动时会自动重新生成。")]
        public string editorSimulatePackageRoot = "";
        [Min(1)] public int requestTimeoutSeconds = 60;
        [Min(1)] public int downloadConcurrency = 8;
        [Min(0)] public int downloadRetryCount = 2;
        [Min(1)] public int downloadMaxRequestPerFrame = 1;
        [Min(0)] public int downloadWatchdogTimeoutSeconds = 10;
        public bool copyBuiltinPackageManifest = true;
        public bool clearUnusedCacheAfterUpdate = true;
        [Tooltip("HostPlayMode 每次启动请求远端 version 指针，以发现新的 ResourceVersion。不会清理仍被当前 Manifest 使用的 Bundle。")]
        public bool refreshHostManifestEveryStartup = true;
        public bool autoUnloadBundleWhenUnused;

        [Header("Build Collection")]
        [Tooltip("InitializeOnly 仅在 Package 不存在时初始化；ManagedGroupsOnly 只维护带 QHY 标记的组；External 仅校验项目配置。")]
        public CollectorManagementMode collectorManagementMode = CollectorManagementMode.InitializeOnly;
        [Min(0)] public int bundleWarningThresholdMiB = 4;
        [Min(0)] public int bundleErrorThresholdMiB = 16;
        [Tooltip("SBP 不支持 IgnoreTypeTreeChanges。该高级开关仅用于风险报告，默认关闭；启用前必须执行旧客户端兼容测试。")]
        public bool ignoreTypeTreeChangesForIncrementalBuild;

        [Header("Startup")]
        public string startupSceneAddress = "Main";
        public HotUpdateAssemblySpec[] hotUpdateAssemblies = { new HotUpdateAssemblySpec() };
        [HideInInspector]
        public string[] aotMetadataAssemblyNames = Array.Empty<string>();
        [Tooltip("仅用于补充 HybridCLR 自动分析结果；通常保持为空。")]
        public string[] aotMetadataExtraAssemblyNames = Array.Empty<string>();
        [HideInInspector] public string[] aotMetadataAutoAssemblyNames = Array.Empty<string>();
        [HideInInspector] public string aotMetadataAnalysisTarget = "";
        [HideInInspector] public string aotMetadataAnalysisHash = "";
        [HideInInspector] public string aotMetadataAnalysisUtc = "";
        [HideInInspector] public bool aotMetadataAutomationInitialized;

        public string LocalVersionKey =>
            $"GameIntegration.{Application.identifier}.{PackageName}.{GetCurrentPlatform()}.LastGoodVersion";

        public string GetLocalVersionKey(EPlayMode playMode)
        {
            return $"GameIntegration.{Application.identifier}.{PackageName}.{GetCurrentPlatform()}.{playMode}.LastGoodVersion";
        }

        public string GetLocalVersionKey(EPlayMode playMode, DistributionRuntimeConfig distribution)
        {
            if (distribution == null) return GetLocalVersionKey(playMode);
            return $"GameIntegration.{Application.identifier}.{distribution.platform}." +
                   $"{PackageName}.{distribution.clientVersion}.{playMode}.LastGoodVersion";
        }

        public DistributionRuntimeConfig CreateRuntimeConfig(IntegrationPlatform platform, string clientVersion)
        {
            PlatformResourceProfile profile = GetPlatformResourceProfile(platform);
            string baseUrl;
            try
            {
                baseUrl = NormalizeResourceBaseUrl(profile.baseUrl);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"平台 {platform} 的 Resource BaseURL 无效：{exception.Message}", exception);
            }
            string directory = GetGameDirectoryOrThrow();
            if (string.Equals(GetLastPathSegment(new Uri(baseUrl)), directory,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Resource BaseURL 只填写服务器资源总根，不能重复包含 GameDirectory。");
            string gameRoot = DistributionPathResolver.CombineUrl(baseUrl, directory);
            string platformRoot = DistributionPathResolver.CombineUrl(gameRoot,
                DistributionPathResolver.GetPlatformSegment(platform));
            var config = new DistributionRuntimeConfig
            {
                gameDirectory = directory,
                platform = DistributionPathResolver.GetPlatformSegment(platform),
                clientVersion = clientVersion?.Trim(),
                cdnRoot = DistributionPathResolver.CombineUrl(platformRoot, "cdn"),
                originRoot = DistributionPathResolver.CombineUrl(platformRoot, "origin")
            };
            config.ValidateOrThrow();
            return config;
        }

        public string GetGameDirectoryOrThrow()
        {
            string value = (gameDirectory ?? string.Empty).Trim();
            DistributionPathResolver.ValidateSegment(value, "GameDirectory", true);
            return value;
        }

        public PlatformResourceProfile GetPlatformResourceProfile(IntegrationPlatform platform)
        {
            PlatformResourceProfile[] profiles = platformResourceProfiles ?? Array.Empty<PlatformResourceProfile>();
            PlatformResourceProfile match = null;
            foreach (PlatformResourceProfile profile in profiles)
            {
                if (profile == null || profile.platform != platform) continue;
                if (match != null)
                    throw new InvalidOperationException($"平台资源配置重复：{platform}。");
                match = profile;
            }
            return match ?? throw new InvalidOperationException($"缺少平台资源配置：{platform}。");
        }

        public void ValidatePlatformResourceProfilesOrThrow(
            IntegrationPlatform? platformToValidate = null)
        {
            GetGameDirectoryOrThrow();
            PlatformResourceProfile[] profiles = platformResourceProfiles ?? Array.Empty<PlatformResourceProfile>();
            IntegrationPlatform[] configured = Array.ConvertAll(Array.FindAll(profiles,
                profile => profile != null), profile => profile.platform);
            if (Array.Exists(configured, platform => platform == IntegrationPlatform.Unknown))
                throw new InvalidOperationException("平台资源配置不能使用 Unknown。");
            foreach (IntegrationPlatform platform in configured)
            {
                PlatformResourceProfile[] matches = Array.FindAll(profiles,
                    profile => profile != null && profile.platform == platform);
                if (matches.Length > 1)
                    throw new InvalidOperationException($"平台 {platform} 只能配置一个 BaseURL。");
                if (!platformToValidate.HasValue || platform == platformToValidate.Value)
                    NormalizeResourceBaseUrl(matches[0].baseUrl);
            }
        }

        public static string NormalizeResourceBaseUrl(string value)
        {
            string normalized = (value ?? string.Empty)
                .Trim(' ', '\t', '\r', '\n', '\uFEFF', '\u200B', '\u2060');
            Match markdown = MarkdownUrlPattern.Match(normalized);
            if (markdown.Success) normalized = markdown.Groups[1].Value;
            else if (normalized.Length > 1 && normalized[0] == '<' &&
                     normalized[normalized.Length - 1] == '>')
                normalized = normalized.Substring(1, normalized.Length - 2).Trim();

            if (!string.IsNullOrWhiteSpace(normalized) &&
                normalized.IndexOf("://", StringComparison.Ordinal) < 0)
                normalized = "https://" + normalized;
            normalized = normalized.TrimEnd('/');
            ValidateResourceBaseUrl(normalized, "Resource BaseURL");
            return new Uri(normalized).AbsoluteUri.TrimEnd('/');
        }

        public static void ValidateResourceBaseUrl(string value, string name)
        {
            Uri uri = ValidateHttpRoot(value, name);
            string lastSegment = GetLastPathSegment(uri);
            if (string.Equals(lastSegment, "cdn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lastSegment, "origin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{name} 必须指向资源总根，不能以 /cdn 或 /origin 结尾。");
            foreach (IntegrationPlatform platform in (IntegrationPlatform[])Enum.GetValues(typeof(IntegrationPlatform)))
            {
                if (platform == IntegrationPlatform.Unknown) continue;
                if (string.Equals(lastSegment, DistributionPathResolver.GetPlatformSegment(platform),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"{name} 只填写资源总根，不能包含平台目录 /{lastSegment}。");
            }
        }

        public static void ValidateDistributionRoot(string value, string name, string gameDirectory,
            string platform, string expectedFolder)
        {
            Uri uri = ValidateHttpRoot(value, name);
            string expectedSuffix = "/" + gameDirectory + "/" + platform + "/" + expectedFolder;
            string path = uri.AbsolutePath.TrimEnd('/');
            if (!path.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{name} 必须以 {expectedSuffix} 结尾。");
        }

        private static Uri ValidateHttpRoot(string value, string name)
        {
            if (!Uri.TryCreate((value ?? string.Empty).Trim(), UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException(
                    $"{name} 输入 '{value}' 无效。请填写域名（如 res.example.com）或无查询、片段和凭据的 HTTP(S) 根地址。");
            return uri;
        }

        private static string GetLastPathSegment(Uri uri)
        {
            string path = uri.AbsolutePath.Trim('/');
            if (path.Length == 0) return string.Empty;
            string[] segments = path.Split('/');
            return segments[segments.Length - 1];
        }

        public static IntegrationPlatform GetCurrentPlatform()
        {
#if UNITY_EDITOR
            switch (UnityEditor.EditorUserBuildSettings.activeBuildTarget)
            {
                case UnityEditor.BuildTarget.StandaloneWindows:
                case UnityEditor.BuildTarget.StandaloneWindows64: return IntegrationPlatform.Windows;
                case UnityEditor.BuildTarget.Android: return IntegrationPlatform.Android;
                case UnityEditor.BuildTarget.iOS: return IntegrationPlatform.IOS;
                case UnityEditor.BuildTarget.StandaloneOSX: return IntegrationPlatform.MacOS;
                case UnityEditor.BuildTarget.StandaloneLinux64: return IntegrationPlatform.Linux;
                case UnityEditor.BuildTarget.WebGL: return IntegrationPlatform.WebGL;
                default: return IntegrationPlatform.Unknown;
            }
#else
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer: return IntegrationPlatform.Windows;
                case RuntimePlatform.Android: return IntegrationPlatform.Android;
                case RuntimePlatform.IPhonePlayer: return IntegrationPlatform.IOS;
                case RuntimePlatform.OSXPlayer: return IntegrationPlatform.MacOS;
                case RuntimePlatform.LinuxPlayer: return IntegrationPlatform.Linux;
                case RuntimePlatform.WebGLPlayer: return IntegrationPlatform.WebGL;
                default: return IntegrationPlatform.Unknown;
            }
#endif
        }

        public void ValidateOrThrow()
        {
#if UNITY_EDITOR
            ValidateOrThrow(EPlayMode.EditorSimulateMode);
#else
            ValidateOrThrow(EPlayMode.HostPlayMode);
#endif
        }

        public void ValidateOrThrow(EPlayMode playMode)
        {
            GetGameDirectoryOrThrow();
            if (string.IsNullOrWhiteSpace(startupSceneAddress))
                throw new InvalidOperationException("QHYFrameworkSettings.startupSceneAddress 不能为空。");
            if (playMode == EPlayMode.None || playMode == EPlayMode.CustomPlayMode)
                throw new InvalidOperationException($"当前不支持 YooAsset 启动模式：{playMode}。");
#if UNITY_EDITOR
            if (playMode == EPlayMode.HostPlayMode)
                CreateRuntimeConfig(GetCurrentPlatform(), Application.version);
#else
            if (playMode == EPlayMode.EditorSimulateMode)
                throw new InvalidOperationException("EditorSimulateMode 只能在 Unity Editor 中使用。");
#endif
        }
    }
}
