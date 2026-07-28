using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
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
        public readonly string FileName;
        public readonly int CompletedFiles;
        public readonly int TotalFiles;
        public readonly long UploadedBytes;
        public readonly long TotalBytes;

        public FtpUploadProgress(string fileName, int completedFiles, int totalFiles,
            long uploadedBytes, long totalBytes)
        {
            FileName = fileName;
            CompletedFiles = completedFiles;
            TotalFiles = totalFiles;
            UploadedBytes = uploadedBytes;
            TotalBytes = totalBytes;
        }

        public float Progress => TotalBytes <= 0 ? 1f : (float)UploadedBytes / TotalBytes;
    }

    internal static class FtpReleaseUploader
    {
        private sealed class UploadItem
        {
            public string LocalPath;
            public string RemotePath;
            public long Length;
            public string FinalRemotePath;
        }

        public static async Task UploadAsync(string releaseRoot, FtpUploadOptions options,
            bool includeClient, Action<FtpUploadProgress> onProgress, CancellationToken cancellationToken)
        {
            Validate(options);
            string cdnRoot = Path.Combine(releaseRoot, "CDN");
            if (!Directory.Exists(cdnRoot))
                throw new DirectoryNotFoundException(F("找不到热更新包目录：{0}。请先执行构建。",
                    "Hot-update package directory not found: {0}. Build it first.", cdnRoot));

            var items = CollectFiles(cdnRoot, options.HotUpdateDirectory, true);
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

            long totalBytes = items.Sum(item => item.Length);
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
            try
            {
                await RenameAsync(options, temporary, finalPath);
            }
            catch (WebException)
            {
                try
                {
                    FtpWebRequest delete = CreateRequest(options, finalPath, WebRequestMethods.Ftp.DeleteFile);
                    using FtpWebResponse deleteResponse = (FtpWebResponse)await delete.GetResponseAsync();
                }
                catch (WebException exception) when (exception.Response is FtpWebResponse response &&
                                                      response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                { }
                await RenameAsync(options, temporary, finalPath);
            }
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
            string version = new DirectoryInfo(releaseRoot).Name;
            string platform = Directory.GetParent(releaseRoot)?.Name ?? "unknown";
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
                return 1;
            if (extension.Equals(".version", StringComparison.OrdinalIgnoreCase))
                return 2;
            if (extension.Equals(".hash", StringComparison.OrdinalIgnoreCase))
                return 3;
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
