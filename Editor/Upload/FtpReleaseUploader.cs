using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
        public bool EnableSsl;
        public bool UsePassive = true;
        public int Concurrency = 4;
        public int RetryCount = 2;
    }

    internal readonly struct FtpUploadProgress
    {
        public readonly string Stage, FileName;
        public readonly int CompletedFiles, TotalFiles;
        public readonly long UploadedBytes, TotalBytes;
        public readonly bool UseFileCountProgress;
        public FtpUploadProgress(string fileName, int completedFiles, int totalFiles, long uploadedBytes,
            long totalBytes, string stage = null, bool useFileCountProgress = false)
        { Stage = stage ?? "FTP Upload"; FileName = fileName; CompletedFiles = completedFiles;
          TotalFiles = totalFiles; UploadedBytes = uploadedBytes; TotalBytes = totalBytes;
          UseFileCountProgress = useFileCountProgress; }
        public float Progress => UseFileCountProgress ? (TotalFiles == 0 ? 1 : (float)CompletedFiles / TotalFiles)
            : (TotalBytes == 0 ? 1 : (float)UploadedBytes / TotalBytes);
    }

    /// <summary>schema v5 游戏目录/CDN/Origin Releases 镜像 FTP/FTPS 上传器。</summary>
    internal static class FtpReleaseUploader
    {
        // 文件可以并行上传，但 FTP 目录必须按父子顺序创建。该锁同时避免多个上传任务在
        // MKD 完成前把同一路径误判为已准备完成。
        private static readonly SemaphoreSlim DirectoryCreationGate = new SemaphoreSlim(1, 1);

        public static async Task UploadAsync(string buildRoot, FtpUploadOptions options, bool includeClient,
            Func<long, int, bool> onPrepared, Action<FtpUploadProgress> onProgress,
            CancellationToken cancellationToken)
        {
            Validate(options);
            string planPath = Path.Combine(buildRoot, "publish-plan.json");
            if (!File.Exists(planPath)) throw new FileNotFoundException("publish-plan.json is missing.", planPath);
            ReleaseUploadPlan plan = JsonUtility.FromJson<ReleaseUploadPlan>(File.ReadAllText(planPath));
            if (plan == null || plan.schemaVersion != DistributionRuntimeConfig.CurrentSchemaVersion)
                throw new InvalidDataException("publish-plan.json is not schema v5.");
            if (ReleaseBaselineStore.IsAlreadyPublished(buildRoot, plan))
                throw new ReleaseAlreadyPublishedException("该 ResourceVersion 已登记为 Published，禁止重复上传。");

            ReleaseBaselineStore.SaveUploading(buildRoot, plan);
            Dictionary<string, QhyBundleIndexEntry> remoteObjects = await LoadRemoteIndexAsync(options,
                plan, cancellationToken);
            UploadArtifact[] plannedArtifacts = (plan.added ?? Array.Empty<UploadArtifact>())
                .Concat(plan.changed ?? Array.Empty<UploadArtifact>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.relativePath))
                .Where(x => includeClient || !x.resourcePath.StartsWith("cdn/clients/", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.relativePath, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray();
            HashSet<string> verifiedRemoteBundles = await VerifyIndexedRemoteBundlesAsync(options,
                plannedArtifacts, remoteObjects, onProgress, cancellationToken);
            UploadArtifact[] candidates = plannedArtifacts
                .Where(x => !x.contentAddressedBundle || !verifiedRemoteBundles.Contains(x.relativePath))
                .ToArray();
            var ordinary = candidates.Where(x => !x.pointer).OrderBy(x => x.relativePath).ToArray();
            var cdnFiles = ordinary.Where(x => x.resourcePath.StartsWith("cdn/",
                StringComparison.OrdinalIgnoreCase)).ToArray();
            var originMetadata = ordinary.Where(x => x.resourcePath.StartsWith("origin/",
                StringComparison.OrdinalIgnoreCase)).ToArray();
            if (cdnFiles.Length + originMetadata.Length != ordinary.Length)
                throw new InvalidDataException("发布计划包含未归类到 cdn/origin 的文件。");
            var pointers = candidates.Where(x => x.pointer).OrderBy(x => x.relativePath).ToArray();
            long total = candidates.Sum(x => x.length);
            if (onPrepared != null && !onPrepared(total, candidates.Length)) throw new OperationCanceledException();

            long uploaded = 0;
            int completed = 0;
            var directories = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            using var gate = new SemaphoreSlim(Math.Max(1, Math.Min(8, options.Concurrency)));
            Task[] tasks = cdnFiles.Select(async artifact =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    await UploadArtifactAsync(plan, artifact, options, directories, cancellationToken);
                    long bytes = Interlocked.Add(ref uploaded, artifact.length);
                    int files = Interlocked.Increment(ref completed);
                    onProgress?.Invoke(new FtpUploadProgress(artifact.relativePath, files,
                        candidates.Length, bytes, total));
                }
                finally { gate.Release(); }
            }).ToArray();
            await Task.WhenAll(tasks);

            // Origin 发布索引必须等所有 CDN 不可变文件完成后再上传。
            foreach (UploadArtifact metadata in originMetadata)
            {
                await UploadArtifactAsync(plan, metadata, options, directories, cancellationToken);
                uploaded += metadata.length; completed++;
                onProgress?.Invoke(new FtpUploadProgress(metadata.relativePath, completed,
                    candidates.Length, uploaded, total));
            }

            // 可变指针严格最后串行发布，任何前置文件失败都不会提前激活版本。
            foreach (UploadArtifact pointer in pointers)
            {
                await UploadArtifactAsync(plan, pointer, options, directories, cancellationToken);
                uploaded += pointer.length; completed++;
                onProgress?.Invoke(new FtpUploadProgress(pointer.relativePath, completed,
                    candidates.Length, uploaded, total));
            }
            ReleaseBaselineStore.SavePublished(buildRoot, plan);
        }

        private static async Task<HashSet<string>> VerifyIndexedRemoteBundlesAsync(FtpUploadOptions options,
            UploadArtifact[] artifacts, IReadOnlyDictionary<string, QhyBundleIndexEntry> remoteObjects,
            Action<FtpUploadProgress> onProgress, CancellationToken token)
        {
            UploadArtifact[] indexed = artifacts.Where(artifact => artifact.contentAddressedBundle &&
                    remoteObjects.TryGetValue(artifact.bundleName, out QhyBundleIndexEntry entry) &&
                    entry.length == artifact.length && string.Equals(entry.sha256, artifact.sha256,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (indexed.Length == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var verified = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            int completed = 0;
            using var gate = new SemaphoreSlim(Math.Max(1, Math.Min(8, options.Concurrency)));
            Task[] checks = indexed.Select(async artifact =>
            {
                await gate.WaitAsync(token);
                try
                {
                    // qhy.json is an acceleration index, not proof that the object still exists.
                    // A cheap FTP SIZE check prevents a stale/partially uploaded index from making
                    // QHY publish a manifest whose Bundle is absent on the resource server.
                    if (await RemoteFileHasExpectedLengthAsync(options, artifact.relativePath,
                            artifact.length, token))
                        verified.TryAdd(artifact.relativePath, 0);
                    int done = Interlocked.Increment(ref completed);
                    onProgress?.Invoke(new FtpUploadProgress(artifact.relativePath, done,
                        indexed.Length, done, indexed.Length, "FTP Bundle Check", true));
                }
                finally { gate.Release(); }
            }).ToArray();
            await Task.WhenAll(checks);
            return new HashSet<string>(verified.Keys, StringComparer.OrdinalIgnoreCase);
        }

        public static async Task RollbackAsync(string targetBuildRoot, FtpUploadOptions options,
            Action<FtpUploadProgress> onProgress, CancellationToken cancellationToken)
        {
            Validate(options);
            string planPath = Path.Combine(targetBuildRoot, "publish-plan.json");
            ReleaseUploadPlan plan = JsonUtility.FromJson<ReleaseUploadPlan>(File.ReadAllText(planPath));
            if (plan == null) throw new InvalidDataException("Rollback publish plan is invalid.");
            UploadArtifact pointer = (plan.added ?? Array.Empty<UploadArtifact>())
                .Concat(plan.changed ?? Array.Empty<UploadArtifact>()).FirstOrDefault(x => x.pointer &&
                    x.resourcePath.EndsWith(".version", StringComparison.OrdinalIgnoreCase));
            if (pointer == null) throw new InvalidDataException("Rollback target does not contain a resource pointer.");
            string local = Path.Combine(targetBuildRoot, "Reports", "rollback.version");
            Directory.CreateDirectory(Path.GetDirectoryName(local) ?? targetBuildRoot);
            File.WriteAllText(local, plan.resourceVersion, new UTF8Encoding(false));
            string remote = pointer.relativePath;
            await EnsureDirectoryAsync(options, Parent(remote), new ConcurrentDictionary<string, byte>(), cancellationToken);
            await UploadWithRetryAsync(options, local, remote, cancellationToken);
            plan.rollback = true;
            ResourceReleaseBaseline baseline = ReleaseBaselineStore.LoadForRelease(targetBuildRoot, plan);
            ReleaseBaselineStore.SaveRollback(targetBuildRoot, plan,
                baseline?.highestResourceVersion ?? baseline?.resourceVersion);
            onProgress?.Invoke(new FtpUploadProgress(pointer.relativePath, 1, 1,
                new FileInfo(local).Length, new FileInfo(local).Length, "Rollback pointer"));
        }

        public static string PrepareManualRollback(string targetBuildRoot)
        {
            string planPath = Path.Combine(targetBuildRoot, "publish-plan.json");
            ReleaseUploadPlan plan = JsonUtility.FromJson<ReleaseUploadPlan>(File.ReadAllText(planPath));
            if (plan == null || plan.schemaVersion != DistributionRuntimeConfig.CurrentSchemaVersion)
                throw new InvalidDataException("Rollback publish plan is not schema v5.");
            UploadArtifact pointer = (plan.added ?? Array.Empty<UploadArtifact>())
                .Concat(plan.changed ?? Array.Empty<UploadArtifact>()).FirstOrDefault(x => x.pointer &&
                    x.resourcePath.EndsWith(".version", StringComparison.OrdinalIgnoreCase));
            if (pointer == null) throw new InvalidDataException("Rollback target does not contain a resource pointer.");
            string root = Path.Combine(targetBuildRoot, "Rollback");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            string path = Path.Combine(root, pointer.relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
            File.WriteAllText(path, plan.resourceVersion, new UTF8Encoding(false));
            plan.rollback = true;
            File.WriteAllText(planPath, JsonUtility.ToJson(plan, true), new UTF8Encoding(false));
            ReleaseBaselineStore.SaveManualPending(targetBuildRoot, plan);
            return root;
        }

        private static async Task UploadArtifactAsync(ReleaseUploadPlan plan, UploadArtifact artifact,
            FtpUploadOptions options, ConcurrentDictionary<string, byte> directories,
            CancellationToken cancellationToken)
        {
            string local = Path.Combine(plan.releasesRoot,
                artifact.relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(local)) throw new FileNotFoundException("Server mirror artifact is missing.", local);
            string remote = artifact.relativePath;
            await EnsureDirectoryAsync(options, Parent(remote), directories, cancellationToken);
            await UploadWithRetryAsync(options, local, remote, cancellationToken);
        }

        private static async Task<Dictionary<string, QhyBundleIndexEntry>> LoadRemoteIndexAsync(
            FtpUploadOptions options, ReleaseUploadPlan plan, CancellationToken token)
        {
            DistributionPathResolver.ValidateSegment(plan.gameDirectory, "GameDirectory", true);
            string platformRoot = plan.gameDirectory.Trim('/') + "/" + plan.platform.Trim('/');
            string remote = platformRoot.Trim('/') + "/origin/qhy.json";
            try
            {
                QhyReleaseIndex index = JsonUtility.FromJson<QhyReleaseIndex>(
                    await DownloadTextAsync(options, remote, token));
                if (index == null || index.schemaVersion != DistributionRuntimeConfig.CurrentSchemaVersion)
                    throw new InvalidOperationException("远端 origin/qhy.json 不是 schemaVersion=5，请手工清空远端游戏目录。");
                return (index.bundles ?? Array.Empty<QhyBundleIndexEntry>())
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.name))
                    .GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            }
            catch (WebException exception) when (IsMissing(exception))
            {
                if (await DirectoryAppearsNonEmptyAsync(options, platformRoot, token))
                    throw new InvalidOperationException("远端游戏/平台目录非空但缺少 schema v5 origin/qhy.json。请手工清空后重新 FullPackage。");
                return new Dictionary<string, QhyBundleIndexEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static async Task UploadWithRetryAsync(FtpUploadOptions options, string local, string remote,
            CancellationToken token)
        {
            Exception last = null;
            for (int attempt = 0; attempt <= Math.Max(0, options.RetryCount); attempt++)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    FtpWebRequest request = Request(options, remote, WebRequestMethods.Ftp.UploadFile);
                    using CancellationTokenRegistration registration = token.Register(request.Abort);
                    request.ContentLength = new FileInfo(local).Length;
                    using (Stream input = File.OpenRead(local))
                    using (Stream output = await request.GetRequestStreamAsync()) await input.CopyToAsync(output, 81920, token);
                    using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
                    return;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                { last = ex; if (attempt < options.RetryCount) await Task.Delay(300 * (attempt + 1), token); }
            }
            throw new IOException($"FTP 文件上传失败：'{remote}'。远端父目录为 " +
                $"'{Parent(remote)}'，请确认文件写入权限、可用空间和文件名规则。", last);
        }

        private static async Task EnsureDirectoryAsync(FtpUploadOptions options, string path,
            ConcurrentDictionary<string, byte> known, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            await DirectoryCreationGate.WaitAsync(token);
            try
            {
                string[] segments = path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                string current = string.Empty;
                foreach (string segment in segments)
                {
                    current += "/" + segment;
                    if (known.ContainsKey(current)) continue;

                    if (!await DirectoryExistsAsync(options, current, token))
                    {
                        try
                        {
                            FtpWebRequest request = Request(options, current, WebRequestMethods.Ftp.MakeDirectory);
                            using CancellationTokenRegistration registration = token.Register(request.Abort);
                            using FtpWebResponse response =
                                (FtpWebResponse)await request.GetResponseAsync();
                        }
                        catch (WebException exception)
                        {
                            // 其他客户端可能刚好创建了相同目录。只有复查确认存在才可继续；
                            // 权限等真实故障不能再伪装成“目录已存在”。
                            if (!await DirectoryExistsAsync(options, current, token))
                                throw new IOException($"无法创建 FTP 远端目录 '{current}'。" +
                                    $"QHY 会直接从 FTP 账号登录根目录创建平台目录，" +
                                    $"请确认该账号的登录根目录正确，并且具有逐级列出和创建目录的权限。服务器返回：" +
                                    DescribeFtpFailure(exception), exception);
                        }
                    }

                    // 只有确认目录存在后才写缓存，避免并发上传进入尚未创建的父目录。
                    known.TryAdd(current, 0);
                    token.ThrowIfCancellationRequested();
                }
            }
            finally { DirectoryCreationGate.Release(); }
        }

        private static async Task<bool> DirectoryExistsAsync(FtpUploadOptions options, string path,
            CancellationToken token)
        {
            FtpWebRequest request = Request(options, path, WebRequestMethods.Ftp.ListDirectory);
            using CancellationTokenRegistration registration = token.Register(request.Abort);
            try
            {
                using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
                using Stream stream = response.GetResponseStream();
                token.ThrowIfCancellationRequested();
                return true;
            }
            catch (WebException exception) when (IsMissing(exception)) { return false; }
        }

        private static async Task<string> DownloadTextAsync(FtpUploadOptions options, string path, CancellationToken token)
        {
            using FtpWebResponse response = (FtpWebResponse)await Request(options, path, WebRequestMethods.Ftp.DownloadFile).GetResponseAsync();
            using var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8);
            token.ThrowIfCancellationRequested();
            return await reader.ReadToEndAsync();
        }
        private static async Task<bool> RemoteFileHasExpectedLengthAsync(FtpUploadOptions options,
            string path, long expectedLength, CancellationToken token)
        {
            FtpWebRequest request = Request(options, path, WebRequestMethods.Ftp.GetFileSize);
            using CancellationTokenRegistration registration = token.Register(request.Abort);
            try
            {
                using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
                return response.ContentLength == expectedLength;
            }
            catch (WebException exception) when (IsMissing(exception))
            {
                return false;
            }
            catch (WebException exception) when (IsSizeCommandUnsupported(exception))
            {
                // Servers without SIZE cannot safely prove the indexed object is present. Uploading
                // it again is slower but preserves correctness and keeps current pointers atomic.
                return false;
            }
        }
        private static async Task<bool> DirectoryAppearsNonEmptyAsync(FtpUploadOptions options, string path, CancellationToken token)
        {
            try
            {
                using FtpWebResponse response = (FtpWebResponse)await Request(options, path, WebRequestMethods.Ftp.ListDirectory).GetResponseAsync();
                using var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null);
                token.ThrowIfCancellationRequested();
                return !string.IsNullOrWhiteSpace(await reader.ReadToEndAsync());
            }
            catch (WebException exception) when (IsMissing(exception)) { return false; }
        }
        private static FtpWebRequest Request(FtpUploadOptions options, string path, string method)
        {
            string scheme = options.EnableSsl ? "ftp" : "ftp";
            var request = (FtpWebRequest)WebRequest.Create($"{scheme}://{options.Host}:{options.Port}/{path.TrimStart('/')}");
            request.Method = method; request.Credentials = new NetworkCredential(options.UserName, options.Password);
            request.EnableSsl = options.EnableSsl; request.UsePassive = options.UsePassive;
            request.UseBinary = true; request.KeepAlive = false; request.Timeout = 30000;
            return request;
        }
        private static bool IsMissing(WebException exception) =>
            exception.Response is FtpWebResponse ftp && ftp.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable;
        private static bool IsSizeCommandUnsupported(WebException exception) =>
            exception.Response is FtpWebResponse ftp &&
            (ftp.StatusCode == FtpStatusCode.CommandNotImplemented ||
             ftp.StatusCode == FtpStatusCode.CommandSyntaxError ||
             ftp.StatusCode == FtpStatusCode.ArgumentSyntaxError);
        private static string DescribeFtpFailure(WebException exception)
        {
            if (exception.Response is FtpWebResponse ftp)
                return $"{(int)ftp.StatusCode} {ftp.StatusDescription?.Trim()}";
            return exception.Status + ": " + exception.Message;
        }
        private static string Parent(string value) { int index = value.LastIndexOf('/'); return index < 0 ? string.Empty : value.Substring(0, index); }
        private static void Validate(FtpUploadOptions value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.Host) || string.IsNullOrWhiteSpace(value.UserName))
                throw new InvalidOperationException("FTP Host 和用户名不能为空。");
            if (value.Port < 1 || value.Port > 65535) throw new InvalidOperationException("FTP Port 无效。");
        }
    }

    internal sealed class DistributionVerificationResult
    {
        public int VerifiedFiles;
        public string[] Warnings = Array.Empty<string>();
    }

    internal static class DistributionEndpointVerifier
    {
        internal static async Task<DistributionVerificationResult> VerifyAndPublishAsync(string buildRoot,
            Action<FtpUploadProgress> progress, CancellationToken token)
        {
            string planPath = Path.Combine(buildRoot, "publish-plan.json");
            if (!File.Exists(planPath)) throw new FileNotFoundException("publish-plan.json is missing.", planPath);
            ReleaseUploadPlan plan = JsonUtility.FromJson<ReleaseUploadPlan>(File.ReadAllText(planPath));
            if (plan == null || plan.schemaVersion != DistributionRuntimeConfig.CurrentSchemaVersion || string.IsNullOrWhiteSpace(plan.cdnRoot) ||
                string.IsNullOrWhiteSpace(plan.originRoot))
                throw new InvalidDataException("publish-plan.json 缺少 schema v5 游戏目录或 CDN/Origin 地址。");
            DistributionPathResolver.ValidateSegment(plan.gameDirectory, "GameDirectory", true);
            if (ReleaseBaselineStore.IsAlreadyPublished(buildRoot, plan))
                throw new ReleaseAlreadyPublishedException("该版本已经登记为 Published。");

            IEnumerable<UploadArtifact> verificationSet = (plan.added ?? Array.Empty<UploadArtifact>())
                .Concat(plan.changed ?? Array.Empty<UploadArtifact>());
            if (plan.rollback) verificationSet = verificationSet.Concat(plan.unchanged ?? Array.Empty<UploadArtifact>());
            UploadArtifact[] artifacts = verificationSet
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.resourcePath))
                .GroupBy(x => x.resourcePath, StringComparer.OrdinalIgnoreCase).Select(x => x.First())
                .OrderBy(x => x.pointer ? 1 : 0).ThenBy(x => x.resourcePath).ToArray();
            for (int index = 0; index < artifacts.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                UploadArtifact artifact = artifacts[index];
                string url = ResolveArtifactUrl(plan, artifact.resourcePath);
                if (artifact.pointer) url += (url.Contains("?") ? "&" : "?") + "qhy=" + DateTime.UtcNow.Ticks;
                byte[] bytes = await DownloadAsync(url, null, token);
                string sha = Sha(bytes);
                if (bytes.LongLength != artifact.length || !string.Equals(sha, artifact.sha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"资源服务器内容校验失败：{artifact.resourcePath}，长度或 SHA-256 不匹配。");
                progress?.Invoke(new FtpUploadProgress(artifact.resourcePath, index + 1,
                    artifacts.Length, index + 1, artifacts.Length, "Resource Verify", true));
            }
            if (plan.rollback)
            {
                ResourceReleaseBaseline baseline = ReleaseBaselineStore.LoadForRelease(buildRoot, plan);
                ReleaseBaselineStore.SaveRollback(buildRoot, plan,
                    baseline?.highestResourceVersion ?? baseline?.resourceVersion);
            }
            else ReleaseBaselineStore.SavePublished(buildRoot, plan);
            return new DistributionVerificationResult { VerifiedFiles = artifacts.Length };
        }

        private static string ResolveArtifactUrl(ReleaseUploadPlan plan, string resourcePath)
        {
            if (resourcePath.StartsWith("cdn/", StringComparison.OrdinalIgnoreCase))
                return DistributionPathResolver.CombineUrl(plan.cdnRoot, resourcePath.Substring(4));
            if (resourcePath.StartsWith("origin/", StringComparison.OrdinalIgnoreCase))
                return DistributionPathResolver.CombineUrl(plan.originRoot, resourcePath.Substring(7));
            throw new InvalidDataException("发布文件未归类到 cdn 或 origin：" + resourcePath);
        }

        private static async Task<byte[]> DownloadAsync(string url, string range, CancellationToken token)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET"; request.Timeout = 30000; request.ReadWriteTimeout = 30000;
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            if (!string.IsNullOrWhiteSpace(range)) request.AddRange(long.Parse(range));
            using CancellationTokenRegistration registration = token.Register(request.Abort);
            using HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync();
            if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                throw new IOException($"Resource HTTP {(int)response.StatusCode}: {url}");
            using Stream input = response.GetResponseStream() ?? Stream.Null;
            using var output = new MemoryStream();
            await input.CopyToAsync(output, 81920, token);
            return output.ToArray();
        }
        private static string Sha(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
