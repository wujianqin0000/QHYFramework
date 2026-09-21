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
        public int schemaVersion = DistributionRuntimeConfig.CurrentSchemaVersion;
        public string gameDirectory, platform, clientVersion, resourceVersion, highestResourceVersion;
        public string[] publishedResourceVersions = Array.Empty<string>();
        public string releaseRoot, stage = "Built", publishedAtUtc;
        public bool hasPublishedBaseline;
        public string pendingClientVersion, pendingResourceVersion, pendingBuildRoot;
    }

    [Serializable] internal sealed class PublicationHistory
    { public int schemaVersion = DistributionRuntimeConfig.CurrentSchemaVersion; public PublicationHistoryEntry[] entries = Array.Empty<PublicationHistoryEntry>(); }
    [Serializable] internal sealed class PublicationHistoryEntry
    { public string action, clientVersion, resourceVersion, timestampUtc, buildRoot; }

    internal static class ReleaseBaselineStore
    {
        internal static ResourceReleaseBaseline Load(ReleaseOptions options)
        {
            string path = GetPath(options);
            if (!File.Exists(path)) return null;
            ResourceReleaseBaseline value = JsonUtility.FromJson<ResourceReleaseBaseline>(File.ReadAllText(path));
            if (value == null || value.schemaVersion != DistributionRuntimeConfig.CurrentSchemaVersion) return null;
            value.releaseRoot = ResolveStoredPath(options, value.releaseRoot);
            value.pendingBuildRoot = ResolveStoredPath(options, value.pendingBuildRoot);
            return value;
        }
        internal static void SaveBuilt(ReleaseOptions options, string buildRoot, ReleaseUploadPlan plan) =>
            SaveStage(options, buildRoot, plan, "Built", false);
        internal static void SaveManualPending(string buildRoot, ReleaseUploadPlan plan) =>
            SaveStage(OptionsFromPlan(buildRoot, plan), buildRoot, plan, "Built", false);
        internal static void SaveUploading(string buildRoot, ReleaseUploadPlan plan) =>
            SaveStage(OptionsFromPlan(buildRoot, plan), buildRoot, plan, "Uploading", false);
        internal static void SavePublished(string releaseRoot, ReleaseUploadPlan plan)
        {
            ReleaseOptions options = OptionsFromPlan(releaseRoot, plan);
            SaveStage(options, releaseRoot, plan, "Published", true);
            AppendHistory(options, "Publish", plan, releaseRoot);
        }
        internal static bool IsAlreadyPublished(string releaseRoot, ReleaseUploadPlan plan)
        {
            ResourceReleaseBaseline value = Load(OptionsFromPlan(releaseRoot, plan));
            return value?.hasPublishedBaseline == true && string.Equals(value.resourceVersion, plan.resourceVersion,
                StringComparison.OrdinalIgnoreCase);
        }
        internal static bool IsClientAlreadyPublished(string releaseRoot, ReleaseUploadPlan plan)
        {
            ResourceReleaseBaseline value = Load(OptionsFromPlan(releaseRoot, plan));
            return value?.hasPublishedBaseline == true && string.Equals(value.clientVersion, plan.clientVersion,
                StringComparison.OrdinalIgnoreCase);
        }
        internal static void SaveRollback(string releaseRoot, ReleaseUploadPlan plan, string highest)
        {
            ReleaseOptions options = OptionsFromPlan(releaseRoot, plan);
            SaveStage(options, releaseRoot, plan, "Published", true);
            AppendHistory(options, "Rollback", plan, releaseRoot);
        }
        internal static void MarkClientPublished(string releaseRoot, ReleaseUploadPlan plan) { }
        internal static ResourceReleaseBaseline LoadForRelease(string releaseRoot, ReleaseUploadPlan plan) =>
            Load(OptionsFromPlan(releaseRoot, plan));
        internal static string GetPath(ReleaseOptions options) =>
            Path.Combine(DistributionReleaseLayout.StateRoot(options), "publication-state.json");

        internal static ReleaseRollbackTarget[] GetRollbackTargets(ReleaseOptions options)
        {
            ResourceReleaseBaseline baseline = Load(options);
            string path = Path.Combine(DistributionReleaseLayout.StateRoot(options), "history.json");
            if (baseline?.hasPublishedBaseline != true || !File.Exists(path)) return Array.Empty<ReleaseRollbackTarget>();
            PublicationHistory history = JsonUtility.FromJson<PublicationHistory>(File.ReadAllText(path));
            return (history?.entries ?? Array.Empty<PublicationHistoryEntry>())
                .Where(x => x.clientVersion == options.clientVersion && x.resourceVersion != baseline.resourceVersion)
                .GroupBy(x => x.resourceVersion, StringComparer.OrdinalIgnoreCase).Select(x => x.Last())
                .Select(x =>
                {
                    string resolvedRoot = ResolveStoredPath(options, x.buildRoot);
                    string planPath = Path.Combine(resolvedRoot, "publish-plan.json");
                    if (!File.Exists(planPath)) return null;
                    ReleaseUploadPlan plan = JsonUtility.FromJson<ReleaseUploadPlan>(File.ReadAllText(planPath));
                    return plan == null ? null : new ReleaseRollbackTarget
                    { ResourceVersion = plan.resourceVersion, ReleaseRoot = resolvedRoot, Plan = plan };
                }).Where(x => x != null).Reverse().ToArray();
        }

        private static void SaveStage(ReleaseOptions options, string buildRoot, ReleaseUploadPlan plan,
            string stage, bool published)
        {
            ResourceReleaseBaseline old = Load(options);
            Directory.CreateDirectory(DistributionReleaseLayout.StateRoot(options));
            plan.stage = stage;
            bool retainPublished = !published && old?.hasPublishedBaseline == true;
            var value = new ResourceReleaseBaseline
            {
                gameDirectory = plan.gameDirectory,
                platform = plan.platform,
                clientVersion = retainPublished ? old.clientVersion : plan.clientVersion,
                resourceVersion = retainPublished ? old.resourceVersion : plan.resourceVersion,
                highestResourceVersion = Higher(plan.clientVersion,
                    old?.highestResourceVersion ?? old?.resourceVersion, plan.resourceVersion),
                publishedResourceVersions = published ? (old?.publishedResourceVersions ?? Array.Empty<string>())
                    .Concat(new[] { old?.resourceVersion, plan.resourceVersion })
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    : old?.publishedResourceVersions ?? Array.Empty<string>(),
                releaseRoot = StorePath(options, retainPublished ? old.releaseRoot : buildRoot), stage = stage,
                hasPublishedBaseline = published || old?.hasPublishedBaseline == true,
                pendingClientVersion = published ? string.Empty : plan.clientVersion,
                pendingResourceVersion = published ? string.Empty : plan.resourceVersion,
                pendingBuildRoot = published ? string.Empty : StorePath(options, buildRoot),
                publishedAtUtc = published ? DateTime.UtcNow.ToString("O") : old?.publishedAtUtc
            };
            File.WriteAllText(GetPath(options), JsonUtility.ToJson(value, true), new UTF8Encoding(false));
            string planPath = Path.Combine(buildRoot, "publish-plan.json");
            if (File.Exists(planPath)) File.WriteAllText(planPath, JsonUtility.ToJson(plan, true), new UTF8Encoding(false));
        }
        private static void AppendHistory(ReleaseOptions options, string action, ReleaseUploadPlan plan,
            string buildRoot)
        {
            string path = Path.Combine(DistributionReleaseLayout.StateRoot(options), "history.json");
            PublicationHistory value = File.Exists(path)
                ? JsonUtility.FromJson<PublicationHistory>(File.ReadAllText(path)) : new PublicationHistory();
            value ??= new PublicationHistory();
            value.entries = (value.entries ?? Array.Empty<PublicationHistoryEntry>()).Concat(new[]
            {
                new PublicationHistoryEntry { action = action, clientVersion = plan.clientVersion,
                    resourceVersion = plan.resourceVersion, timestampUtc = DateTime.UtcNow.ToString("O"),
                    buildRoot = StorePath(options, buildRoot) }
            }).ToArray();
            File.WriteAllText(path, JsonUtility.ToJson(value, true), new UTF8Encoding(false));
        }
        private static ReleaseOptions OptionsFromPlan(string buildRoot, ReleaseUploadPlan plan)
        {
            return new ReleaseOptions { outputRoot = plan.releasesRoot ?? "Releases",
                gameDirectory = plan.gameDirectory,
                stateRootOverride = plan.stateRoot,
                target = plan.platform == "android" ? BuildTarget.Android : BuildTarget.StandaloneWindows64,
                clientVersion = plan.clientVersion, resourceVersion = plan.resourceVersion };
        }
        private static string Higher(string client, string left, string right)
        {
            bool l = ResourceVersionResolver.TryGetRevision(left, client, out int li);
            bool r = ResourceVersionResolver.TryGetRevision(right, client, out int ri);
            if (!l) return right ?? string.Empty; if (!r) return left ?? string.Empty;
            return ri > li ? right : left;
        }
        private static string StorePath(ReleaseOptions options, string path) =>
            string.IsNullOrWhiteSpace(path) ? string.Empty :
                DistributionReleaseLayout.Relative(ProjectRoot(), Path.GetFullPath(path));
        private static string ResolveStoredPath(ReleaseOptions options, string path) =>
            string.IsNullOrWhiteSpace(path) ? string.Empty : (Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(ProjectRoot(),
                    path.Replace('/', Path.DirectorySeparatorChar))));
        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
