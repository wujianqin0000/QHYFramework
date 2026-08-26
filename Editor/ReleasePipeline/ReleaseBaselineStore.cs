using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameIntegration.Editor
{
    [Serializable]
    internal sealed class ResourceReleaseBaseline
    {
        public int schemaVersion = 1;
        public string channel;
        public string platform;
        public string clientVersion;
        public string resourceVersion;
        public string highestResourceVersion;
        public string[] publishedResourceVersions = Array.Empty<string>();
        public string releaseRoot;
        public string publishedAtUtc;
    }

    internal static class ReleaseBaselineStore
    {
        internal static ResourceReleaseBaseline Load(ReleaseOptions options)
        {
            string path = GetPath(options);
            if (!File.Exists(path)) return null;
            ResourceReleaseBaseline value = JsonUtility.FromJson<ResourceReleaseBaseline>(File.ReadAllText(path));
            return value != null && value.schemaVersion == 1 ? value : null;
        }

        internal static void SavePublished(string releaseRoot, ReleaseUploadPlan plan)
        {
            string outputRoot = GetOutputRoot(releaseRoot);
            var options = new ReleaseOptions
            {
                outputRoot = outputRoot,
                channel = plan.channel,
                target = ParseTarget(plan.platform),
                clientVersion = plan.clientVersion
            };
            string path = GetPath(options);
            ResourceReleaseBaseline existing = File.Exists(path)
                ? JsonUtility.FromJson<ResourceReleaseBaseline>(File.ReadAllText(path))
                : null;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? outputRoot);
            var baseline = new ResourceReleaseBaseline
            {
                channel = plan.channel,
                platform = plan.platform,
                clientVersion = plan.clientVersion,
                resourceVersion = plan.resourceVersion,
                highestResourceVersion = SelectHigher(plan.clientVersion,
                    existing?.highestResourceVersion ?? existing?.resourceVersion, plan.resourceVersion),
                publishedResourceVersions = MergePublishedVersions(existing, plan.resourceVersion),
                releaseRoot = Path.GetFullPath(releaseRoot),
                publishedAtUtc = DateTime.UtcNow.ToString("O")
            };
            File.WriteAllText(path, JsonUtility.ToJson(baseline, true), new UTF8Encoding(false));
        }

        internal static bool IsAlreadyPublished(string releaseRoot, ReleaseUploadPlan plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.resourceVersion)) return false;
            string outputRoot = GetOutputRoot(releaseRoot);
            var options = new ReleaseOptions
            {
                outputRoot = outputRoot,
                channel = plan.channel,
                target = ParseTarget(plan.platform),
                clientVersion = plan.clientVersion
            };
            ResourceReleaseBaseline baseline = Load(options);
            return baseline != null && string.Equals(baseline.resourceVersion,
                plan.resourceVersion, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsClientAlreadyPublished(string releaseRoot, ReleaseUploadPlan plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.clientVersion)) return false;
            string path = Path.Combine(GetOutputRoot(releaseRoot), "Baselines",
                plan.platform + ".client-version");
            return File.Exists(path) && string.Equals(File.ReadAllText(path).Trim(),
                plan.clientVersion, StringComparison.OrdinalIgnoreCase);
        }

        internal static void SaveRollback(string releaseRoot, ReleaseUploadPlan previousPlan,
            string highestPublishedResourceVersion)
        {
            string outputRoot = GetOutputRoot(releaseRoot);
            var options = new ReleaseOptions
            {
                outputRoot = outputRoot,
                channel = previousPlan.channel,
                target = ParseTarget(previousPlan.platform),
                clientVersion = previousPlan.clientVersion
            };
            string path = GetPath(options);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? outputRoot);
            ResourceReleaseBaseline existing = File.Exists(path)
                ? JsonUtility.FromJson<ResourceReleaseBaseline>(File.ReadAllText(path))
                : null;
            string knownHighest = SelectHigher(previousPlan.clientVersion,
                existing?.highestResourceVersion ?? existing?.resourceVersion,
                highestPublishedResourceVersion);
            var baseline = new ResourceReleaseBaseline
            {
                channel = previousPlan.channel,
                platform = previousPlan.platform,
                clientVersion = previousPlan.clientVersion,
                resourceVersion = previousPlan.resourceVersion,
                highestResourceVersion = SelectHigher(previousPlan.clientVersion,
                    previousPlan.resourceVersion, knownHighest),
                publishedResourceVersions = MergePublishedVersions(existing,
                    previousPlan.resourceVersion),
                releaseRoot = Path.GetFullPath(releaseRoot),
                publishedAtUtc = DateTime.UtcNow.ToString("O")
            };
            File.WriteAllText(path, JsonUtility.ToJson(baseline, true), new UTF8Encoding(false));
        }

        internal static void MarkClientPublished(string releaseRoot, ReleaseUploadPlan plan)
        {
            string outputRoot = GetOutputRoot(releaseRoot);
            string path = Path.Combine(outputRoot, "Baselines", plan.platform + ".client-version");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? outputRoot);
            File.WriteAllText(path, plan.clientVersion, new UTF8Encoding(false));
        }

        internal static ResourceReleaseBaseline LoadForRelease(string releaseRoot, ReleaseUploadPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return Load(new ReleaseOptions
            {
                outputRoot = GetOutputRoot(releaseRoot),
                channel = plan.channel,
                target = ParseTarget(plan.platform),
                clientVersion = plan.clientVersion
            });
        }

        internal static ReleaseRollbackTarget[] GetRollbackTargets(ReleaseOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            ResourceReleaseBaseline baseline = Load(options);
            if (baseline == null || string.IsNullOrWhiteSpace(baseline.resourceVersion))
                return Array.Empty<ReleaseRollbackTarget>();

            string highest = string.IsNullOrWhiteSpace(baseline.highestResourceVersion)
                ? baseline.resourceVersion
                : baseline.highestResourceVersion;
            if (!ResourceVersionResolver.TryGetRevision(highest, options.clientVersion,
                    out int highestRevision))
                return Array.Empty<ReleaseRollbackTarget>();

            string clientRoot = ReleasePipeline.GetClientVersionRoot(options);
            var roots = new List<string>();
            string revisionsRoot = Path.Combine(clientRoot, "Revisions");
            if (Directory.Exists(revisionsRoot))
                roots.AddRange(Directory.GetDirectories(revisionsRoot));
            if (Directory.Exists(clientRoot))
                roots.AddRange(Directory.GetDirectories(clientRoot)
                    .Where(path => ResourceVersionResolver.TryGetRevision(Path.GetFileName(path),
                        options.clientVersion, out _)));

            var targets = new List<ReleaseRollbackTarget>();
            var publishedVersions = new HashSet<string>(baseline.publishedResourceVersions ??
                Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            bool hasPublicationLedger = publishedVersions.Count > 0;
            publishedVersions.Add(baseline.resourceVersion);
            if (hasPublicationLedger)
                ExpandPublishedHistory(publishedVersions, roots, options);
            foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string planPath = Path.Combine(root, "upload-plan.json");
                if (!File.Exists(planPath)) continue;
                ReleaseUploadPlan plan;
                try { plan = JsonUtility.FromJson<ReleaseUploadPlan>(File.ReadAllText(planPath)); }
                catch { continue; }
                if (plan == null ||
                    !string.Equals(plan.channel, options.channel, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(plan.platform, ReleasePipeline.GetPlatformName(options.target),
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(plan.clientVersion, options.clientVersion,
                        StringComparison.OrdinalIgnoreCase) ||
                    !ResourceVersionResolver.TryGetRevision(plan.resourceVersion,
                        options.clientVersion, out int revision) || revision > highestRevision ||
                    (hasPublicationLedger && !publishedVersions.Contains(plan.resourceVersion)) ||
                    string.Equals(plan.resourceVersion, baseline.resourceVersion,
                        StringComparison.OrdinalIgnoreCase) ||
                    !HasCompleteLocalSnapshot(root, plan))
                    continue;
                targets.Add(new ReleaseRollbackTarget
                {
                    ResourceVersion = plan.resourceVersion,
                    ReleaseRoot = Path.GetFullPath(root),
                    Plan = plan
                });
            }

            return targets.GroupBy(item => item.ResourceVersion, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(item =>
                {
                    ResourceVersionResolver.TryGetRevision(item.ResourceVersion, options.clientVersion,
                        out int revision);
                    return revision;
                }).ToArray();
        }

        private static bool HasCompleteLocalSnapshot(string releaseRoot, ReleaseUploadPlan plan)
        {
            string cdn = Path.Combine(releaseRoot, "CDN");
            if (!Directory.Exists(cdn) || Directory.GetFiles(cdn, "*.version",
                    SearchOption.TopDirectoryOnly).Length != 1)
                return false;
            UploadArtifact[] artifacts = (plan.added ?? Array.Empty<UploadArtifact>())
                .Concat(plan.changed ?? Array.Empty<UploadArtifact>())
                .Concat(plan.unchanged ?? Array.Empty<UploadArtifact>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.relativePath)).ToArray();
            if (!artifacts.Any(item => item.contentAddressedBundle) ||
                !artifacts.Any(item => Path.GetExtension(item.relativePath)
                    .Equals(".bytes", StringComparison.OrdinalIgnoreCase)))
                return false;
            string safeRoot = Path.GetFullPath(cdn).TrimEnd(Path.DirectorySeparatorChar) +
                              Path.DirectorySeparatorChar;
            return artifacts.All(item =>
            {
                string path = Path.GetFullPath(Path.Combine(cdn,
                    item.relativePath.Replace('/', Path.DirectorySeparatorChar)));
                return path.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(path);
            });
        }

        private static void ExpandPublishedHistory(ISet<string> publishedVersions,
            IEnumerable<string> roots, ReleaseOptions options)
        {
            bool changed;
            do
            {
                changed = false;
                foreach (string root in roots)
                {
                    string planPath = Path.Combine(root, "upload-plan.json");
                    if (!File.Exists(planPath)) continue;
                    ReleaseUploadPlan plan;
                    try { plan = JsonUtility.FromJson<ReleaseUploadPlan>(File.ReadAllText(planPath)); }
                    catch { continue; }
                    if (plan == null ||
                        !string.Equals(plan.channel, options.channel, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(plan.platform, ReleasePipeline.GetPlatformName(options.target),
                            StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(plan.clientVersion, options.clientVersion,
                            StringComparison.OrdinalIgnoreCase) ||
                        !publishedVersions.Contains(plan.resourceVersion) ||
                        string.IsNullOrWhiteSpace(plan.previousResourceVersion))
                        continue;
                    changed |= publishedVersions.Add(plan.previousResourceVersion);
                }
            } while (changed);
        }

        internal static string GetPath(ReleaseOptions options)
        {
            string name = string.Join(".", new[]
            {
                options.channel, ReleasePipeline.GetPlatformName(options.target), options.clientVersion
            }.Select(Sanitize));
            return Path.GetFullPath(Path.Combine(options.outputRoot, "Baselines", "Resources", name + ".json"));
        }

        private static string GetOutputRoot(string releaseRoot)
        {
            DirectoryInfo current = new DirectoryInfo(Path.GetFullPath(releaseRoot));
            if (string.Equals(current.Parent?.Name, "Revisions", StringComparison.OrdinalIgnoreCase))
                current = current.Parent;
            for (int i = 0; i < 4; i++)
                current = current.Parent ?? throw new InvalidOperationException("Invalid release directory layout.");
            return current.FullName;
        }

        private static BuildTarget ParseTarget(string platform)
        {
            return string.Equals(platform, "Android", StringComparison.OrdinalIgnoreCase)
                ? BuildTarget.Android
                : BuildTarget.StandaloneWindows64;
        }

        private static string Sanitize(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string((value ?? string.Empty).Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static string SelectHigher(string clientVersion, string left, string right)
        {
            bool hasLeft = ResourceVersionResolver.TryGetRevision(left, clientVersion, out int leftRevision);
            bool hasRight = ResourceVersionResolver.TryGetRevision(right, clientVersion, out int rightRevision);
            if (!hasLeft) return right ?? string.Empty;
            if (!hasRight) return left ?? string.Empty;
            return rightRevision > leftRevision ? right : left;
        }

        private static string[] MergePublishedVersions(ResourceReleaseBaseline existing,
            params string[] versions)
        {
            return (existing?.publishedResourceVersions ?? Array.Empty<string>())
                .Concat(new[] { existing?.resourceVersion })
                .Concat(versions ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
