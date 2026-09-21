using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace GameIntegration.Editor
{
    internal static class ReleaseSnapshotMaterializer
    {
        internal static ReleaseUploadPlan MaterializeV3(string sourceRoot, string buildRoot,
            QHYFrameworkSettings settings, ReleaseOptions options, ResourceReleaseBaseline baseline)
        {
            if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException(sourceRoot);
            string reportName = YooAssetConfiguration.GetBuildReportFileName(settings.packageName,
                options.resourceVersion);
            string reportSource = Path.Combine(sourceRoot, reportName);
            if (!File.Exists(reportSource)) throw new FileNotFoundException("YooAsset build report is missing.", reportSource);
            BuildReport report = BuildReport.Deserialize(File.ReadAllText(reportSource));
            string reportsRoot = Path.Combine(buildRoot, "Reports");
            Directory.CreateDirectory(reportsRoot);
            File.Copy(reportSource, Path.Combine(reportsRoot, reportName), false);

            var added = new List<UploadArtifact>();
            var changed = new List<UploadArtifact>();
            var unchanged = new List<UploadArtifact>();
            Dictionary<string, ReportBundleInfo> previousByName = new Dictionary<string, ReportBundleInfo>(
                StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(baseline?.releaseRoot))
            {
                string previousReport = Path.Combine(baseline.releaseRoot, "Reports",
                    YooAssetConfiguration.GetBuildReportFileName(settings.packageName, baseline.resourceVersion));
                if (File.Exists(previousReport))
                    previousByName = BuildReport.Deserialize(File.ReadAllText(previousReport)).BundleInfos
                        .ToDictionary(x => x.BundleName, StringComparer.OrdinalIgnoreCase);
            }
            foreach (ReportBundleInfo bundle in report.BundleInfos.OrderBy(x => x.FileName,
                         StringComparer.OrdinalIgnoreCase))
            {
                string source = Path.Combine(sourceRoot, bundle.FileName);
                string destination = DistributionReleaseLayout.BundlePath(options, bundle.FileName);
                UploadArtifact artifact = MergeImmutable(source, destination, options, true, 10,
                    "immutable-year");
                if (artifact.changeType == "Unchanged") unchanged.Add(artifact);
                else if (previousByName.ContainsKey(bundle.BundleName))
                {
                    artifact.changeType = "Changed";
                    changed.Add(artifact);
                }
                else added.Add(artifact);
            }

            foreach (string extension in new[] { ".bytes", ".hash" })
            {
                string name = settings.packageName + "_" + options.resourceVersion + extension;
                string source = Path.Combine(sourceRoot, name);
                if (!File.Exists(source)) throw new FileNotFoundException("YooAsset manifest artifact is missing.", source);
                UploadArtifact artifact = MergeImmutable(source,
                    DistributionReleaseLayout.VersionPath(options, extension), options,
                    false, 20, "immutable-year");
                (artifact.changeType == "Unchanged" ? unchanged : added).Add(artifact);
            }

            string versionName = settings.packageName + ".version";
            string versionSource = Path.Combine(sourceRoot, versionName);
            string versionDestination = DistributionReleaseLayout.CurrentVersionPath(options);
            Directory.CreateDirectory(Path.GetDirectoryName(versionDestination) ??
                                      DistributionReleaseLayout.PlatformRoot(options));
            File.Copy(versionSource, versionDestination, true);
            UploadArtifact pointer = CreateArtifact(versionDestination, options, false, "Changed", 100,
                "no-cache", true);
            added.Add(pointer);

            WriteReleaseIndex(options);
            string index = DistributionReleaseLayout.IndexPath(options);
            added.Add(CreateArtifact(index, options, false, "Changed", 80, "metadata", false));

            return new ReleaseUploadPlan
            {
                gameDirectory = DistributionReleaseLayout.GameDirectory(options),
                platform = DistributionReleaseLayout.Platform(options),
                clientVersion = options.clientVersion,
                resourceVersion = options.resourceVersion,
                previousResourceVersion = baseline?.resourceVersion ?? string.Empty,
                releasesRoot = DistributionReleaseLayout.ReleasesRoot(options),
                buildRoot = buildRoot,
                stateRoot = DistributionReleaseLayout.StateRoot(options),
                cdnRoot = settings.CreateRuntimeConfig(
                    ReleasePipeline.GetIntegrationPlatform(options.target), options.clientVersion).cdnRoot,
                originRoot = settings.CreateRuntimeConfig(
                    ReleasePipeline.GetIntegrationPlatform(options.target), options.clientVersion).originRoot,
                added = added.ToArray(),
                changed = changed.ToArray(),
                unchanged = unchanged.ToArray(),
                snapshotBytes = report.BundleInfos.Sum(x => x.FileSize),
                uploadBytes = added.Concat(changed).Sum(x => x.length),
                estimatedClientDownloadBytes = report.BundleInfos.Where(x =>
                    !previousByName.TryGetValue(x.BundleName, out ReportBundleInfo old) ||
                    !string.Equals(old.FileName, x.FileName, StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.FileSize)
            };
        }

        private static UploadArtifact MergeImmutable(string source, string destination,
            ReleaseOptions options, bool bundle, int order, string cacheClass)
        {
            if (File.Exists(destination))
            {
                if (new FileInfo(source).Length != new FileInfo(destination).Length ||
                    !string.Equals(BundleDeltaAnalyzer.ComputeSha256(source),
                        BundleDeltaAnalyzer.ComputeSha256(destination), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("禁止覆盖同名不可变发布对象：" + destination);
                return CreateArtifact(destination, options, bundle, "Unchanged", order, cacheClass, false);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException());
            File.Copy(source, destination, false);
            return CreateArtifact(destination, options, bundle, "Added", order, cacheClass, false);
        }

        internal static UploadArtifact CreateArtifact(string path, ReleaseOptions options, bool bundle,
            string changeType, int order, string cacheClass, bool pointer)
        {
            return new UploadArtifact
            {
                relativePath = DistributionReleaseLayout.ReleasesRelative(options, path),
                resourcePath = DistributionReleaseLayout.ResourceRelative(options, path),
                bundleName = bundle ? Path.GetFileName(path) : string.Empty,
                sha256 = BundleDeltaAnalyzer.ComputeSha256(path),
                length = new FileInfo(path).Length,
                changeType = changeType,
                contentAddressedBundle = bundle,
                phase = pointer ? "Publish" : "Files",
                pointer = pointer
            };
        }

        private static void WriteReleaseIndex(ReleaseOptions options)
        {
            string bundlesRoot = Path.Combine(DistributionReleaseLayout.PlatformRoot(options),
                "cdn", "bundles");
            QhyBundleIndexEntry[] entries = Directory.Exists(bundlesRoot)
                ? Directory.GetFiles(bundlesRoot, "*.bundle", SearchOption.TopDirectoryOnly).Select(path =>
                    new QhyBundleIndexEntry
                    {
                        name = Path.GetFileName(path),
                        sha256 = BundleDeltaAnalyzer.ComputeSha256(path),
                        length = new FileInfo(path).Length
                    }).OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<QhyBundleIndexEntry>();
            string path = DistributionReleaseLayout.IndexPath(options);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? DistributionReleaseLayout.ReleasesRoot(options));
            File.WriteAllText(path, UnityEngine.JsonUtility.ToJson(new QhyReleaseIndex { bundles = entries }, true));
        }

    }

    internal static class IncrementalUploadMaterializer
    {
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string newFileName, string existingFileName,
            IntPtr securityAttributes);

        internal static void Synchronize(ReleaseOptions options, ReleaseUploadPlan plan)
        {
            string filesRoot = DistributionReleaseLayout.UploadFilesRoot(options);
            string publishRoot = DistributionReleaseLayout.UploadPublishRoot(options);
            Recreate(filesRoot);
            Recreate(publishRoot);
            UploadArtifact[] artifacts = (plan.added ?? Array.Empty<UploadArtifact>())
                .Concat(plan.changed ?? Array.Empty<UploadArtifact>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.relativePath))
                .GroupBy(item => item.relativePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToArray();
            foreach (UploadArtifact artifact in artifacts)
            {
                string source = Path.Combine(plan.releasesRoot,
                    artifact.relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(source)) throw new FileNotFoundException("Release artifact is missing.", source);
                string root = artifact.pointer ? publishRoot : filesRoot;
                string destination = Path.Combine(root,
                    artifact.relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? root);
                if (artifact.pointer || artifact.resourcePath.EndsWith("origin/qhy.json",
                        StringComparison.OrdinalIgnoreCase) || !TryHardLink(destination, source))
                    File.Copy(source, destination, false);
            }
        }

        private static bool TryHardLink(string destination, string source)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) return false;
            try { return CreateHardLink(destination, source, IntPtr.Zero); }
            catch { return false; }
        }

        private static void Recreate(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Directory.CreateDirectory(path);
        }
    }

    internal sealed class BundleDeltaAnalysis
    {
        public BuildReport CurrentReport;
        public BuildReport PreviousReport;
        public BundleDeltaRecord[] Added = Array.Empty<BundleDeltaRecord>();
        public BundleDeltaRecord[] Changed = Array.Empty<BundleDeltaRecord>();
        public BundleDeltaRecord[] Unchanged = Array.Empty<BundleDeltaRecord>();
        public BundleDeltaRecord[] Removed = Array.Empty<BundleDeltaRecord>();
        public ReleaseUploadPlan UploadPlan;
    }

    internal static class BundleDeltaAnalyzer
    {
        internal static BundleDeltaAnalysis AnalyzeV3(string sourceRoot, string packageName,
            ReleaseOptions options, ResourceReleaseBaseline baseline, ReleaseUploadPlan plan)
        {
            BuildReport current = LoadReport(Path.Combine(sourceRoot,
                YooAssetConfiguration.GetBuildReportFileName(packageName, options.resourceVersion)), true);
            BuildReport previous = null;
            if (!string.IsNullOrWhiteSpace(baseline?.releaseRoot))
            {
                string oldReport = Path.Combine(baseline.releaseRoot, "Reports",
                    YooAssetConfiguration.GetBuildReportFileName(packageName, baseline.resourceVersion));
                previous = LoadReport(oldReport, false);
            }
            var oldByName = (previous?.BundleInfos ?? new List<ReportBundleInfo>())
                .ToDictionary(x => x.BundleName, StringComparer.OrdinalIgnoreCase);
            var currentByName = current.BundleInfos.ToDictionary(x => x.BundleName,
                StringComparer.OrdinalIgnoreCase);
            var added = new List<BundleDeltaRecord>();
            var changed = new List<BundleDeltaRecord>();
            var unchanged = new List<BundleDeltaRecord>();
            foreach (ReportBundleInfo item in current.BundleInfos)
            {
                if (!oldByName.TryGetValue(item.BundleName, out ReportBundleInfo old))
                    added.Add(CreateRecord("Added", null, item, current, false));
                else if (string.Equals(old.FileName, item.FileName, StringComparison.OrdinalIgnoreCase))
                    unchanged.Add(CreateRecord("Unchanged", old, item, current, false));
                else
                    changed.Add(CreateRecord("Changed", old, item, current,
                        HaveSameAssets(old, item) && HaveSameDependencies(old, item)));
            }
            BundleDeltaRecord[] removed = oldByName.Values.Where(x => !currentByName.ContainsKey(x.BundleName))
                .Select(x => CreateRecord("Removed", x, null, previous, false)).ToArray();
            return new BundleDeltaAnalysis
            {
                CurrentReport = current,
                PreviousReport = previous,
                Added = added.ToArray(),
                Changed = changed.ToArray(),
                Unchanged = unchanged.ToArray(),
                Removed = removed,
                UploadPlan = plan
            };
        }

        private static BundleDeltaRecord CreateRecord(string type, ReportBundleInfo previous,
            ReportBundleInfo current, BuildReport report, bool suspectedTypeTree)
        {
            ReportBundleInfo source = current ?? previous;
            string[] mainAssets = (source?.BundleContents ?? new List<EditorAssetInfo>())
                .Select(item => item.AssetPath).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            var mainSet = new HashSet<string>(mainAssets, StringComparer.OrdinalIgnoreCase);
            string[] dependencyAssets = (report?.AssetInfos ?? new List<ReportAssetInfo>())
                .Where(item => mainSet.Contains(item.AssetPath))
                .SelectMany(item => item.DependAssets ?? new List<EditorAssetInfo>())
                .Select(item => item.AssetPath).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            return new BundleDeltaRecord
            {
                bundleName = source?.BundleName ?? string.Empty,
                previousFileName = previous?.FileName ?? string.Empty,
                currentFileName = current?.FileName ?? string.Empty,
                previousSize = previous?.FileSize ?? 0,
                currentSize = current?.FileSize ?? 0,
                changeType = type,
                mainAssets = mainAssets,
                dependencyAssets = dependencyAssets,
                referencedBy = (source?.ReferenceBundles ?? new List<string>()).ToArray(),
                suspectedTypeTreeChange = suspectedTypeTree
            };
        }

        private static bool HaveSameAssets(ReportBundleInfo left, ReportBundleInfo right)
        {
            return left.BundleContents.Select(item => item.AssetPath).OrderBy(value => value)
                .SequenceEqual(right.BundleContents.Select(item => item.AssetPath).OrderBy(value => value),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static bool HaveSameDependencies(ReportBundleInfo left, ReportBundleInfo right)
        {
            return left.DependBundles.OrderBy(value => value).SequenceEqual(
                right.DependBundles.OrderBy(value => value), StringComparer.OrdinalIgnoreCase);
        }

        private static BuildReport LoadReport(string path, bool required)
        {
            if (!File.Exists(path))
            {
                if (required) throw new FileNotFoundException("YooAsset build report is missing.", path);
                return null;
            }
            return BuildReport.Deserialize(File.ReadAllText(path));
        }

        internal static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
