using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace GameIntegration.Editor
{
    public sealed class EngineStrippingAnalysis
    {
        public string[] CollectedAssetPaths = Array.Empty<string>();
        public string[] AnimationAssetPaths = Array.Empty<string>();
        public string[] RequiredAssemblies = Array.Empty<string>();
        public bool RequiresAnimationModule => RequiredAssemblies.Contains(
            EngineStrippingLinkerAutomation.AnimationModule, StringComparer.OrdinalIgnoreCase);
    }

    internal static class EngineStrippingLinkerAutomation
    {
        internal const string AnimationModule = "UnityEngine.AnimationModule";
        private static readonly string[] AnimationExtensions =
        {
            ".anim", ".controller", ".overridecontroller", ".mask"
        };

        internal static EngineStrippingAnalysis Analyze(QHYFrameworkSettings settings)
        {
            if (!settings) throw new ArgumentNullException(nameof(settings));
            CollectResult collected = BundleCollectorSettingData.Setting.BeginCollect(
                settings.packageName, false, true);
            var assets = (collected.CollectAssets ?? new List<CollectAssetInfo>())
                .SelectMany(item => new[] { item.AssetInfo }.Concat(item.DependAssets ??
                    new List<EditorAssetInfo>()))
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.AssetPath) &&
                               item.AssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => item.AssetPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).OrderBy(item => item.AssetPath,
                    StringComparer.OrdinalIgnoreCase).ToArray();
            string[] animationAssets = assets.Where(item => IsAnimationAsset(item.AssetPath,
                    item.AssetType, HasImportedAnimationClips(item.AssetPath)))
                .Select(item => item.AssetPath).ToArray();
            return new EngineStrippingAnalysis
            {
                CollectedAssetPaths = assets.Select(item => item.AssetPath).ToArray(),
                AnimationAssetPaths = animationAssets,
                RequiredAssemblies = animationAssets.Length > 0
                    ? new[] { AnimationModule }
                    : Array.Empty<string>()
            };
        }

        internal static EngineStrippingAnalysis Synchronize(QHYFrameworkSettings settings)
        {
            EngineStrippingAnalysis analysis = Analyze(settings);
            string xml = BuildLinkXml(analysis.RequiredAssemblies);
            string directory = Path.GetDirectoryName(IntegrationProjectPaths.GeneratedQhyLinkXml)
                               ?? IntegrationProjectPaths.GeneratedRoot;
            Directory.CreateDirectory(directory);
            File.WriteAllText(IntegrationProjectPaths.GeneratedQhyLinkXml, xml,
                new UTF8Encoding(false));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(IntegrationProjectPaths.GeneratedQhyLinkXml,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Debug.Log($"[QHYFramework] Engine stripping protection generated: " +
                      $"{IntegrationProjectPaths.GeneratedQhyLinkXml}; assemblies: " +
                      $"[{string.Join(", ", analysis.RequiredAssemblies)}]; external animation assets: " +
                      analysis.AnimationAssetPaths.Length);
            return analysis;
        }

        internal static IReadOnlyList<string> ValidateProtection(EngineStrippingAnalysis analysis,
            bool stripEngineCode, string linkXml = null)
        {
            if (!stripEngineCode || analysis == null || !analysis.RequiresAnimationModule)
                return Array.Empty<string>();
            string xml = linkXml;
            if (xml == null && File.Exists(IntegrationProjectPaths.GeneratedQhyLinkXml))
                xml = File.ReadAllText(IntegrationProjectPaths.GeneratedQhyLinkXml);
            if (PreservesAssembly(xml, AnimationModule))
                return Array.Empty<string>();
            return new[]
            {
                L("外置 YooAsset 动画资源需要 UnityEngine.AnimationModule，但当前 Player 裁剪配置未完整保留该模块，Android IL2CPP 构建后动画可能无法播放。请重新执行 FullPackage 构建以生成 Assets/Game/Generated/QHYLink/link.xml。",
                    "External YooAsset animation assets require UnityEngine.AnimationModule, but the current Player stripping configuration does not fully preserve it. Animation may fail after an Android IL2CPP build. Run FullPackage Build again to generate Assets/Game/Generated/QHYLink/link.xml.")
            };
        }

        internal static bool IsAnimationAsset(string assetPath, Type assetType,
            bool importedModelHasAnimationClips = false)
        {
            if (assetType != null && (typeof(AnimationClip).IsAssignableFrom(assetType) ||
                                      typeof(RuntimeAnimatorController).IsAssignableFrom(assetType) ||
                                      typeof(AnimatorOverrideController).IsAssignableFrom(assetType) ||
                                      typeof(Motion).IsAssignableFrom(assetType) ||
                                      typeof(Avatar).IsAssignableFrom(assetType) ||
                                      typeof(AvatarMask).IsAssignableFrom(assetType)))
                return true;
            string extension = Path.GetExtension(assetPath ?? string.Empty);
            return AnimationExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
                   importedModelHasAnimationClips;
        }

        internal static string BuildLinkXml(IEnumerable<string> assemblies)
        {
            string[] names = (assemblies ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            var linker = new XElement("linker", names.Select(name =>
                new XElement("assembly", new XAttribute("fullname", name),
                    new XAttribute("preserve", "all"))));
            return new XDocument(new XDeclaration("1.0", "utf-8", null), linker)
                .ToString() + Environment.NewLine;
        }

        internal static bool PreservesAssembly(string xml, string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(assemblyName))
                return false;
            try
            {
                XDocument document = XDocument.Parse(xml);
                return document.Descendants("assembly").Any(element =>
                    string.Equals((string)element.Attribute("fullname"), assemblyName,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string)element.Attribute("preserve"), "all",
                        StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private static bool HasImportedAnimationClips(string assetPath)
        {
            if (!(AssetImporter.GetAtPath(assetPath) is ModelImporter importer))
                return false;
            return (importer.clipAnimations?.Length ?? 0) > 0 ||
                   (importer.defaultClipAnimations?.Length ?? 0) > 0;
        }

        private static string L(string chinese, string english)
        {
            return EditorLocalization.Text(chinese, english);
        }
    }

    public static class ReleaseValidation
    {
        public static IReadOnlyList<string> Validate(QHYFrameworkSettings settings, bool requireGeneratedFiles,
            BuildTarget target = BuildTarget.NoTarget, IEnumerable<string> effectiveAotMetadata = null,
            EngineStrippingAnalysis engineStrippingAnalysis = null)
        {
            var errors = new List<string>();
            if (!settings)
                return new[] { L("QHYFrameworkSettings 不存在。", "QHYFrameworkSettings does not exist.") };
            if (target == BuildTarget.NoTarget)
                target = EditorUserBuildSettings.activeBuildTarget;
            IntegrationPlatform platform = ReleasePipeline.GetIntegrationPlatform(target);
            try
            {
                settings.ValidatePlatformResourceProfilesOrThrow(platform);
                settings.CreateRuntimeConfig(platform, PlayerSettings.bundleVersion);
            }
            catch (Exception exception) { errors.Add(exception.Message); }
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
                foreach (string name in effectiveAotMetadata ??
                                        settings.aotMetadataAssemblyNames ?? Array.Empty<string>())
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
            if (engineStrippingAnalysis != null)
                errors.AddRange(EngineStrippingLinkerAutomation.ValidateProtection(
                    engineStrippingAnalysis, PlayerSettings.stripEngineCode));
            return errors.Distinct().ToArray();
        }

        public static void ThrowIfInvalid(QHYFrameworkSettings settings, bool requireGeneratedFiles,
            BuildTarget target = BuildTarget.NoTarget, IEnumerable<string> effectiveAotMetadata = null,
            EngineStrippingAnalysis engineStrippingAnalysis = null)
        {
            IReadOnlyList<string> errors = Validate(settings, requireGeneratedFiles, target,
                effectiveAotMetadata, engineStrippingAnalysis);
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
