using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameIntegration.Editor
{
    internal sealed class FtpUploadOptions
    {
        public string Host = string.Empty;
        public int Port = 21;
        public string UserName = string.Empty;
        public string Password = string.Empty;
        public string HotUpdateDirectory = string.Empty;
        public string ClientDirectory = string.Empty;
        public bool EnableSsl;
        public bool UsePassive = true;
    }

    internal readonly struct FtpUploadProgress
    {
        public readonly string Stage;
        public readonly string FileName;
        public readonly int CompletedFiles;
        public readonly int TotalFiles;
        public readonly long UploadedBytes;
        public readonly long TotalBytes;
        public readonly bool UseFileCountProgress;

        public FtpUploadProgress(string fileName, int completedFiles, int totalFiles,
            long uploadedBytes, long totalBytes, string stage = null, bool useFileCountProgress = false)
        {
            Stage = string.IsNullOrWhiteSpace(stage) ? L("FTP 上传", "FTP Upload") : stage;
            FileName = fileName;
            CompletedFiles = completedFiles;
            TotalFiles = totalFiles;
            UploadedBytes = uploadedBytes;
            TotalBytes = totalBytes;
            UseFileCountProgress = useFileCountProgress;
        }

        public float Progress => UseFileCountProgress
            ? (TotalFiles <= 0 ? 1f : (float)CompletedFiles / TotalFiles)
            : (TotalBytes <= 0 ? 1f : (float)UploadedBytes / TotalBytes);

        private static string L(string chinese, string english)
        {
            return EditorLocalization.Text(chinese, english);
        }
    }

    internal static class FtpReleaseUploader
    {
        private const string RemoteHashIndexFileName = ".qhy-bundle-hashes.json";

        private sealed class UploadItem
        {
            public string LocalPath;
            public string RemotePath;
            public long Length;
            public string FinalRemotePath;
        }

        [Serializable]
        private sealed class BundleHashEntry
        {
            public string relativePath;
            public long length;
            public string sha256;
        }

        [Serializable]
        private sealed class BundleHashIndex
        {
            public int schemaVersion = 1;
            public string updatedUtc;
            public BundleHashEntry[] entries = Array.Empty<BundleHashEntry>();
        }

        private sealed class HashIndexContext
        {
            public BundleHashIndex Index;
            public Dictionary<string, BundleHashEntry> RemoteEntries;
            public Dictionary<string, BundleHashEntry> CachedEntries;
            public string CachePath;
            public bool Changed;
        }

        private sealed class PreparedUpload
        {
            public List<UploadItem> Items;
            public BundleHashIndex HashIndex;
            public string CachePath;
        }

        public static async Task UploadAsync(string releaseRoot, FtpUploadOptions options,
            bool includeClient, Func<long, int, bool> onPrepared,
            Action<FtpUploadProgress> onProgress, CancellationToken cancellationToken)
        {
            string cdnRoot = Path.Combine(releaseRoot, "CDN");
            if (!Directory.Exists(cdnRoot))
                throw new DirectoryNotFoundException(F("找不到热更新包目录：{0}。请先执行构建。",
                    "Hot-update package directory not found: {0}. Build it first.", cdnRoot));

            string planPath = Path.Combine(releaseRoot, "upload-plan.json");
            if (!File.Exists(planPath))
                throw new FileNotFoundException(L("找不到增量上传计划，请重新构建。",
                    "Incremental upload plan is missing. Rebuild the release."), planPath);
            ReleaseUploadPlan plan = JsonUtility.FromJson<ReleaseUploadPlan>(File.ReadAllText(planPath));
            if (plan == null || string.IsNullOrWhiteSpace(plan.resourceVersion))
                throw new InvalidDataException("Invalid upload-plan.json.");
            if (!includeClient && !ReleaseContentChangeDetector.HasChanges(plan))
                throw new NoReleaseContentChangesException(L(
                    "上传计划中没有热更 DLL 或 YooAsset 资源变化，已阻止空更新上传。",
                    "The upload plan contains no hot-update DLL or YooAsset content changes. Empty update upload was blocked."));
            bool resourceAlreadyPublished = ReleaseBaselineStore.IsAlreadyPublished(releaseRoot, plan);
            bool clientAlreadyPublished = includeClient &&
                                          ReleaseBaselineStore.IsClientAlreadyPublished(releaseRoot, plan);
            if (resourceAlreadyPublished && (!includeClient || clientAlreadyPublished))
                throw new ReleaseAlreadyPublishedException(F(
                    "资源版本 {0} 已上传成功，不能重复发布。请先构建新的 ResourceVersion；若此前执行过回滚，可从回滚后的基线重新发布。",
                    "Resource version {0} has already been uploaded successfully and cannot be published again. Build a new ResourceVersion first; a rolled-back baseline may be republished.",
                    plan.resourceVersion));
            Validate(options);
            PreparedUpload prepared = null;
            var items = new List<UploadItem>();
            if (!resourceAlreadyPublished)
            {
                prepared = await CollectPlannedFilesAsync(releaseRoot, cdnRoot,
                    options.HotUpdateDirectory, plan, options, onProgress, cancellationToken);
                items.AddRange(prepared.Items);
                AddAuditFile(items, planPath, options.HotUpdateDirectory);
                AddAuditFile(items, Path.Combine(releaseRoot, "release-report.json"),
                    options.HotUpdateDirectory);
                AddAuditFile(items, Path.Combine(releaseRoot, "artifacts.sha256"),
                    options.HotUpdateDirectory);
            }
            if (includeClient)
            {
                string clientUpdateRoot = Path.Combine(releaseRoot, "ClientUpdate");
                if (!Directory.Exists(clientUpdateRoot))
                    throw new DirectoryNotFoundException(F("找不到客户端包目录：{0}。请先执行完整客户端构建。",
                        "Client package directory not found: {0}. Run Full Package Build first.", clientUpdateRoot));
                string manifestPath = Path.Combine(clientUpdateRoot, "latest.json");
                ClientUpdateManifest manifest = JsonUtility.FromJson<ClientUpdateManifest>(
                    File.ReadAllText(manifestPath));
                string clientRoot = GetRemoteParent(options.ClientDirectory);
                await EnsureNewerThanRemoteAsync(options, CombineRemote(clientRoot, "latest.json"),
                    manifest.Version, cancellationToken);
                items.AddRange(CollectClientUpdateFiles(clientUpdateRoot, options.ClientDirectory, clientRoot));
            }

            items = items.OrderBy(item => GetYooAssetUploadPriority(
                    string.IsNullOrWhiteSpace(item.FinalRemotePath) ? item.RemotePath : item.FinalRemotePath))
                .ThenBy(item => item.RemotePath, StringComparer.OrdinalIgnoreCase).ToList();

            long totalBytes = items.Sum(item => item.Length);
            if (onPrepared != null && !onPrepared(totalBytes, items.Count))
                throw new OperationCanceledException(cancellationToken);
            long uploadedBytes = 0;
            var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UploadItem item = items[index];
                string parent = GetRemoteParent(item.RemotePath);
                await EnsureDirectoryAsync(options, parent, createdDirectories, cancellationToken);
                await UploadFileAsync(options, item, value =>
                {
                    onProgress?.Invoke(new FtpUploadProgress(Path.GetFileName(item.LocalPath), index,
                        items.Count, uploadedBytes + value, totalBytes));
                }, cancellationToken);
                if (!string.IsNullOrWhiteSpace(item.FinalRemotePath))
                    await PublishLatestManifestAsync(options, item.RemotePath, item.FinalRemotePath,
                        cancellationToken);
                uploadedBytes += item.Length;
                onProgress?.Invoke(new FtpUploadProgress(Path.GetFileName(item.LocalPath), index + 1,
                    items.Count, uploadedBytes, totalBytes));
            }
            if (prepared != null)
                SaveHashIndexCache(prepared.CachePath, prepared.HashIndex);
            if (!resourceAlreadyPublished)
                ReleaseBaselineStore.SavePublished(releaseRoot, plan);
            if (includeClient)
                ReleaseBaselineStore.MarkClientPublished(releaseRoot, plan);
        }

        public static async Task RollbackAsync(string targetReleaseRoot, FtpUploadOptions options,
            Action<FtpUploadProgress> onProgress, CancellationToken cancellationToken)
        {
            Validate(options);
            string planPath = Path.Combine(targetReleaseRoot, "upload-plan.json");
            if (!File.Exists(planPath)) throw new FileNotFoundException("upload-plan.json is missing.", planPath);
            ReleaseUploadPlan target = JsonUtility.FromJson<ReleaseUploadPlan>(File.ReadAllText(planPath));
            if (target == null || string.IsNullOrWhiteSpace(target.resourceVersion))
                throw new InvalidDataException("Invalid rollback target upload-plan.json.");
            ResourceReleaseBaseline baseline = ReleaseBaselineStore.LoadForRelease(targetReleaseRoot, target);
            if (baseline == null || string.IsNullOrWhiteSpace(baseline.resourceVersion))
                throw new InvalidOperationException(L("没有已发布资源基线，不能执行回滚。",
                    "No published resource baseline exists; rollback is unavailable."));
            if (string.Equals(baseline.resourceVersion, target.resourceVersion,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(F("资源版本 {0} 已经是当前远端版本。",
                    "Resource version {0} is already the active remote version.", target.resourceVersion));
            string highest = string.IsNullOrWhiteSpace(baseline.highestResourceVersion)
                ? baseline.resourceVersion
                : baseline.highestResourceVersion;
            if (!ResourceVersionResolver.TryGetRevision(highest, target.clientVersion,
                    out int highestRevision) ||
                !ResourceVersionResolver.TryGetRevision(target.resourceVersion, target.clientVersion,
                    out int targetRevision) || targetRevision > highestRevision)
                throw new InvalidOperationException(F("资源版本 {0} 不在已发布历史范围内。",
                    "Resource version {0} is outside the published history.", target.resourceVersion));

            string cdnRoot = Path.Combine(targetReleaseRoot, "CDN");
            string versionName = Directory.GetFiles(cdnRoot, "*.version",
                SearchOption.TopDirectoryOnly).Select(Path.GetFileName).Single();
            string localVersion = Path.Combine(cdnRoot, versionName);
            if (!string.Equals(File.ReadAllText(localVersion).Trim(), target.resourceVersion,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(F("本地 version 指针与回滚目标 {0} 不一致。",
                    "The local version pointer does not match rollback target {0}.",
                    target.resourceVersion));
            await ValidateRemoteRollbackTargetAsync(cdnRoot, target, options, onProgress,
                cancellationToken);
            string finalRemote = CombineRemote(options.HotUpdateDirectory, versionName);
            string temporary = finalRemote + ".uploading";
            var item = new UploadItem
            {
                LocalPath = localVersion,
                RemotePath = temporary,
                FinalRemotePath = finalRemote,
                Length = new FileInfo(localVersion).Length
            };
            var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await EnsureDirectoryAsync(options, GetRemoteParent(temporary), created, cancellationToken);
            await UploadFileAsync(options, item, null, cancellationToken);
            await PublishLatestManifestAsync(options, temporary, finalRemote, cancellationToken);
            ReleaseBaselineStore.SaveRollback(targetReleaseRoot, target, highest);
        }

        private static async Task ValidateRemoteRollbackTargetAsync(string cdnRoot, ReleaseUploadPlan plan,
            FtpUploadOptions options, Action<FtpUploadProgress> onProgress,
            CancellationToken cancellationToken)
        {
            HashIndexContext hashes = await LoadHashIndexAsync(options, options.HotUpdateDirectory,
                onProgress, cancellationToken);
            UploadArtifact[] artifacts = (plan.added ?? Array.Empty<UploadArtifact>())
                .Concat(plan.changed ?? Array.Empty<UploadArtifact>())
                .Concat(plan.unchanged ?? Array.Empty<UploadArtifact>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.relativePath))
                .GroupBy(item => item.relativePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToArray();
            if (!artifacts.Any(item => item.contentAddressedBundle) ||
                !artifacts.Any(item => Path.GetExtension(item.relativePath)
                    .Equals(".bytes", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(F("回滚目标 {0} 的上传计划不完整，缺少 Manifest 或 Bundle。",
                    "Rollback target {0} has an incomplete upload plan with no manifest or Bundle.",
                    plan.resourceVersion));
            for (int index = 0; index < artifacts.Length; index++)
            {
                UploadArtifact artifact = artifacts[index];
                cancellationToken.ThrowIfCancellationRequested();
                onProgress?.Invoke(new FtpUploadProgress(artifact.relativePath, index,
                    artifacts.Length, 0, 0, L("验证远端回滚版本", "Validating Remote Rollback Target"),
                    true));
                string extension = Path.GetExtension(artifact.relativePath);
                if (extension.Equals(".version", StringComparison.OrdinalIgnoreCase))
                {
                    ReportRollbackValidationProgress(onProgress, artifact, index + 1, artifacts.Length);
                    continue;
                }
                string remotePath = CombineRemote(options.HotUpdateDirectory, artifact.relativePath);
                if (artifact.contentAddressedBundle)
                {
                    if (!await VerifyOrRejectExistingBundleAsync(options, remotePath, artifact, hashes,
                            cancellationToken))
                        throw new FileNotFoundException(F("远端缺少回滚版本 {0} 所需的 Bundle：{1}",
                            "Remote rollback target {0} is missing required Bundle: {1}",
                            plan.resourceVersion, artifact.relativePath));
                    ReportRollbackValidationProgress(onProgress, artifact, index + 1, artifacts.Length);
                    continue;
                }

                if (!extension.Equals(".bytes", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".hash", StringComparison.OrdinalIgnoreCase))
                {
                    ReportRollbackValidationProgress(onProgress, artifact, index + 1, artifacts.Length);
                    continue;
                }
                string localPath = Path.GetFullPath(Path.Combine(cdnRoot,
                    artifact.relativePath.Replace('/', Path.DirectorySeparatorChar)));
                string safeRoot = Path.GetFullPath(cdnRoot).TrimEnd(Path.DirectorySeparatorChar) +
                                  Path.DirectorySeparatorChar;
                if (!localPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(localPath))
                    throw new FileNotFoundException("Rollback target artifact is missing or unsafe.", localPath);
                string remoteSha = await ComputeRemoteSha256Async(options, remotePath, cancellationToken);
                if (!string.Equals(remoteSha, artifact.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(F("远端回滚文件内容不匹配：{0}",
                        "Remote rollback artifact content mismatch: {0}", artifact.relativePath));
                ReportRollbackValidationProgress(onProgress, artifact, index + 1, artifacts.Length);
            }
        }

        private static void ReportRollbackValidationProgress(Action<FtpUploadProgress> onProgress,
            UploadArtifact artifact, int completed, int total)
        {
            onProgress?.Invoke(new FtpUploadProgress(artifact.relativePath, completed, total, 0, 0,
                L("验证远端回滚版本", "Validating Remote Rollback Target"), true));
        }

        private static async Task<PreparedUpload> CollectPlannedFilesAsync(string releaseRoot,
            string localRoot, string remoteRoot, ReleaseUploadPlan plan, FtpUploadOptions options,
            Action<FtpUploadProgress> onProgress, CancellationToken cancellationToken)
        {
            var result = new List<UploadItem>();
            HashIndexContext hashes = await LoadHashIndexAsync(options, remoteRoot, onProgress,
                cancellationToken);
            UploadArtifact[] planned = (plan.added ?? Array.Empty<UploadArtifact>())
                .Concat(plan.changed ?? Array.Empty<UploadArtifact>()).ToArray();
            for (int index = 0; index < planned.Length; index++)
            {
                UploadArtifact artifact = planned[index];
                cancellationToken.ThrowIfCancellationRequested();
                onProgress?.Invoke(new FtpUploadProgress(artifact.relativePath, index,
                    planned.Length, 0, 0, L("分析 FTP 远端文件", "Analyzing Remote FTP Files"), true));
                string localPath = Path.GetFullPath(Path.Combine(localRoot,
                    artifact.relativePath.Replace('/', Path.DirectorySeparatorChar)));
                string safeRoot = Path.GetFullPath(localRoot).TrimEnd(Path.DirectorySeparatorChar) +
                                  Path.DirectorySeparatorChar;
                if (!localPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(localPath))
                    throw new FileNotFoundException("Planned upload artifact is missing or unsafe.", localPath);
                string remotePath = CombineRemote(remoteRoot, artifact.relativePath);
                if (artifact.contentAddressedBundle &&
                    await VerifyOrRejectExistingBundleAsync(options, remotePath, artifact, hashes,
                        cancellationToken))
                {
                    onProgress?.Invoke(new FtpUploadProgress(artifact.relativePath, index + 1,
                        planned.Length, 0, 0, L("分析 FTP 远端文件", "Analyzing Remote FTP Files"), true));
                    continue;
                }
                bool versionPointer = Path.GetExtension(artifact.relativePath)
                    .Equals(".version", StringComparison.OrdinalIgnoreCase);
                result.Add(new UploadItem
                {
                    LocalPath = localPath,
                    RemotePath = versionPointer ? remotePath + ".uploading" : remotePath,
                    FinalRemotePath = versionPointer ? remotePath : string.Empty,
                    Length = artifact.length
                });
                if (artifact.contentAddressedBundle)
                {
                    UpsertHashEntry(hashes.RemoteEntries, artifact);
                    hashes.Changed = true;
                }
                onProgress?.Invoke(new FtpUploadProgress(artifact.relativePath, index + 1,
                    planned.Length, 0, 0, L("分析 FTP 远端文件", "Analyzing Remote FTP Files"), true));
            }
            hashes.Index.entries = hashes.RemoteEntries.Values
                .OrderBy(entry => entry.relativePath, StringComparer.OrdinalIgnoreCase).ToArray();
            hashes.Index.updatedUtc = DateTime.UtcNow.ToString("O");
            if (hashes.Changed)
            {
                string localIndex = Path.Combine(releaseRoot, "ftp-bundle-hashes.json");
                File.WriteAllText(localIndex, JsonUtility.ToJson(hashes.Index, true),
                    new UTF8Encoding(false));
                string finalRemote = CombineRemote(remoteRoot, RemoteHashIndexFileName);
                result.Add(new UploadItem
                {
                    LocalPath = localIndex,
                    RemotePath = finalRemote + ".uploading",
                    FinalRemotePath = finalRemote,
                    Length = new FileInfo(localIndex).Length
                });
            }

            return new PreparedUpload
            {
                Items = result.OrderBy(item => GetYooAssetUploadPriority(item.FinalRemotePath.Length > 0
                        ? item.FinalRemotePath
                        : item.RemotePath))
                    .ThenBy(item => item.RemotePath, StringComparer.OrdinalIgnoreCase).ToList(),
                HashIndex = hashes.Index,
                CachePath = hashes.CachePath
            };
        }

        private static void AddAuditFile(List<UploadItem> items, string path, string remoteRoot)
        {
            if (!File.Exists(path)) return;
            items.Add(new UploadItem
            {
                LocalPath = path,
                RemotePath = CombineRemote(remoteRoot, Path.GetFileName(path)),
                Length = new FileInfo(path).Length
            });
        }

        private static async Task<bool> VerifyOrRejectExistingBundleAsync(FtpUploadOptions options,
            string remotePath, UploadArtifact artifact, HashIndexContext hashes,
            CancellationToken cancellationToken)
        {
            long remoteLength;
            try
            {
                FtpWebRequest size = CreateRequest(options, remotePath, WebRequestMethods.Ftp.GetFileSize);
                using FtpWebResponse response = (FtpWebResponse)await size.GetResponseAsync();
                remoteLength = response.ContentLength;
            }
            catch (WebException exception) when (exception.Response is FtpWebResponse response &&
                                                  response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
            {
                return false;
            }

            if (remoteLength != artifact.length)
                throw new InvalidDataException(
                    $"Remote content-addressed bundle collision: {artifact.relativePath} has length {remoteLength}, expected {artifact.length}.");

            if (hashes.RemoteEntries.TryGetValue(artifact.relativePath, out BundleHashEntry indexed))
            {
                ValidateHashEntry(indexed, artifact, "remote hash index");
                return true;
            }

            if (hashes.CachedEntries.TryGetValue(artifact.relativePath, out BundleHashEntry cached))
            {
                if (cached.length == artifact.length && string.Equals(cached.sha256, artifact.sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    UpsertHashEntry(hashes.RemoteEntries, artifact);
                    hashes.Changed = true;
                    return true;
                }
                hashes.CachedEntries.Remove(artifact.relativePath);
            }

            string remoteSha = await ComputeRemoteSha256Async(options, remotePath, cancellationToken);
            if (!string.Equals(remoteSha, artifact.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Remote content-addressed bundle collision: {artifact.relativePath} has a different SHA-256.");
            UpsertHashEntry(hashes.RemoteEntries, artifact);
            UpsertHashEntry(hashes.CachedEntries, artifact);
            hashes.Changed = true;
            SaveHashIndexCache(hashes.CachePath, new BundleHashIndex
            {
                updatedUtc = DateTime.UtcNow.ToString("O"),
                entries = hashes.CachedEntries.Values.OrderBy(entry => entry.relativePath,
                    StringComparer.OrdinalIgnoreCase).ToArray()
            });
            return true;
        }

        private static async Task<HashIndexContext> LoadHashIndexAsync(FtpUploadOptions options,
            string remoteRoot, Action<FtpUploadProgress> onProgress,
            CancellationToken cancellationToken)
        {
            string cachePath = GetHashIndexCachePath(options, remoteRoot);
            BundleHashIndex cached = LoadHashIndexFile(cachePath, false) ?? new BundleHashIndex();
            BundleHashIndex remote = null;
            onProgress?.Invoke(new FtpUploadProgress(RemoteHashIndexFileName, 0, 1, 0, 0,
                L("下载远端哈希索引", "Downloading Remote Hash Index"), true));
            try
            {
                string json = await DownloadTextAsync(options,
                    CombineRemote(remoteRoot, RemoteHashIndexFileName), cancellationToken);
                remote = JsonUtility.FromJson<BundleHashIndex>(json);
                if (remote == null || remote.schemaVersion != 1)
                    throw new InvalidDataException("The remote QHY bundle hash index is invalid or unsupported.");
            }
            catch (WebException exception) when (exception.Response is FtpWebResponse response &&
                                                  response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
            {
                remote = new BundleHashIndex();
            }
            onProgress?.Invoke(new FtpUploadProgress(RemoteHashIndexFileName, 1, 1, 0, 0,
                L("下载远端哈希索引", "Downloading Remote Hash Index"), true));

            return new HashIndexContext
            {
                Index = remote,
                RemoteEntries = ToEntryMap(remote.entries),
                CachedEntries = ToEntryMap(cached.entries),
                CachePath = cachePath,
                Changed = remote.entries == null || remote.entries.Length == 0
            };
        }

        private static Dictionary<string, BundleHashEntry> ToEntryMap(IEnumerable<BundleHashEntry> entries)
        {
            return (entries ?? Array.Empty<BundleHashEntry>())
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.relativePath))
                .GroupBy(entry => entry.relativePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        }

        private static void ValidateHashEntry(BundleHashEntry entry, UploadArtifact artifact, string source)
        {
            if (entry.length != artifact.length || !string.Equals(entry.sha256, artifact.sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Remote content-addressed bundle collision: " +
                                               $"{artifact.relativePath} differs from the {source}.");
        }

        private static void UpsertHashEntry(IDictionary<string, BundleHashEntry> entries,
            UploadArtifact artifact)
        {
            entries[artifact.relativePath] = new BundleHashEntry
            {
                relativePath = artifact.relativePath,
                length = artifact.length,
                sha256 = artifact.sha256
            };
        }

        private static BundleHashIndex LoadHashIndexFile(string path, bool required)
        {
            try
            {
                if (!File.Exists(path)) return null;
                BundleHashIndex value = JsonUtility.FromJson<BundleHashIndex>(File.ReadAllText(path));
                return value != null && value.schemaVersion == 1 ? value : null;
            }
            catch when (!required)
            {
                return null;
            }
        }

        private static string GetHashIndexCachePath(FtpUploadOptions options, string remoteRoot)
        {
            string identity = string.Join("|", options.EnableSsl, options.Host?.Trim().ToLowerInvariant(),
                options.Port, options.UserName?.Trim().ToLowerInvariant(), remoteRoot?.Trim().ToLowerInvariant());
            byte[] bytes;
            using (SHA256 sha = SHA256.Create())
                bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
            string key = BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            return Path.GetFullPath(Path.Combine("Library", "QHYFramework", "FtpVerificationCache",
                key + ".json"));
        }

        private static void SaveHashIndexCache(string path, BundleHashIndex index)
        {
            if (index == null || string.IsNullOrWhiteSpace(path)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Library");
                File.WriteAllText(path, JsonUtility.ToJson(index, true), new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QHYFramework] Failed to update the local FTP verification cache: " +
                                 exception.GetBaseException().Message);
            }
        }

        private static async Task<string> ComputeRemoteSha256Async(FtpUploadOptions options, string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FtpWebRequest request = CreateRequest(options, path, WebRequestMethods.Ftp.DownloadFile);
            using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
            using Stream stream = response.GetResponseStream();
            using SHA256 sha = SHA256.Create();
            byte[] buffer = new byte[64 * 1024];
            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read <= 0) break;
                sha.TransformBlock(buffer, 0, read, buffer, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static List<UploadItem> CollectClientUpdateFiles(string root, string versionDirectory,
            string clientRoot)
        {
            var result = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !path.EndsWith("latest.json", StringComparison.OrdinalIgnoreCase))
                .Select(path => new UploadItem
                {
                    LocalPath = path,
                    RemotePath = CombineRemote(versionDirectory, Path.GetFileName(path)),
                    Length = new FileInfo(path).Length
                }).ToList();
            string manifest = Path.Combine(root, "latest.json");
            if (!File.Exists(manifest)) throw new FileNotFoundException("客户端 latest.json 不存在。", manifest);
            string finalPath = CombineRemote(clientRoot, "latest.json");
            result.Add(new UploadItem
            {
                LocalPath = manifest,
                RemotePath = finalPath + ".uploading",
                FinalRemotePath = finalPath,
                Length = new FileInfo(manifest).Length
            });
            return result;
        }

        private static async Task EnsureNewerThanRemoteAsync(FtpUploadOptions options, string remoteManifest,
            string localVersion, CancellationToken cancellationToken)
        {
            string json;
            try { json = await DownloadTextAsync(options, remoteManifest, cancellationToken); }
            catch (WebException exception) when (exception.Response is FtpWebResponse response &&
                                                  response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
            { return; }
            ClientUpdateManifest remote = JsonUtility.FromJson<ClientUpdateManifest>(json);
            ClientVersion localVersionValue = ClientVersion.Parse(localVersion);
            ClientVersion remoteVersionValue = ClientVersion.Parse(remote.Version);
            if (localVersionValue.CompareTo(remoteVersionValue) <= 0)
                throw new InvalidOperationException(F(
                    "客户端版本必须高于服务器 latest.json。当前：{0}，服务器：{1}",
                    "Client version must be newer than server latest.json. Local: {0}, server: {1}",
                    localVersion, remote.Version));
        }

        private static async Task<string> DownloadTextAsync(FtpUploadOptions options, string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FtpWebRequest request = CreateRequest(options, path, WebRequestMethods.Ftp.DownloadFile);
            using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
            using Stream stream = response.GetResponseStream();
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        private static async Task PublishLatestManifestAsync(FtpUploadOptions options, string temporary,
            string finalPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string backup = finalPath + ".previous";
            bool movedOld = false;
            try
            {
                try
                {
                    await DeleteIfExistsAsync(options, backup);
                    await RenameAsync(options, finalPath, backup);
                    movedOld = true;
                }
                catch (WebException exception) when (exception.Response is FtpWebResponse response &&
                                                      response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                { }
                await RenameAsync(options, temporary, finalPath);
                if (movedOld) await DeleteIfExistsAsync(options, backup);
            }
            catch
            {
                if (movedOld)
                {
                    await DeleteIfExistsAsync(options, finalPath);
                    await RenameAsync(options, backup, finalPath);
                }
                throw;
            }
        }

        private static async Task DeleteIfExistsAsync(FtpUploadOptions options, string path)
        {
            try
            {
                FtpWebRequest delete = CreateRequest(options, path, WebRequestMethods.Ftp.DeleteFile);
                using FtpWebResponse response = (FtpWebResponse)await delete.GetResponseAsync();
            }
            catch (WebException exception) when (exception.Response is FtpWebResponse response &&
                                                  response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
            { }
        }

        private static async Task RenameAsync(FtpUploadOptions options, string source, string destination)
        {
            FtpWebRequest request = CreateRequest(options, source, WebRequestMethods.Ftp.Rename);
            request.RenameTo = destination;
            using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
        }

        private static List<UploadItem> CollectFiles(string localRoot, string remoteRoot, bool yooAssetPackage)
        {
            return Directory.GetFiles(localRoot, "*", SearchOption.AllDirectories)
                // 同版本覆盖时先上传 bundle，再上传清单内容，最后替换 hash。
                // 客户端只有读到新 hash 后才会开始使用新清单，避免发布中间态。
                .OrderBy(path => yooAssetPackage ? GetYooAssetUploadPriority(path) : 0)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new UploadItem
                {
                    LocalPath = path,
                    RemotePath = CombineRemote(remoteRoot,
                        path.Substring(localRoot.Length).TrimStart(Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar).Replace('\\', '/')),
                    Length = new FileInfo(path).Length
                }).ToList();
        }

        private static async Task<List<UploadItem>> CollectClientFilesAsync(string releaseRoot,
            string clientRoot, string remoteRoot, Action<FtpUploadProgress> onProgress,
            CancellationToken cancellationToken)
        {
            string[] files = Directory.GetFiles(clientRoot, "*", SearchOption.AllDirectories)
                .Where(path => !IsDoNotShipPath(GetRelativePath(clientRoot, path)))
                .ToArray();
            if (files.Length == 0)
                throw new FileNotFoundException(F("客户端包目录为空：{0}",
                    "Client package directory is empty: {0}", clientRoot));

            string singleRelativePath = files.Length == 1 ? GetRelativePath(clientRoot, files[0]) : string.Empty;
            if (files.Length == 1 && singleRelativePath.IndexOfAny(new[] { '/', '\\' }) < 0)
            {
                return new List<UploadItem>
                {
                    new UploadItem
                    {
                        LocalPath = files[0],
                        RemotePath = CombineRemote(remoteRoot, Path.GetFileName(files[0])),
                        Length = new FileInfo(files[0]).Length
                    }
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            var releaseDirectory = new DirectoryInfo(releaseRoot);
            DirectoryInfo clientVersionDirectory = string.Equals(releaseDirectory.Parent?.Name, "Revisions",
                StringComparison.OrdinalIgnoreCase)
                ? releaseDirectory.Parent?.Parent
                : releaseDirectory.Parent;
            string version = clientVersionDirectory?.Name ?? releaseDirectory.Name;
            string platform = clientVersionDirectory?.Parent?.Name ?? "unknown";
            string archiveName = $"Client_{SanitizeFileName(platform)}_{SanitizeFileName(version)}.zip";
            string archiveRoot = Path.Combine(releaseRoot, "Upload");
            string archivePath = Path.Combine(archiveRoot, archiveName);
            Directory.CreateDirectory(archiveRoot);
            if (File.Exists(archivePath))
                File.Delete(archivePath);

            onProgress?.Invoke(new FtpUploadProgress(L("正在压缩客户端…", "Compressing client…"),
                0, 1, 0, 1));
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                CreateClientArchive(clientRoot, archivePath, files, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }, cancellationToken);

            return new List<UploadItem>
            {
                new UploadItem
                {
                    LocalPath = archivePath,
                    RemotePath = CombineRemote(remoteRoot, archiveName),
                    Length = new FileInfo(archivePath).Length
                }
            };
        }

        private static void CreateClientArchive(string clientRoot, string archivePath, IEnumerable<string> files,
            CancellationToken cancellationToken)
        {
            using var archiveStream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 64 * 1024);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create);
            var buffer = new byte[64 * 1024];
            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = GetRelativePath(clientRoot, file).Replace('\\', '/');
                ZipArchiveEntry entry = archive.CreateEntry(relativePath,
                    System.IO.Compression.CompressionLevel.Optimal);
                using Stream entryStream = entry.Open();
                using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                    64 * 1024);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = source.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;
                    entryStream.Write(buffer, 0, read);
                }
            }
        }

        private static bool IsDoNotShipPath(string relativePath)
        {
            string[] segments = relativePath.Replace('\\', '/').Split('/');
            return segments.Any(segment =>
                segment.IndexOf("BackUpThisFolder_ButDontShipItWithYourGame",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                segment.IndexOf("BurstDebugInformation_DoNotShip", StringComparison.OrdinalIgnoreCase) >= 0 ||
                segment.IndexOf("DoNotShip", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetRelativePath(string root, string path)
        {
            return path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return new string((value ?? string.Empty)
                .Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
        }

        private static int GetYooAssetUploadPriority(string path)
        {
            string extension = Path.GetExtension(path);
            if (extension.Equals(".bytes", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".report", StringComparison.OrdinalIgnoreCase))
                return extension.Equals(".report", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
            if (extension.Equals(".hash", StringComparison.OrdinalIgnoreCase))
                return 2;
            if (extension.Equals(".version", StringComparison.OrdinalIgnoreCase))
                return 4;
            return 0;
        }

        private static async Task UploadFileAsync(FtpUploadOptions options, UploadItem item,
            Action<long> onUploaded, CancellationToken cancellationToken)
        {
            FtpWebRequest request = CreateRequest(options, item.RemotePath, WebRequestMethods.Ftp.UploadFile);
            request.ContentLength = item.Length;
            // 必须先关闭数据流再读取最终响应，否则部分 FTP 服务只会返回 150 Accepted data connection。
            using (Stream requestStream = await request.GetRequestStreamAsync())
            using (var fileStream = new FileStream(item.LocalPath, FileMode.Open, FileAccess.Read,
                       FileShare.Read, 64 * 1024, true))
            {
                var buffer = new byte[64 * 1024];
                long uploaded = 0;
                while (true)
                {
                    int read = await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (read <= 0)
                        break;
                    await requestStream.WriteAsync(buffer, 0, read, cancellationToken);
                    uploaded += read;
                    onUploaded?.Invoke(uploaded);
                }
            }

            // FTP 4xx/5xx 会由 GetResponseAsync 直接抛出 WebException；不要把合法的 1xx/2xx/3xx 判为失败。
            using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
        }

        private static async Task EnsureDirectoryAsync(FtpUploadOptions options, string path,
            HashSet<string> created, CancellationToken cancellationToken)
        {
            string current = string.Empty;
            foreach (string segment in SplitRemote(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                current += "/" + segment;
                if (!created.Add(current))
                    continue;
                try
                {
                    FtpWebRequest request = CreateRequest(options, current, WebRequestMethods.Ftp.MakeDirectory);
                    using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
                }
                catch (WebException exception) when (DirectoryProbablyExists(exception))
                {
                    // 大多数 FTP 服务对已存在目录返回 550，后续上传会验证目录是否真正可用。
                }
            }
        }

        private static FtpWebRequest CreateRequest(FtpUploadOptions options, string remotePath, string method)
        {
            string host = options.Host.Trim();
            if (host.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring("ftp://".Length);
            host = host.TrimEnd('/');
            string escapedPath = string.Join("/", SplitRemote(remotePath).Select(Uri.EscapeDataString));
            var request = (FtpWebRequest)WebRequest.Create($"ftp://{host}:{options.Port}/{escapedPath}");
            request.Method = method;
            request.Credentials = new NetworkCredential(options.UserName, options.Password);
            request.UseBinary = true;
            request.UsePassive = options.UsePassive;
            request.EnableSsl = options.EnableSsl;
            request.KeepAlive = false;
            return request;
        }

        private static bool DirectoryProbablyExists(WebException exception)
        {
            return exception.Response is FtpWebResponse response &&
                   response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable;
        }

        private static string CombineRemote(string root, string relative)
        {
            return "/" + root.Trim().Trim('/').Replace('\\', '/') + "/" + relative.TrimStart('/');
        }

        private static string GetRemoteParent(string path)
        {
            int index = path.LastIndexOf('/');
            return index <= 0 ? string.Empty : path.Substring(0, index);
        }

        private static IEnumerable<string> SplitRemote(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Split(new[] { '/' },
                StringSplitOptions.RemoveEmptyEntries);
        }

        private static void Validate(FtpUploadOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.Host))
                throw new ArgumentException(L("FTP 主机不能为空。", "FTP Host cannot be empty."));
            if (options.Port <= 0 || options.Port > 65535)
                throw new ArgumentException(L("FTP 端口无效。", "FTP Port is invalid."));
            if (string.IsNullOrWhiteSpace(options.UserName))
                throw new ArgumentException(L("FTP 用户名不能为空。", "FTP User Name cannot be empty."));
            if (string.IsNullOrWhiteSpace(options.HotUpdateDirectory))
                throw new ArgumentException(L("热更新远端目录不能为空。",
                    "Hot Update Remote Directory cannot be empty."));
            if (string.IsNullOrWhiteSpace(options.ClientDirectory))
                throw new ArgumentException(L("客户端远端目录不能为空。",
                    "Client Remote Directory cannot be empty."));
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
