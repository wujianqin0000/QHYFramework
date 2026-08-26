using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using YooAsset;
using YooAsset.Editor;

namespace GameIntegration.Editor
{
    internal static class ReleaseSnapshotMaterializer
    {
        private const string SharedBundleDirectoryName = "SharedBundles";

        internal static void Materialize(string sourceRoot, string cdnRoot, string clientVersionRoot,
            string packageName, string resourceVersion)
        {
            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException("YooAsset package output is missing: " + sourceRoot);

            string reportPath = Path.Combine(sourceRoot,
                YooAssetConfiguration.GetBuildReportFileName(packageName, resourceVersion));
            if (!File.Exists(reportPath))
                throw new FileNotFoundException("YooAsset build report is missing.", reportPath);

            BuildReport report = BuildReport.Deserialize(File.ReadAllText(reportPath));
            MaterializeFiles(sourceRoot, cdnRoot, clientVersionRoot,
                (report?.BundleInfos ?? new List<ReportBundleInfo>()).Select(item => item.FileName));
        }

        internal static void MaterializeFiles(string sourceRoot, string cdnRoot, string clientVersionRoot,
            IEnumerable<string> bundleFileNames)
        {
            var bundleFiles = new HashSet<string>(bundleFileNames ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            string sharedRoot = Path.Combine(clientVersionRoot, SharedBundleDirectoryName);
            Directory.CreateDirectory(cdnRoot);

            foreach (string source in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relative = source.Substring(sourceRoot.Length).TrimStart(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destination = Path.Combine(cdnRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? cdnRoot);
                if (!bundleFiles.Contains(Path.GetFileName(source)))
                {
                    File.Copy(source, destination, true);
                    continue;
                }

                string shared = Path.Combine(sharedRoot, Path.GetFileName(source));
                EnsureSharedBundle(source, shared);
                if (!TryCreateHardLink(destination, shared))
                    File.Copy(shared, destination, true);
            }
        }

        private static void EnsureSharedBundle(string source, string shared)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shared) ?? throw new InvalidOperationException(
                "Shared bundle directory is invalid."));
            if (File.Exists(shared))
            {
                if (new FileInfo(source).Length != new FileInfo(shared).Length ||
                    !string.Equals(BundleDeltaAnalyzer.ComputeSha256(source),
                        BundleDeltaAnalyzer.ComputeSha256(shared), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Shared content-addressed bundle collision: '{Path.GetFileName(shared)}'.");
                return;
            }

            string temporary = shared + ".copying";
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                File.Copy(source, temporary, false);
                File.Move(temporary, shared);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static bool TryCreateHardLink(string destination, string existing)
        {
            if (File.Exists(destination)) File.Delete(destination);
            PlatformID platform = Environment.OSVersion.Platform;
            if (platform != PlatformID.Win32NT && platform != PlatformID.Win32Windows)
                return false;
            try
            {
                return CreateHardLink(destination, existing, IntPtr.Zero);
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(string newFileName, string existingFileName,
            IntPtr securityAttributes);
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
        internal static BundleDeltaAnalysis Analyze(string cdnRoot, string packageName,
            ReleaseOptions options, ResourceReleaseBaseline baseline)
        {
            string currentReportPath = Path.Combine(cdnRoot,
                YooAssetConfiguration.GetBuildReportFileName(packageName, options.resourceVersion));
            BuildReport current = LoadReport(currentReportPath, true);
            string previousCdn = baseline == null ? string.Empty : Path.Combine(baseline.releaseRoot, "CDN");
            BuildReport previous = baseline == null ? null : LoadReport(Path.Combine(previousCdn,
                YooAssetConfiguration.GetBuildReportFileName(packageName, baseline.resourceVersion)), false);

            var previousByName = (previous?.BundleInfos ?? new List<ReportBundleInfo>())
                .ToDictionary(item => item.BundleName, StringComparer.OrdinalIgnoreCase);
            var currentByName = current.BundleInfos.ToDictionary(item => item.BundleName,
                StringComparer.OrdinalIgnoreCase);
            var added = new List<BundleDeltaRecord>();
            var changed = new List<BundleDeltaRecord>();
            var unchanged = new List<BundleDeltaRecord>();

            foreach (ReportBundleInfo bundle in current.BundleInfos.OrderBy(item => item.BundleName,
                         StringComparer.OrdinalIgnoreCase))
            {
                if (!previousByName.TryGetValue(bundle.BundleName, out ReportBundleInfo old))
                {
                    added.Add(CreateRecord("Added", null, bundle, current, false));
                    continue;
                }

                if (string.Equals(old.FileName, bundle.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    ValidateContentAddressCollision(previousCdn, cdnRoot, old, bundle);
                    unchanged.Add(CreateRecord("Unchanged", old, bundle, current, false));
                }
                else
                {
                    bool suspectedTypeTree = HaveSameAssets(old, bundle) &&
                                             HaveSameDependencies(old, bundle);
                    changed.Add(CreateRecord("Changed", old, bundle, current, suspectedTypeTree));
                }
            }

            BundleDeltaRecord[] removed = previousByName.Values
                .Where(item => !currentByName.ContainsKey(item.BundleName))
                .OrderBy(item => item.BundleName, StringComparer.OrdinalIgnoreCase)
                .Select(item => CreateRecord("Removed", item, null, previous, false)).ToArray();

            var result = new BundleDeltaAnalysis
            {
                CurrentReport = current,
                PreviousReport = previous,
                Added = added.ToArray(),
                Changed = changed.ToArray(),
                Unchanged = unchanged.ToArray(),
                Removed = removed
            };
            result.UploadPlan = CreatePlan(cdnRoot, previousCdn, options, baseline, current,
                result.Added, result.Changed, result.Unchanged, result.Removed);
            return result;
        }

        private static ReleaseUploadPlan CreatePlan(string currentRoot, string previousRoot,
            ReleaseOptions options, ResourceReleaseBaseline baseline, BuildReport report,
            BundleDeltaRecord[] addedBundles, BundleDeltaRecord[] changedBundles,
            BundleDeltaRecord[] unchangedBundles, BundleDeltaRecord[] removedBundles)
        {
            var added = new List<UploadArtifact>();
            var changed = new List<UploadArtifact>();
            var unchanged = new List<UploadArtifact>();
            var bundleNames = new HashSet<string>(report.BundleInfos.Select(item => item.FileName),
                StringComparer.OrdinalIgnoreCase);
            var changedNames = new HashSet<string>(changedBundles.Select(item => item.currentFileName),
                StringComparer.OrdinalIgnoreCase);
            var unchangedNames = new HashSet<string>(unchangedBundles.Select(item => item.currentFileName),
                StringComparer.OrdinalIgnoreCase);

            foreach (string path in Directory.GetFiles(currentRoot, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(path);
                bool bundle = bundleNames.Contains(name);
                UploadArtifact artifact = CreateArtifact(path, name, bundle,
                    bundle && changedNames.Contains(name) ? "Changed" : "Added");
                if (bundle && unchangedNames.Contains(name))
                {
                    artifact.changeType = "Unchanged";
                    unchanged.Add(artifact);
                }
                else if ((bundle && changedNames.Contains(name)) ||
                         (!bundle && !string.IsNullOrWhiteSpace(previousRoot) &&
                          File.Exists(Path.Combine(previousRoot, name))))
                {
                    artifact.changeType = "Changed";
                    changed.Add(artifact);
                }
                else
                {
                    added.Add(artifact);
                }
            }

            long snapshotBytes = report.BundleInfos.Sum(item => item.FileSize);
            long uploadBytes = added.Concat(changed).Sum(item => item.length);
            long clientBytes = addedBundles.Sum(item => item.currentSize) +
                               changedBundles.Sum(item => item.currentSize);
            return new ReleaseUploadPlan
            {
                channel = options.channel,
                platform = ReleasePipeline.GetPlatformName(options.target),
                clientVersion = options.clientVersion,
                resourceVersion = options.resourceVersion,
                previousResourceVersion = baseline?.resourceVersion ?? string.Empty,
                added = added.ToArray(),
                changed = changed.ToArray(),
                unchanged = unchanged.ToArray(),
                removed = removedBundles.Select(item => new UploadArtifact
                {
                    relativePath = item.previousFileName,
                    bundleName = item.bundleName,
                    length = item.previousSize,
                    changeType = "Removed",
                    contentAddressedBundle = true
                }).ToArray(),
                snapshotBytes = snapshotBytes,
                uploadBytes = uploadBytes,
                estimatedClientDownloadBytes = clientBytes
            };
        }

        private static UploadArtifact CreateArtifact(string path, string relative, bool bundle, string type)
        {
            return new UploadArtifact
            {
                relativePath = relative.Replace('\\', '/'),
                bundleName = bundle ? relative : string.Empty,
                sha256 = ComputeSha256(path),
                length = new FileInfo(path).Length,
                changeType = type,
                contentAddressedBundle = bundle
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

        private static void ValidateContentAddressCollision(string previousRoot, string currentRoot,
            ReportBundleInfo previous, ReportBundleInfo current)
        {
            string oldPath = Path.Combine(previousRoot, previous.FileName);
            string currentPath = Path.Combine(currentRoot, current.FileName);
            if (!File.Exists(oldPath) || !File.Exists(currentPath))
                throw new FileNotFoundException(
                    $"Cannot validate content-addressed bundle '{current.FileName}' against the published baseline.",
                    !File.Exists(oldPath) ? oldPath : currentPath);
            if (new FileInfo(oldPath).Length != new FileInfo(currentPath).Length ||
                !string.Equals(ComputeSha256(oldPath), ComputeSha256(currentPath),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Content-addressed bundle collision: '{current.FileName}' has different content.");
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
