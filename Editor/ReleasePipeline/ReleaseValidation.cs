using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using YooAsset.Editor;

namespace GameIntegration.Editor
{
    public static class ReleaseValidation
    {
        public static IReadOnlyList<string> Validate(QHYFrameworkSettings settings, bool requireGeneratedFiles,
            BuildTarget target = BuildTarget.NoTarget)
        {
            var errors = new List<string>();
            if (!settings)
                return new[] { L("QHYFrameworkSettings 不存在。", "QHYFrameworkSettings does not exist.") };
            if (target == BuildTarget.NoTarget)
                target = EditorUserBuildSettings.activeBuildTarget;
            IntegrationPlatform platform = ReleasePipeline.GetIntegrationPlatform(target);
            string remoteUrl = settings.GetRemoteBaseUrl(platform);
            if (string.IsNullOrWhiteSpace(remoteUrl))
                errors.Add(F("HostPlayMode 缺少 {0} 平台的远端 URL 配置。",
                    "HostPlayMode is missing a remote URL for platform {0}.", platform));
            else if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri remoteUri) ||
                     (remoteUri.Scheme != Uri.UriSchemeHttp && remoteUri.Scheme != Uri.UriSchemeHttps))
                errors.Add(F("{0} 平台的远端 URL 无效：{1}",
                    "The remote URL for platform {0} is invalid: {1}", platform, remoteUrl));
            else
            {
                string version = PlayerSettings.bundleVersion?.Trim().Trim('/') ?? string.Empty;
                if (!string.IsNullOrEmpty(version) && remoteUri.AbsolutePath.TrimEnd('/')
                        .EndsWith("/" + version, StringComparison.OrdinalIgnoreCase))
                    errors.Add(F("{0} 平台的远端根地址不应包含资源版本 {1}：{2}",
                        "The remote base URL for platform {0} must not include package version {1}: {2}",
                        platform, version, remoteUrl));
                string packageUrl = settings.GetRemotePackageUrl(platform, version);
                if (!Uri.TryCreate(packageUrl, UriKind.Absolute, out _))
                    errors.Add(F("{0} 平台自动拼接后的远端资源地址无效：{1}",
                        "The generated remote package URL for platform {0} is invalid: {1}",
                        platform, packageUrl));
            }

            if (platform == IntegrationPlatform.Windows || platform == IntegrationPlatform.Android)
            {
                string clientUrl = settings.GetClientUpdateBaseUrl(platform);
                if (!Uri.TryCreate(clientUrl, UriKind.Absolute, out Uri clientUri) ||
                    (clientUri.Scheme != Uri.UriSchemeHttp && clientUri.Scheme != Uri.UriSchemeHttps))
                    errors.Add(F("{0} 平台的客户端更新根地址无效：{1}",
                        "The client update base URL for platform {0} is invalid: {1}", platform, clientUrl));
            }

            var duplicatePlatforms = (settings.platformProfiles ?? Array.Empty<PlatformRemoteProfile>())
                .Where(profile => profile != null)
                .GroupBy(profile => profile.platform)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key.ToString()).ToArray();
            if (duplicatePlatforms.Length > 0)
                errors.Add(L("平台远端配置重复：", "Duplicate platform remote profiles: ") +
                           string.Join(", ", duplicatePlatforms));
            if (EditorBuildSettings.scenes.Length != 1 ||
                EditorBuildSettings.scenes[0].path != IntegrationProjectPaths.BootScene ||
                !EditorBuildSettings.scenes[0].enabled)
                errors.Add(F("Build Settings 必须只启用 {0}。",
                    "Build Settings must contain only {0}.", IntegrationProjectPaths.BootScene));
            if (!File.Exists(IntegrationProjectPaths.MainScene))
                errors.Add(F("缺少 YooAsset 业务场景 {0}。",
                    "Missing YooAsset game scene: {0}.", IntegrationProjectPaths.MainScene));
            if (!File.Exists(BootUIPrefabGenerator.PrefabPath))
                errors.Add(F("缺少 Boot UGUI Prefab：{0}",
                    "Missing Boot UGUI prefab: {0}", BootUIPrefabGenerator.PrefabPath));
            if (requireGeneratedFiles)
            {
                foreach (HotUpdateAssemblySpec spec in settings.hotUpdateAssemblies ?? Array.Empty<HotUpdateAssemblySpec>())
                {
                    string path = $"{IntegrationProjectPaths.GeneratedHotUpdate}/{spec.assemblyName}.dll.bytes";
                    if (!File.Exists(path)) errors.Add(F("缺少热更 DLL：{0}",
                        "Missing hot-update DLL: {0}", path));
                }
                foreach (string name in settings.aotMetadataAssemblyNames ?? Array.Empty<string>())
                {
                    string fileName = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll";
                    string path = $"{IntegrationProjectPaths.GeneratedAotMetadata}/{fileName}.bytes";
                    if (!File.Exists(path)) errors.Add(F("缺少 AOT 元数据：{0}",
                        "Missing AOT metadata: {0}", path));
                }
            }

            try
            {
                BundleCollectorSettingData.Setting.BeginCollect(settings.packageName, true, false);
            }
            catch (Exception exception)
            {
                errors.Add(L("YooAsset 收集配置无效或存在重复短地址：",
                               "YooAsset collector configuration is invalid or contains duplicate addresses: ") +
                           exception.GetBaseException().Message);
            }
            return errors.Distinct().ToArray();
        }

        public static void ThrowIfInvalid(QHYFrameworkSettings settings, bool requireGeneratedFiles,
            BuildTarget target = BuildTarget.NoTarget)
        {
            IReadOnlyList<string> errors = Validate(settings, requireGeneratedFiles, target);
            if (errors.Count > 0)
                throw new InvalidOperationException(L("发布校验失败：\n- ",
                    "Release validation failed:\n- ") + string.Join("\n- ", errors));
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
