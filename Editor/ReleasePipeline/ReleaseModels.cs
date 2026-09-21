using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace GameIntegration.Editor
{
    public enum ReleaseMode
    {
        FullPackage,
        HotUpdateOnly
    }

    [Serializable]
    public sealed class ReleaseOptions
    {
        public string gameDirectory = "game";
        public string clientVersion = "";
        public string resourceVersion = "";
        public ReleaseMode mode = ReleaseMode.FullPackage;
        public BuildTarget target = BuildTarget.StandaloneWindows64;
        public bool developmentBuild;
        public string outputRoot = "Releases";

        [NonSerialized]
        public bool automaticResourceVersion;
        [NonSerialized]
        public string buildRootOverride;
        [NonSerialized]
        public string stateRootOverride;

        // Editor-only, one-shot authorization. It is deliberately not persisted and is left null
        // by command-line builds so an unattended release can never bypass an AOT mismatch.
        [NonSerialized]
        public Func<string, bool> confirmForceAotMetadataPublish;

    }

    [Serializable]
    public sealed class UploadArtifact
    {
        public string resourcePath;
        public string relativePath;
        public string bundleName;
        public string sha256;
        public long length;
        public string changeType;
        public bool contentAddressedBundle;
        public string phase;
        public bool pointer;
    }

    [Serializable]
    public sealed class ReleaseUploadPlan
    {
        public int schemaVersion = DistributionRuntimeConfig.CurrentSchemaVersion;
        public string gameDirectory = "game";
        public string platform;
        public string clientVersion;
        public string resourceVersion;
        public string previousResourceVersion;
        public string releasesRoot;
        public string buildRoot;
        public string stateRoot;
        public string cdnRoot;
        public string originRoot;
        public string stage = "Built";
        public bool rollback;
        public UploadArtifact[] added = Array.Empty<UploadArtifact>();
        public UploadArtifact[] changed = Array.Empty<UploadArtifact>();
        public UploadArtifact[] unchanged = Array.Empty<UploadArtifact>();
        public UploadArtifact[] removed = Array.Empty<UploadArtifact>();
        public long snapshotBytes;
        public long uploadBytes;
        public long estimatedClientDownloadBytes;
    }

    internal sealed class ReleaseRollbackTarget
    {
        public string ResourceVersion;
        public string ReleaseRoot;
        public ReleaseUploadPlan Plan;
    }

    [Serializable]
    public sealed class BundleDeltaRecord
    {
        public string bundleName;
        public string previousFileName;
        public string currentFileName;
        public long previousSize;
        public long currentSize;
        public string changeType;
        public string[] mainAssets = Array.Empty<string>();
        public string[] dependencyAssets = Array.Empty<string>();
        public string[] referencedBy = Array.Empty<string>();
        public bool suspectedTypeTreeChange;
    }

    [Serializable]
    public sealed class ReleaseReportData
    {
        public int schemaVersion = DistributionRuntimeConfig.CurrentSchemaVersion;
        public string gameDirectory;
        public string version;
        public string resourceVersion;
        public string previousResourceVersion;
        public string platform;
        public string mode;
        public string unityVersion;
        public string clientVersion;
        public string cdnRoot;
        public string originRoot;
        public string clientManifestUrl;
        public string timestampUtc;
        public string aotBaselineSha256;
        public string[] aotMetadataAssemblies;
        public string[] artifacts;
        public long snapshotBytes;
        public long uploadBytes;
        public long estimatedClientDownloadBytes;
        public int bundleCount;
        public int addedBundleCount;
        public int changedBundleCount;
        public int unchangedBundleCount;
        public int removedBundleCount;
        public bool hotUpdateDllChanged;
        public bool aotMetadataChanged;
        public bool aotMetadataBundleChanged;
        public bool aotMetadataPayloadMatched;
        public bool aotMetadataForcePublished;
        public string aotMetadataMismatchReason;
        public bool aotClientBaselineMatched;
        public bool stripEngineCode;
        public string engineStrippingLinkPath;
        public string[] engineStrippingProtectedAssemblies = Array.Empty<string>();
        public int externalAnimationAssetCount;
        public string[] externalAnimationAssets = Array.Empty<string>();
        public BundleDeltaRecord[] addedBundles = Array.Empty<BundleDeltaRecord>();
        public BundleDeltaRecord[] changedBundles = Array.Empty<BundleDeltaRecord>();
        public BundleDeltaRecord[] unchangedBundles = Array.Empty<BundleDeltaRecord>();
        public BundleDeltaRecord[] removedBundles = Array.Empty<BundleDeltaRecord>();
    }

    internal sealed class AotMetadataPublishBlockedException : InvalidOperationException
    {
        public AotMetadataPublishBlockedException(string message) : base(message)
        {
        }
    }

    internal sealed class NoReleaseContentChangesException : InvalidOperationException
    {
        public NoReleaseContentChangesException(string message) : base(message)
        {
        }
    }

    internal sealed class ReleaseAlreadyPublishedException : InvalidOperationException
    {
        public ReleaseAlreadyPublishedException(string message) : base(message)
        {
        }
    }

    internal static class ReleaseContentChangeDetector
    {
        internal static bool HasChanges(BundleDeltaAnalysis delta)
        {
            return delta != null && ((delta.Added?.Length ?? 0) > 0 ||
                                     (delta.Changed?.Length ?? 0) > 0 ||
                                     (delta.Removed?.Length ?? 0) > 0);
        }

        internal static bool HasChanges(ReleaseUploadPlan plan)
        {
            if (plan == null) return false;
            return (plan.added ?? Array.Empty<UploadArtifact>())
                       .Concat(plan.changed ?? Array.Empty<UploadArtifact>())
                       .Any(item => item.contentAddressedBundle) ||
                   (plan.removed?.Length ?? 0) > 0;
        }
    }

    internal static class DistributionReleaseLayout
    {
        internal static string ReleasesRoot(ReleaseOptions options) => Path.GetFullPath(options.outputRoot);
        internal static string PlatformRoot(ReleaseOptions options) =>
            Path.Combine(ReleasesRoot(options), GameDirectory(options), Platform(options));
        internal static string BuildRoot(ReleaseOptions options) => Path.GetFullPath(Path.Combine(
            string.IsNullOrWhiteSpace(options.buildRootOverride) ? "QHYBuilds" : options.buildRootOverride,
            Platform(options), options.clientVersion, options.resourceVersion));
        internal static string StateRoot(ReleaseOptions options) => string.IsNullOrWhiteSpace(options.stateRootOverride)
            ? Path.GetFullPath(Path.Combine("ProjectSettings", "QHYFramework", "ReleaseState", Platform(options)))
            : Path.GetFullPath(options.stateRootOverride);
        internal static string BundlePath(ReleaseOptions options, string bundleFileName) =>
            Path.Combine(PlatformRoot(options), "cdn", "bundles", bundleFileName);
        internal static string VersionPath(ReleaseOptions options, string extension) =>
            Path.Combine(PlatformRoot(options), "cdn", "versions", options.clientVersion,
                options.resourceVersion + extension);
        internal static string CurrentVersionPath(ReleaseOptions options) =>
            Path.Combine(PlatformRoot(options), "origin", "current", options.clientVersion + ".version");
        internal static string ClientPath(ReleaseOptions options, string extension) =>
            Path.Combine(PlatformRoot(options), "cdn", "clients", options.clientVersion + extension);
        internal static string ClientManifestPath(ReleaseOptions options) =>
            Path.Combine(PlatformRoot(options), "origin", "current", "client.json");
        internal static string IndexPath(ReleaseOptions options) =>
            Path.Combine(PlatformRoot(options), "origin", "qhy.json");
        internal static string UploadFilesRoot(ReleaseOptions options) =>
            Path.Combine(BuildRoot(options), "Upload", "1-Files");
        internal static string UploadPublishRoot(ReleaseOptions options) =>
            Path.Combine(BuildRoot(options), "Upload", "2-Publish");
        internal static bool TryGetExistingUploadRoot(ReleaseOptions options, bool publish,
            out string path)
        {
            path = string.Empty;
            if (options == null || string.IsNullOrWhiteSpace(options.clientVersion) ||
                string.IsNullOrWhiteSpace(options.resourceVersion)) return false;
            try
            {
                string buildRoot = BuildRoot(options);
                string uploadRoot = Path.GetFullPath(Path.Combine(buildRoot, "Upload"));
                string candidate = Path.GetFullPath(publish
                    ? UploadPublishRoot(options)
                    : UploadFilesRoot(options));
                string prefix = uploadRoot.TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
                path = candidate;
                return Directory.Exists(candidate);
            }
            catch
            {
                path = string.Empty;
                return false;
            }
        }
        internal static string Platform(ReleaseOptions options) =>
            DistributionPathResolver.GetPlatformSegment(ReleasePipeline.GetIntegrationPlatform(options.target));
        internal static string GameDirectory(ReleaseOptions options)
        {
            string value = (options?.gameDirectory ?? string.Empty).Trim();
            DistributionPathResolver.ValidateSegment(value, "GameDirectory", true);
            return value;
        }
        internal static string ReleasesRelative(ReleaseOptions options, string absolutePath) =>
            Relative(ReleasesRoot(options), absolutePath);
        internal static string ResourceRelative(ReleaseOptions options, string absolutePath) =>
            Relative(PlatformRoot(options), absolutePath);
        internal static string Relative(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path is outside release root: " + full);
            return full.Substring(prefix.Length).Replace('\\', '/');
        }

        internal static void ValidateCleanOrV5(ReleaseOptions options)
        {
            string releases = ReleasesRoot(options);
            foreach (string legacy in new[] { "Server", "Builds", "State", "Baselines", "Revisions",
                         "SharedBundles", "CDN", "ClientUpdate" })
            {
                if (Directory.Exists(Path.Combine(releases, legacy)))
                    throw new InvalidOperationException($"检测到旧发布结构 {legacy}。QHY schema v5 不兼容旧版本，请手工清空 Releases、QHYBuilds、旧发布状态和服务器资源根后重新 FullPackage。");
            }
            foreach (string legacyPlatform in new[] { "android", "windows", "ios", "macos", "linux64", "webgl" })
            {
                if (Directory.Exists(Path.Combine(releases, legacyPlatform)))
                    throw new InvalidOperationException($"检测到旧的根目录直出平台结构 {legacyPlatform}。schema v5 要求 Releases/{{游戏资源目录}}/{{平台}}，请手工清空旧产物后重新 FullPackage。");
            }
            string statePath = Path.Combine(StateRoot(options), "publication-state.json");
            if (File.Exists(statePath))
            {
                QhyReleaseIndex stateHeader = JsonUtility.FromJson<QhyReleaseIndex>(File.ReadAllText(statePath));
                if (stateHeader == null || stateHeader.schemaVersion != DistributionRuntimeConfig.CurrentSchemaVersion)
                    throw new InvalidOperationException("检测到旧发布状态。QHY schema v5 不兼容旧版本，请手工清空 ProjectSettings/QHYFramework/ReleaseState 后重新 FullPackage。");
            }
            string platformRoot = PlatformRoot(options);
            string indexPath = IndexPath(options);
            if (File.Exists(indexPath))
            {
                QhyReleaseIndex index = JsonUtility.FromJson<QhyReleaseIndex>(File.ReadAllText(indexPath));
                if (index == null || index.schemaVersion != DistributionRuntimeConfig.CurrentSchemaVersion)
                    throw new InvalidOperationException("本地 Releases 不是 QHY schema v5，请手工清空后重新 FullPackage。");
                return;
            }
            if (Directory.Exists(platformRoot) && Directory.EnumerateFileSystemEntries(platformRoot).Any())
                throw new InvalidOperationException("游戏/平台 Releases 非空但缺少 schema v5 origin/qhy.json，请手工清空后重新 FullPackage。");
            if (options.mode != ReleaseMode.FullPackage)
                throw new InvalidOperationException("schema v5 尚未建立 FullPackage 基线，HotUpdateOnly 已禁用。");
        }
    }

    [Serializable]
    internal sealed class QhyReleaseIndex
    {
        public int schemaVersion = DistributionRuntimeConfig.CurrentSchemaVersion;
        public string updatedAtUtc = DateTime.UtcNow.ToString("O");
        public QhyBundleIndexEntry[] bundles = Array.Empty<QhyBundleIndexEntry>();
    }

    [Serializable]
    internal sealed class QhyBundleIndexEntry
    {
        public string name;
        public string sha256;
        public long length;
    }

    internal static class DistributionRuntimeConfigGenerator
    {
        internal static DistributionRuntimeConfig Synchronize(QHYFrameworkSettings settings, ReleaseOptions options)
        {
            DistributionRuntimeConfig config = settings.CreateRuntimeConfig(
                ReleasePipeline.GetIntegrationPlatform(options.target), options.clientVersion);
            Directory.CreateDirectory(IntegrationProjectPaths.GeneratedDistributionResources);
            File.WriteAllText(IntegrationProjectPaths.GeneratedDistributionConfig,
                JsonUtility.ToJson(config, true), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(IntegrationProjectPaths.GeneratedDistributionConfig,
                ImportAssetOptions.ForceSynchronousImport);
            return config;
        }
    }
}
