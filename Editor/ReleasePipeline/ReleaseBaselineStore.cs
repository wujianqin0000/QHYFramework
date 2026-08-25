using System;
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
                releaseRoot = Path.GetFullPath(releaseRoot),
                publishedAtUtc = DateTime.UtcNow.ToString("O")
            };
            File.WriteAllText(path, JsonUtility.ToJson(baseline, true), new UTF8Encoding(false));
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
    }
}
