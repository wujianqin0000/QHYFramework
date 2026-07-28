using System;
using UnityEngine;
using UnityEngine.Serialization;
using YooAsset;

namespace GameIntegration
{
    [Serializable]
    public sealed class HotUpdateAssemblySpec
    {
        public string assemblyName = "Game.HotUpdate";
        public string assetAddress = "Game.HotUpdate.dll";
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

    [Serializable]
    public sealed class PlatformRemoteProfile
    {
        public IntegrationPlatform platform;
        [Tooltip("该平台的 CDN 根目录，不要包含资源版本。运行时会自动追加 Application.version。")]
        public string remoteBaseUrl;
        [Tooltip("客户端整包更新根地址，不要包含版本。例如 https://example.com/Client/PC")]
        public string clientUpdateBaseUrl;
    }

    [CreateAssetMenu(fileName = "QHYFrameworkSettings", menuName = "QHY Framework/Settings")]
    public sealed class QHYFrameworkSettings : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/Game/Config/QHYFrameworkSettings.asset";

        [Header("YooAsset")]
        public string packageName = "GamePackage";
        [Tooltip("按 Player 平台选择远端根目录；平台之间完全隔离，避免 Android 误用 PC 清单。")]
        public PlatformRemoteProfile[] platformProfiles = Array.Empty<PlatformRemoteProfile>();
        [FormerlySerializedAs("remoteBaseUrl"), HideInInspector]
        [SerializeField] private string legacyRemoteBaseUrl = "";
        [Tooltip("最近一次编辑器模拟构建目录，仅用于诊断；EditorSimulateMode 启动时会自动重新生成。")]
        public string editorSimulatePackageRoot = "";
        [Min(1)] public int requestTimeoutSeconds = 60;
        [Min(1)] public int downloadConcurrency = 8;
        [Min(0)] public int downloadRetryCount = 2;
        [Min(1)] public int downloadMaxRequestPerFrame = 1;
        [Min(0)] public int downloadWatchdogTimeoutSeconds = 10;
        public bool copyBuiltinPackageManifest = true;
        public bool clearUnusedCacheAfterUpdate = true;
        [Tooltip("HostPlayMode 联网成功后刷新缓存清单，允许同一个 PackageVersion 重复发布内容。不会清理已下载的 Bundle。")]
        public bool refreshHostManifestEveryStartup = true;
        public bool autoUnloadBundleWhenUnused;

#if UNITY_EDITOR
        // Editor-only publishing credentials. These are serialized in the host project's
        // QHYFrameworkSettings asset but are not part of the Player-side class layout.
        [Header("FTP Upload")]
        public string ftpHost = "";
        [Min(1)] public int ftpPort = 21;
        public string ftpUserName = "";
        public string ftpPassword = "";
#endif

        [Header("Startup")]
        public string startupSceneAddress = "Main";
        public HotUpdateAssemblySpec[] hotUpdateAssemblies = { new HotUpdateAssemblySpec() };
        [Tooltip("例如 mscorlib.dll；构建工具会复制为同名 .bytes 地址。")]
        public string[] aotMetadataAssemblyNames = Array.Empty<string>();

        public string LocalVersionKey =>
            $"GameIntegration.{Application.identifier}.{packageName}.{GetCurrentPlatform()}.LastGoodVersion";

        public string GetLocalVersionKey(EPlayMode playMode)
        {
            return $"GameIntegration.{Application.identifier}.{packageName}.{GetCurrentPlatform()}.{playMode}.LastGoodVersion";
        }

        public string GetLegacyLocalVersionKey(EPlayMode playMode)
        {
            return $"GameIntegration.{Application.identifier}.{packageName}.{playMode}.LastGoodVersion";
        }

        public string GetRemoteBaseUrl(IntegrationPlatform platform)
        {
            foreach (PlatformRemoteProfile profile in platformProfiles ?? Array.Empty<PlatformRemoteProfile>())
            {
                if (profile != null && profile.platform == platform)
                    return profile.remoteBaseUrl?.Trim() ?? string.Empty;
            }

            // 只为已有项目的 Windows 配置提供迁移回退；其它平台必须显式配置，避免串平台。
            return platform == IntegrationPlatform.Windows ? legacyRemoteBaseUrl?.Trim() ?? string.Empty : string.Empty;
        }

        public string GetRemoteBaseUrlForCurrentPlatform()
        {
            return GetRemoteBaseUrl(GetCurrentPlatform());
        }

        public string GetRemotePackageUrl(IntegrationPlatform platform, string resourceVersion)
        {
            string root = GetRemoteBaseUrl(platform).TrimEnd('/');
            string version = resourceVersion?.Trim().Trim('/') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(version))
                return string.Empty;
            return $"{root}/{Uri.EscapeDataString(version)}";
        }

        public string GetRemotePackageUrlForCurrentPlatform()
        {
            return GetRemotePackageUrl(GetCurrentPlatform(), Application.version);
        }

        public string GetClientUpdateBaseUrl(IntegrationPlatform platform)
        {
            foreach (PlatformRemoteProfile profile in platformProfiles ?? Array.Empty<PlatformRemoteProfile>())
            {
                if (profile != null && profile.platform == platform)
                    return profile.clientUpdateBaseUrl?.Trim().TrimEnd('/') ?? string.Empty;
            }
            return string.Empty;
        }

        public string GetClientManifestUrl(IntegrationPlatform platform)
        {
            string root = GetClientUpdateBaseUrl(platform);
            return string.IsNullOrWhiteSpace(root) ? string.Empty : root + "/latest.json";
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
            if (string.IsNullOrWhiteSpace(packageName))
                throw new InvalidOperationException("QHYFrameworkSettings.packageName 不能为空。");
            if (string.IsNullOrWhiteSpace(startupSceneAddress))
                throw new InvalidOperationException("QHYFrameworkSettings.startupSceneAddress 不能为空。");
            if (playMode == EPlayMode.None || playMode == EPlayMode.CustomPlayMode)
                throw new InvalidOperationException($"当前不支持 YooAsset 启动模式：{playMode}。");
            if (playMode == EPlayMode.HostPlayMode && string.IsNullOrWhiteSpace(GetRemoteBaseUrlForCurrentPlatform()))
                throw new InvalidOperationException($"HostPlayMode 缺少 {GetCurrentPlatform()} 平台的远端 URL 配置。");
#if !UNITY_EDITOR
            if (playMode == EPlayMode.EditorSimulateMode)
                throw new InvalidOperationException("EditorSimulateMode 只能在 Unity Editor 中使用。");
#endif
        }
    }
}
