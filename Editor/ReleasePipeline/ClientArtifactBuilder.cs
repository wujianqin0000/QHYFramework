using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace GameIntegration.Editor
{
    internal sealed class ClientArtifactResult
    {
        public string Root;
        public string PackagePath;
        public string ManifestPath;
        public ClientUpdateManifest Manifest;
        public UploadArtifact[] Artifacts = Array.Empty<UploadArtifact>();
    }

    internal static class ClientArtifactBuilder
    {
        public static ClientArtifactResult Build(string releaseRoot, string clientRoot,
            QHYFrameworkSettings settings, ReleaseOptions options)
        {
            IntegrationPlatform platform = ReleasePipeline.GetIntegrationPlatform(options.target);
            if (platform != IntegrationPlatform.Windows && platform != IntegrationPlatform.Android)
                return null;
            if (platform == IntegrationPlatform.Windows && options.target != BuildTarget.StandaloneWindows64)
                throw new PlatformNotSupportedException("客户端自动更新首期只支持 Windows64 和 Android。");
            DistributionRuntimeConfig config = settings.CreateRuntimeConfig(platform, options.clientVersion);
            string extension = platform == IntegrationPlatform.Windows ? ".zip" : ".apk";
            string artifactRoot = Path.GetDirectoryName(
                DistributionReleaseLayout.ClientPath(options, extension)) ??
                                  DistributionReleaseLayout.PlatformRoot(options);
            Directory.CreateDirectory(artifactRoot);
            string packagePath;
            string packageType;
            string entry;
            if (platform == IntegrationPlatform.Windows)
            {
                WindowsUpdaterBuilder.BuildAndCopy(clientRoot);
                entry = PlayerSettings.productName + ".exe";
                packagePath = DistributionReleaseLayout.ClientPath(options, ".zip");
                string name = Path.GetFileName(packagePath);
                string temporary = Path.Combine(releaseRoot, "Reports", name + ".candidate");
                Directory.CreateDirectory(Path.GetDirectoryName(temporary) ?? releaseRoot);
                if (File.Exists(temporary)) File.Delete(temporary);
                ZipFile.CreateFromDirectory(clientRoot, temporary,
                    System.IO.Compression.CompressionLevel.Optimal, false);
                MergeImmutable(temporary, packagePath);
                File.Delete(temporary);
                packageType = "zip";
            }
            else
            {
                string apk = Directory.GetFiles(clientRoot, "*.apk", SearchOption.TopDirectoryOnly).SingleOrDefault();
                if (string.IsNullOrWhiteSpace(apk))
                    throw new FileNotFoundException("Android 客户端目录中没有唯一的 APK。", clientRoot);
                entry = string.Empty;
                packagePath = DistributionReleaseLayout.ClientPath(options, ".apk");
                MergeImmutable(apk, packagePath);
                packageType = "apk";
            }

            var info = new FileInfo(packagePath);
            var manifest = new ClientUpdateManifest
            {
                SchemaVersion = DistributionRuntimeConfig.CurrentSchemaVersion,
                Platform = platform.ToString(),
                Version = options.clientVersion,
                AndroidVersionCode = platform == IntegrationPlatform.Android
                    ? PlayerSettings.Android.bundleVersionCode : 0,
                PackageUrl = DistributionPathResolver.CombineUrl(config.cdnRoot,
                    $"clients/{Uri.EscapeDataString(info.Name)}"),
                PackageType = packageType,
                FileName = info.Name,
                SizeBytes = info.Length,
                Sha256 = ComputeSha256(packagePath),
                EntryExecutable = entry,
                PublishedAtUtc = DateTime.UtcNow.ToString("O"),
                Mandatory = true
            };
            string manifestPath = DistributionReleaseLayout.ClientManifestPath(options);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath) ?? artifactRoot);
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            return new ClientArtifactResult
            {
                Root = artifactRoot,
                PackagePath = packagePath,
                ManifestPath = manifestPath,
                Manifest = manifest,
                Artifacts = new[]
                {
                    ReleaseSnapshotMaterializer.CreateArtifact(packagePath, options, false, "Added", 30,
                        "immutable-year-range", false),
                    ReleaseSnapshotMaterializer.CreateArtifact(manifestPath, options, false, "Changed", 110,
                        "no-cache", true)
                }
            };
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void MergeImmutable(string source, string destination)
        {
            if (File.Exists(destination))
            {
                if (new FileInfo(source).Length != new FileInfo(destination).Length ||
                    !string.Equals(ComputeSha256(source), ComputeSha256(destination),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("客户端包版本目录不可覆盖：" + destination);
                return;
            }
            File.Copy(source, destination, false);
        }

        private static void RecreateDirectory(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Directory.CreateDirectory(path);
        }
    }
}
