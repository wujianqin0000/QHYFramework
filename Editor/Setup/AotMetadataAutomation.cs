using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HybridCLR.Editor;
using UnityEditor;
using UnityEngine;

namespace GameIntegration.Editor
{
    internal sealed class AotMetadataAnalysis
    {
        public BuildTarget Target;
        public string SourcePath;
        public string SourceHash;
        public string Error;
        public string[] AutomaticAssemblies = Array.Empty<string>();
        public string[] ExtraAssemblies = Array.Empty<string>();
        public string[] EffectiveAssemblies = Array.Empty<string>();
        public string[] MissingAssemblies = Array.Empty<string>();
        public bool Succeeded => string.IsNullOrEmpty(Error);
    }

    [Serializable]
    internal sealed class AotMetadataSnapshotFile
    {
        public string name;
        public long length;
        public string sha256;
    }

    [Serializable]
    internal sealed class AotMetadataSnapshotManifest
    {
        public int schemaVersion = 2;
        public string buildTarget;
        public string analysisHash;
        public string createdAtUtc;
        public string[] assemblies = Array.Empty<string>();
        public AotMetadataSnapshotFile[] files = Array.Empty<AotMetadataSnapshotFile>();
    }

    internal static class AotMetadataAutomation
    {
        private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly Regex ListRegex = new Regex(
            @"PatchedAOTAssemblyList\s*=\s*new\s+List<string>\s*\{(?<body>.*?)\}\s*;",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private static readonly Regex StringRegex = new Regex("\"(?<name>[^\"\\r\\n]+)\"",
            RegexOptions.CultureInvariant);

        internal static string GetSourcePath()
        {
            string relative = SettingsUtil.HybridCLRSettings.outputAOTGenericReferenceFile?.Trim()
                              ?? string.Empty;
            return Path.GetFullPath(Path.Combine(Application.dataPath, relative));
        }

        internal static AotMetadataAnalysis Inspect(QHYFrameworkSettings settings, BuildTarget target)
        {
            return Inspect(settings, target, GetSourcePath(),
                SettingsUtil.GetAssembliesPostIl2CppStripDir(target));
        }

        internal static AotMetadataAnalysis Inspect(QHYFrameworkSettings settings, BuildTarget target,
            string sourcePath, string strippedRoot)
        {
            var result = new AotMetadataAnalysis
            {
                Target = target,
                SourcePath = sourcePath,
                ExtraAssemblies = Normalize(settings.aotMetadataExtraAssemblyNames)
            };

            try
            {
                if (!File.Exists(result.SourcePath))
                    throw new FileNotFoundException("HybridCLR AOTGenericReferences file is missing.", result.SourcePath);

                byte[] sourceBytes = File.ReadAllBytes(result.SourcePath);
                result.SourceHash = ComputeHash(sourceBytes);
                string source = Encoding.UTF8.GetString(sourceBytes);
                result.AutomaticAssemblies = ParseAutomaticAssemblies(source);
                result.EffectiveAssemblies = Normalize(result.AutomaticAssemblies.Concat(result.ExtraAssemblies));
                result.MissingAssemblies = result.EffectiveAssemblies
                    .Where(name => !File.Exists(Path.Combine(strippedRoot, name))).ToArray();
            }
            catch (Exception exception)
            {
                result.Error = exception.GetBaseException().Message;
            }
            return result;
        }

        internal static string[] ParseAutomaticAssemblies(string source)
        {
            Match list = ListRegex.Match(source ?? string.Empty);
            if (!list.Success)
                throw new InvalidDataException(
                    "Cannot find AOTGenericReferences.PatchedAOTAssemblyList in the generated file.");
            return Normalize(StringRegex.Matches(list.Groups["body"].Value)
                .Cast<Match>().Select(match => match.Groups["name"].Value));
        }

        internal static AotMetadataAnalysis Synchronize(QHYFrameworkSettings settings, BuildTarget target,
            bool requireGeneratedAssemblies, bool trustCurrentTarget = false, bool saveAssets = true)
        {
            if (!settings)
                throw new ArgumentNullException(nameof(settings));

            AotMetadataAnalysis analysis = Inspect(settings, target);
            if (!analysis.Succeeded)
                throw new InvalidOperationException($"AOT metadata analysis failed for {target}: " +
                                                    $"{analysis.Error} Source: {analysis.SourcePath}");

            if (!trustCurrentTarget && settings.aotMetadataAutomationInitialized &&
                !string.Equals(settings.aotMetadataAnalysisTarget, target.ToString(), StringComparison.Ordinal) &&
                string.Equals(settings.aotMetadataAnalysisHash, analysis.SourceHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The generated AOT analysis still belongs to {settings.aotMetadataAnalysisTarget}. " +
                    $"Run HybridCLR Generate/All for {target} before synchronizing.");
            }

            if (!settings.aotMetadataAutomationInitialized)
            {
                var automatic = new HashSet<string>(analysis.AutomaticAssemblies, NameComparer);
                analysis.ExtraAssemblies = Normalize(analysis.ExtraAssemblies.Concat(
                    Normalize(settings.aotMetadataAssemblyNames).Where(name => !automatic.Contains(name))));
                analysis.EffectiveAssemblies = Normalize(
                    analysis.AutomaticAssemblies.Concat(analysis.ExtraAssemblies));
                string strippedRoot = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
                analysis.MissingAssemblies = analysis.EffectiveAssemblies
                    .Where(name => !File.Exists(Path.Combine(strippedRoot, name))).ToArray();
            }

            if (requireGeneratedAssemblies && analysis.MissingAssemblies.Length > 0)
            {
                throw new FileNotFoundException($"AOT metadata assemblies are missing for {target} in " +
                    $"'{SettingsUtil.GetAssembliesPostIl2CppStripDir(target)}': " +
                    string.Join(", ", analysis.MissingAssemblies));
            }

            settings.aotMetadataExtraAssemblyNames = analysis.ExtraAssemblies;
            settings.aotMetadataAutoAssemblyNames = analysis.AutomaticAssemblies;
            settings.aotMetadataAssemblyNames = analysis.EffectiveAssemblies;
            settings.aotMetadataAnalysisTarget = target.ToString();
            settings.aotMetadataAnalysisHash = analysis.SourceHash;
            settings.aotMetadataAnalysisUtc = DateTime.UtcNow.ToString("O");
            settings.aotMetadataAutomationInitialized = true;
            EditorUtility.SetDirty(settings);
            if (saveAssets)
                AssetDatabase.SaveAssets();
            return analysis;
        }

        internal static string[] Normalize(IEnumerable<string> names)
        {
            return (names ?? Array.Empty<string>())
                .Select(NormalizeDllFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct(NameComparer)
                .OrderBy(name => name, NameComparer)
                .ToArray();
        }

        private static string NormalizeDllFileName(string value)
        {
            string name = Path.GetFileName(value?.Trim() ?? string.Empty);
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return string.IsNullOrEmpty(name) ? string.Empty : name + ".dll";
        }

        internal static string ComputeHash(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }
    }

    internal static class AotMetadataSnapshotStore
    {
        private const string ManifestFileName = "snapshot.json";

        internal static string GetRoot(BuildTarget target)
        {
            return Path.GetFullPath(Path.Combine("Library", "GameIntegration", "AOTMetadata", target.ToString()));
        }

        internal static string GetRoot(BuildTarget target, string clientVersion)
        {
            if (string.IsNullOrWhiteSpace(clientVersion)) return GetRoot(target);
            string safe = new string(clientVersion.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
            return Path.GetFullPath(Path.Combine("Library", "GameIntegration", "AOTMetadata",
                target.ToString(), safe));
        }

        internal static void Save(BuildTarget target, string sourceRoot, IEnumerable<string> names,
            string analysisHash, string snapshotRoot = null)
        {
            string root = snapshotRoot ?? GetRoot(target);
            string staging = root + ".staging-" + Guid.NewGuid().ToString("N");
            string backup = root + ".backup-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            string[] assemblies = AotMetadataAutomation.Normalize(names);
            try
            {
                foreach (string name in assemblies)
                    CopyRequired(Path.Combine(sourceRoot, name + ".bytes"),
                        Path.Combine(staging, name + ".bytes"));

                AotMetadataSnapshotFile[] files = assemblies.Select(name =>
                {
                    string path = Path.Combine(staging, name + ".bytes");
                    return new AotMetadataSnapshotFile
                    {
                        name = name,
                        length = new FileInfo(path).Length,
                        sha256 = ComputeSha256(path)
                    };
                }).ToArray();

                var manifest = new AotMetadataSnapshotManifest
                {
                    buildTarget = target.ToString(),
                    analysisHash = analysisHash ?? string.Empty,
                    createdAtUtc = DateTime.UtcNow.ToString("O"),
                    assemblies = assemblies,
                    files = files
                };
                File.WriteAllText(Path.Combine(staging, ManifestFileName), JsonUtility.ToJson(manifest, true),
                    new UTF8Encoding(false));

                if (Directory.Exists(root))
                    Directory.Move(root, backup);
                try
                {
                    Directory.Move(staging, root);
                    if (Directory.Exists(backup))
                        Directory.Delete(backup, true);
                }
                catch
                {
                    if (!Directory.Exists(root) && Directory.Exists(backup))
                        Directory.Move(backup, root);
                    throw;
                }
            }
            finally
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
        }

        internal static string[] Restore(BuildTarget target, string destinationRoot, string snapshotRoot = null)
        {
            string root = snapshotRoot ?? GetRoot(target);
            if (!Directory.Exists(root))
                throw new InvalidOperationException(
                    $"No client AOT metadata snapshot exists for {target}. Run Full Package Build first.");

            string manifestPath = Path.Combine(root, ManifestFileName);
            string[] names;
            if (File.Exists(manifestPath))
            {
                AotMetadataSnapshotManifest manifest =
                    JsonUtility.FromJson<AotMetadataSnapshotManifest>(File.ReadAllText(manifestPath));
                if (manifest == null || (manifest.schemaVersion != 1 && manifest.schemaVersion != 2) ||
                    !string.Equals(manifest.buildTarget, target.ToString(), StringComparison.Ordinal))
                    throw new InvalidDataException($"Invalid AOT metadata snapshot manifest: {manifestPath}");
                names = AotMetadataAutomation.Normalize(manifest.assemblies);
                if (manifest.schemaVersion == 2)
                {
                    var records = (manifest.files ?? Array.Empty<AotMetadataSnapshotFile>())
                        .ToDictionary(item => item.name, StringComparer.OrdinalIgnoreCase);
                    foreach (string name in names)
                    {
                        string source = Path.Combine(root, name + ".bytes");
                        if (!records.TryGetValue(name, out AotMetadataSnapshotFile record) ||
                            !File.Exists(source) || new FileInfo(source).Length != record.length ||
                            !string.Equals(ComputeSha256(source), record.sha256,
                                StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException(
                                $"AOT metadata snapshot SHA-256 validation failed: {name}");
                    }
                }
            }
            else
            {
                names = Directory.GetFiles(root, "*.dll.bytes", SearchOption.TopDirectoryOnly)
                    .Select(path => Path.GetFileName(path).Substring(0,
                        Path.GetFileName(path).Length - ".bytes".Length)).ToArray();
                names = AotMetadataAutomation.Normalize(names);
                Debug.LogWarning($"[QHYFramework] Legacy AOT snapshot for {target} has no manifest. " +
                                 "The assembly list was recovered from existing files.");
            }

            if (Directory.Exists(destinationRoot))
                Directory.Delete(destinationRoot, true);
            Directory.CreateDirectory(destinationRoot);
            foreach (string name in names)
                CopyRequired(Path.Combine(root, name + ".bytes"),
                    Path.Combine(destinationRoot, name + ".bytes"));
            return names;
        }

        internal static bool MatchesSnapshot(BuildTarget target, string generatedRoot,
            string snapshotRoot, out string reason)
        {
            string root = snapshotRoot ?? GetRoot(target);
            if (!Directory.Exists(root))
            {
                reason = $"AOT metadata snapshot does not exist: {root}";
                return false;
            }
            if (!Directory.Exists(generatedRoot))
            {
                reason = $"Generated AOT metadata directory does not exist: {generatedRoot}";
                return false;
            }

            string manifestPath = Path.Combine(root, ManifestFileName);
            string[] expectedNames;
            if (File.Exists(manifestPath))
            {
                AotMetadataSnapshotManifest manifest =
                    JsonUtility.FromJson<AotMetadataSnapshotManifest>(File.ReadAllText(manifestPath));
                if (manifest == null || (manifest.schemaVersion != 1 && manifest.schemaVersion != 2) ||
                    !string.Equals(manifest.buildTarget, target.ToString(), StringComparison.Ordinal))
                {
                    reason = $"Invalid AOT metadata snapshot manifest: {manifestPath}";
                    return false;
                }
                expectedNames = AotMetadataAutomation.Normalize(manifest.assemblies);
            }
            else
            {
                expectedNames = AotMetadataAutomation.Normalize(Directory.GetFiles(root,
                        "*.dll.bytes", SearchOption.TopDirectoryOnly)
                    .Select(path => Path.GetFileName(path).Substring(0,
                        Path.GetFileName(path).Length - ".bytes".Length)));
            }

            string[] actualNames = AotMetadataAutomation.Normalize(Directory.GetFiles(generatedRoot,
                    "*.dll.bytes", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileName(path).Substring(0,
                    Path.GetFileName(path).Length - ".bytes".Length)));
            if (!expectedNames.SequenceEqual(actualNames, StringComparer.OrdinalIgnoreCase))
            {
                reason = "AOT metadata assembly set differs from the published client snapshot. " +
                         $"Expected: [{string.Join(", ", expectedNames)}]; " +
                         $"actual: [{string.Join(", ", actualNames)}].";
                return false;
            }

            foreach (string name in expectedNames)
            {
                string snapshotFile = Path.Combine(root, name + ".bytes");
                string generatedFile = Path.Combine(generatedRoot, name + ".bytes");
                if (!File.Exists(snapshotFile) || !File.Exists(generatedFile))
                {
                    reason = $"AOT metadata file is missing: {name}.bytes";
                    return false;
                }
                var snapshotInfo = new FileInfo(snapshotFile);
                var generatedInfo = new FileInfo(generatedFile);
                if (snapshotInfo.Length != generatedInfo.Length ||
                    !string.Equals(ComputeSha256(snapshotFile), ComputeSha256(generatedFile),
                        StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"AOT metadata payload differs from the published client snapshot: {name}.bytes";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static void CopyRequired(string source, string destination)
        {
            if (!File.Exists(source))
                throw new FileNotFoundException("AOT metadata snapshot file is missing.", source);
            File.Copy(source, destination, true);
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    internal sealed class AotMetadataGeneratedFilePostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            string configured = SettingsUtil.HybridCLRSettings.outputAOTGenericReferenceFile
                ?.Replace('\\', '/').TrimStart('/') ?? string.Empty;
            string generatedAssetPath = "Assets/" + configured;
            if (!importedAssets.Any(path => string.Equals(path.Replace('\\', '/'), generatedAssetPath,
                    StringComparison.OrdinalIgnoreCase)))
                return;

            EditorApplication.delayCall += () =>
            {
                QHYFrameworkSettings settings = IntegrationProjectPreparer.LoadSettings();
                try
                {
                    AotMetadataAutomation.Synchronize(settings, EditorUserBuildSettings.activeBuildTarget,
                        false, true);
                    Debug.Log("[QHYFramework] AOT metadata configuration synchronized from HybridCLR analysis.");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[QHYFramework] Automatic AOT metadata synchronization skipped: " +
                                     exception.GetBaseException().Message);
                }
            };
        }
    }
}
