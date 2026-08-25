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
            string baseUrl = settings.GetClientUpdateBaseUrl(platform);
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException($"{platform} 未配置客户端更新根地址。");

            string artifactRoot = Path.Combine(releaseRoot, "ClientUpdate");
            RecreateDirectory(artifactRoot);
            string packagePath;
            string packageType;
            string entry;
            if (platform == IntegrationPlatform.Windows)
            {
                WindowsUpdaterBuilder.BuildAndCopy(clientRoot);
                entry = PlayerSettings.productName + ".exe";
                string name = $"Client_Windows64_{options.clientVersion}.zip";
                packagePath = Path.Combine(artifactRoot, name);
                ZipFile.CreateFromDirectory(clientRoot, packagePath,
                    System.IO.Compression.CompressionLevel.Optimal, false);
                packageType = "zip";
            }
            else
            {
                string apk = Directory.GetFiles(clientRoot, "*.apk", SearchOption.TopDirectoryOnly).SingleOrDefault();
                if (string.IsNullOrWhiteSpace(apk))
                    throw new FileNotFoundException("Android 客户端目录中没有唯一的 APK。", clientRoot);
                entry = string.Empty;
                string name = $"Client_Android_{options.clientVersion}.apk";
                packagePath = Path.Combine(artifactRoot, name);
                File.Copy(apk, packagePath, true);
                packageType = "apk";
            }

            var info = new FileInfo(packagePath);
            var manifest = new ClientUpdateManifest
            {
                SchemaVersion = 1,
                Platform = platform.ToString(),
                Version = options.clientVersion,
                AndroidVersionCode = platform == IntegrationPlatform.Android
                    ? PlayerSettings.Android.bundleVersionCode : 0,
                PackageUrl = $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(options.clientVersion)}/" +
                             Uri.EscapeDataString(info.Name),
                PackageType = packageType,
                FileName = info.Name,
                SizeBytes = info.Length,
                Sha256 = ComputeSha256(packagePath),
                EntryExecutable = entry,
                PublishedAtUtc = DateTime.UtcNow.ToString("O"),
                Mandatory = true
            };
            string manifestPath = Path.Combine(artifactRoot, "latest.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            return new ClientArtifactResult
            {
                Root = artifactRoot,
                PackagePath = packagePath,
                ManifestPath = manifestPath,
                Manifest = manifest
            };
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void RecreateDirectory(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Directory.CreateDirectory(path);
        }
    }
}
