using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GameIntegration
{
    public interface IClientInstaller
    {
        Task InstallAndRestartAsync(ClientUpdateManifest manifest, string packagePath);
    }

    public sealed class ClientUpdateService
    {
        private readonly QHYFrameworkSettings _settings;
        private readonly Action<StartupProgress> _report;
        private readonly IClientInstaller _installer;

        public ClientUpdateManifest Manifest { get; private set; }
        public bool HasUpdate { get; private set; }
        public string DownloadedPackagePath { get; private set; }

        public ClientUpdateService(QHYFrameworkSettings settings, Action<StartupProgress> report,
            IClientInstaller installer = null)
        {
            _settings = settings ? settings : throw new ArgumentNullException(nameof(settings));
            _report = report;
            _installer = installer ?? ClientInstallerFactory.Create();
        }

        public async Task<bool> CheckAsync()
        {
            HasUpdate = false;
            Manifest = null;
            IntegrationPlatform platform = QHYFrameworkSettings.GetCurrentPlatform();
            if (platform != IntegrationPlatform.Windows && platform != IntegrationPlatform.Android)
                return false;

            string url = _settings.GetClientManifestUrl(platform);
            if (string.IsNullOrWhiteSpace(url))
            {
                UnityEngine.Debug.LogWarning($"[QHYFramework] {platform} 未配置客户端更新地址，跳过整包更新检查。");
                return false;
            }

            _report?.Invoke(new StartupProgress(StartupState.CheckingClientUpdate, 0f, 0, 0, 0, 0,
                "正在检查客户端版本…"));
            try
            {
                using UnityWebRequest request = UnityWebRequest.Get(AppendTimestamp(url));
                request.timeout = Math.Max(1, _settings.requestTimeoutSeconds);
                await SendAsync(request);
                EnsureRequestSucceeded(request, "客户端版本清单请求失败");
                ClientUpdateManifest manifest = JsonUtility.FromJson<ClientUpdateManifest>(request.downloadHandler.text);
                ValidateManifest(manifest, platform);
                ClientVersion current = ClientVersion.Parse(Application.version);
                ClientVersion latest = ClientVersion.Parse(manifest.Version);
                if (latest.CompareTo(current) <= 0)
                    return false;

                Manifest = manifest;
                HasUpdate = true;
                _report?.Invoke(new StartupProgress(StartupState.ClientUpdateAvailable, 0f, 0, 1, 0,
                    manifest.SizeBytes, $"发现新客户端 {manifest.Version}，当前版本 {Application.version}"));
                return true;
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning($"[QHYFramework] 客户端更新检查失败，继续使用当前版本：" +
                                             exception.GetBaseException().Message);
                Manifest = null;
                HasUpdate = false;
                return false;
            }
        }

        public async Task<string> DownloadAsync()
        {
            if (!HasUpdate || Manifest == null)
                throw new InvalidOperationException("当前没有待下载的客户端更新。");

            string directory = Path.Combine(Application.persistentDataPath, "ClientUpdates", Manifest.Version);
            Directory.CreateDirectory(directory);
            string finalPath = Path.Combine(directory, SafeFileName(Manifest.FileName));
            string partialPath = finalPath + ".part";
            if (await VerifyFileAsync(finalPath, false))
            {
                DownloadedPackagePath = finalPath;
                return finalPath;
            }
            if (File.Exists(finalPath)) File.Delete(finalPath);

            int attempts = Math.Max(1, _settings.downloadRetryCount + 1);
            Exception lastError = null;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                try
                {
                    await DownloadAttemptAsync(partialPath);
                    File.Move(partialPath, finalPath);
                    if (!await VerifyFileAsync(finalPath, true))
                        throw new InvalidDataException("客户端安装包长度或 SHA-256 校验失败。");
                    DownloadedPackagePath = finalPath;
                    return finalPath;
                }
                catch (Exception exception)
                {
                    lastError = exception;
                    if (attempt + 1 < attempts)
                        await Task.Delay(500 * (attempt + 1));
                }
            }
            throw new IOException("客户端下载失败：" + lastError?.GetBaseException().Message, lastError);
        }

        public async Task InstallAndRestartAsync()
        {
            if (Manifest == null)
                throw new InvalidOperationException("客户端更新清单不存在。");
            string path = DownloadedPackagePath;
            if (string.IsNullOrWhiteSpace(path) || !await VerifyFileAsync(path, false))
                path = await DownloadAsync();
            _report?.Invoke(new StartupProgress(StartupState.InstallingClient, 1f, 1, 1,
                Manifest.SizeBytes, Manifest.SizeBytes, "正在安装新客户端…"));
            await _installer.InstallAndRestartAsync(Manifest, path);
        }

        private async Task DownloadAttemptAsync(string partialPath)
        {
            long existing = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            if (existing >= Manifest.SizeBytes && Manifest.SizeBytes > 0)
                File.Delete(partialPath);
            existing = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

            bool append = existing > 0;
            using var request = new UnityWebRequest(Manifest.PackageUrl, UnityWebRequest.kHttpVerbGET);
            request.downloadHandler = new DownloadHandlerFile(partialPath, append);
            request.timeout = Math.Max(1, _settings.requestTimeoutSeconds);
            if (append) request.SetRequestHeader("Range", $"bytes={existing}-");
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                long downloaded = existing + (long)request.downloadedBytes;
                float progress = Manifest.SizeBytes <= 0 ? request.downloadProgress :
                    Mathf.Clamp01((float)downloaded / Manifest.SizeBytes);
                _report?.Invoke(new StartupProgress(StartupState.DownloadingClient, progress, 0, 1,
                    downloaded, Manifest.SizeBytes, $"正在下载客户端 {Manifest.Version}…"));
                await Task.Yield();
            }
            EnsureRequestSucceeded(request, "客户端下载失败");
            if (append && request.responseCode != 206)
            {
                File.Delete(partialPath);
                await DownloadFreshAsync(partialPath);
            }
        }

        private async Task DownloadFreshAsync(string path)
        {
            using var request = new UnityWebRequest(Manifest.PackageUrl, UnityWebRequest.kHttpVerbGET);
            request.downloadHandler = new DownloadHandlerFile(path, false);
            request.timeout = Math.Max(1, _settings.requestTimeoutSeconds);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                long downloaded = (long)request.downloadedBytes;
                _report?.Invoke(new StartupProgress(StartupState.DownloadingClient,
                    Manifest.SizeBytes <= 0 ? request.downloadProgress :
                    Mathf.Clamp01((float)downloaded / Manifest.SizeBytes), 0, 1, downloaded,
                    Manifest.SizeBytes, $"正在下载客户端 {Manifest.Version}…"));
                await Task.Yield();
            }
            EnsureRequestSucceeded(request, "客户端下载失败");
        }

        private async Task<bool> VerifyFileAsync(string path, bool deleteOnFailure)
        {
            if (!File.Exists(path)) return false;
            _report?.Invoke(new StartupProgress(StartupState.VerifyingClient, 0f, 0, 1, 0,
                Manifest.SizeBytes, "正在校验客户端安装包…"));
            bool valid = new FileInfo(path).Length == Manifest.SizeBytes &&
                         string.Equals(await ComputeSha256Async(path), Manifest.Sha256,
                             StringComparison.OrdinalIgnoreCase);
            if (!valid && deleteOnFailure && File.Exists(path)) File.Delete(path);
            return valid;
        }

        private static async Task<string> ComputeSha256Async(string path)
        {
            return await Task.Run(() =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using SHA256 sha = SHA256.Create();
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            });
        }

        private static void ValidateManifest(ClientUpdateManifest manifest, IntegrationPlatform platform)
        {
            if (manifest == null || manifest.SchemaVersion != 1)
                throw new InvalidDataException("客户端更新清单格式无效。");
            if (!string.Equals(manifest.Platform, platform.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"客户端更新清单平台不匹配：{manifest.Platform} != {platform}");
            ClientVersion parsedVersion = ClientVersion.Parse(manifest.Version);
            if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidDataException("客户端安装包 URL 无效。");
            if (string.IsNullOrWhiteSpace(manifest.FileName) || manifest.SizeBytes <= 0 ||
                string.IsNullOrWhiteSpace(manifest.Sha256) || manifest.Sha256.Length != 64)
                throw new InvalidDataException("客户端安装包信息不完整。");
            string expectedType = platform == IntegrationPlatform.Android ? "apk" : "zip";
            if (!string.Equals(manifest.PackageType, expectedType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"客户端安装包类型错误：{manifest.PackageType}");
            if (platform == IntegrationPlatform.Android &&
                manifest.AndroidVersionCode != parsedVersion.AndroidVersionCode)
                throw new InvalidDataException("Android Version Code 与客户端版本不匹配。");
            if (platform == IntegrationPlatform.Windows)
            {
                string entry = (manifest.EntryExecutable ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(entry) || Path.IsPathRooted(entry) || entry.Contains(".."))
                    throw new InvalidDataException("Windows 客户端入口路径无效。");
            }
        }

        private static Task SendAsync(UnityWebRequest request)
        {
            var completion = new TaskCompletionSource<bool>();
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            if (operation.isDone) return Task.CompletedTask;
            operation.completed += _ => completion.TrySetResult(true);
            return completion.Task;
        }

        private static void EnsureRequestSucceeded(UnityWebRequest request, string action)
        {
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"{action}：{request.error}，HTTP {request.responseCode}");
        }

        private static string AppendTimestamp(string url) =>
            url + (url.Contains("?") ? "&" : "?") + DateTime.UtcNow.Ticks;

        private static string SafeFileName(string name)
        {
            string result = Path.GetFileName(name ?? string.Empty);
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException("客户端文件名无效。");
            return result;
        }
    }

    internal static class ClientInstallerFactory
    {
        public static IClientInstaller Create()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return new WindowsClientInstaller();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidClientInstaller();
#else
            return new UnsupportedClientInstaller();
#endif
        }
    }

    internal sealed class UnsupportedClientInstaller : IClientInstaller
    {
        public Task InstallAndRestartAsync(ClientUpdateManifest manifest, string packagePath) =>
            throw new PlatformNotSupportedException("当前平台不支持客户端自动安装。");
    }

    public sealed class WindowsClientInstaller : IClientInstaller
    {
        public Task InstallAndRestartAsync(ClientUpdateManifest manifest, string packagePath)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            string installRoot = Directory.GetParent(Application.dataPath)?.FullName;
            string sourceUpdater = Path.Combine(installRoot ?? string.Empty, "GameIntegration.Updater.exe");
            string marker = Path.Combine(installRoot ?? string.Empty, ".gameintegration-client");
            if (string.IsNullOrWhiteSpace(installRoot) || !File.Exists(sourceUpdater) || !File.Exists(marker))
                throw new FileNotFoundException("客户端更新器或安装目录标记不存在。", sourceUpdater);
            string tempRoot = Path.Combine(Path.GetTempPath(), "GameIntegrationUpdater", manifest.Version);
            Directory.CreateDirectory(tempRoot);
            string updater = Path.Combine(tempRoot, "GameIntegration.Updater.exe");
            File.Copy(sourceUpdater, updater, true);
            int processId = Process.GetCurrentProcess().Id;
            string arguments = string.Join(" ", new[]
            {
                "--pid", processId.ToString(), "--archive", Quote(packagePath), "--root", Quote(installRoot),
                "--entry", Quote(manifest.EntryExecutable), "--version", Quote(manifest.Version)
            });
            WindowsNativeProcessLauncher.Start(updater, arguments, tempRoot);
            Application.Quit();
            return Task.CompletedTask;
#else
            throw new PlatformNotSupportedException("Windows 更新器只能在 Windows Player 中运行。");
#endif
        }

        private static string Quote(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }

    internal static class WindowsNativeProcessLauncher
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const int ShowNormal = 1;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr ShellExecuteW(IntPtr window, string operation, string file,
            string parameters, string directory, int showCommand);
#endif

        public static void Start(string executable, string arguments, string workingDirectory)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // System.Diagnostics.Process.Start(UseShellExecute=true) reaches Unity IL2CPP's
            // ShellExecuteEx_internal assertion in Development players. Calling Win32 directly
            // avoids that Unity runtime wrapper while preserving normal shell launch behavior.
            IntPtr result = ShellExecuteW(IntPtr.Zero, "open", executable, arguments,
                workingDirectory, ShowNormal);
            long code = result.ToInt64();
            if (code <= 32)
                throw new IOException($"Windows 更新器启动失败（ShellExecute 返回 {code}，Win32 {Marshal.GetLastWin32Error()}）。");
#else
            throw new PlatformNotSupportedException("Windows 原生进程启动器只能在 Windows Player 中运行。");
#endif
        }
    }

    public sealed class AndroidClientInstaller : IClientInstaller
    {
        public Task InstallAndRestartAsync(ClientUpdateManifest manifest, string packagePath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string providerRoot = Path.Combine(Application.persistentDataPath, "ClientUpdates");
            Directory.CreateDirectory(providerRoot);
            string providerPath = Path.Combine(providerRoot, Path.GetFileName(packagePath));
            if (!string.Equals(Path.GetFullPath(packagePath), Path.GetFullPath(providerPath),
                    StringComparison.OrdinalIgnoreCase))
                File.Copy(packagePath, providerPath, true);
            packagePath = providerPath;
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject packageManager = activity.Call<AndroidJavaObject>("getPackageManager");
            int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
            if (sdk >= 26 && !packageManager.Call<bool>("canRequestPackageInstalls"))
            {
                using var settingsIntent = new AndroidJavaObject("android.content.Intent",
                    "android.settings.MANAGE_UNKNOWN_APP_SOURCES");
                using var uriClass = new AndroidJavaClass("android.net.Uri");
                using AndroidJavaObject packageUri = uriClass.CallStatic<AndroidJavaObject>("parse",
                    "package:" + Application.identifier);
                settingsIntent.Call<AndroidJavaObject>("setData", packageUri);
                activity.Call("startActivity", settingsIntent);
                throw new InvalidOperationException("请允许此应用安装未知来源应用，然后返回并重试安装。");
            }
            string authority = Application.identifier + ".gameintegration.fileprovider";
            using var uri = new AndroidJavaClass("android.net.Uri").CallStatic<AndroidJavaObject>("parse",
                "content://" + authority + "/" + Uri.EscapeDataString(Path.GetFileName(packagePath)));
            using var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW");
            intent.Call<AndroidJavaObject>("setDataAndType", uri, "application/vnd.android.package-archive");
            intent.Call<AndroidJavaObject>("addFlags", 1);
            intent.Call<AndroidJavaObject>("addFlags", 0x10000000);
            activity.Call("startActivity", intent);
            return Task.CompletedTask;
#else
            throw new PlatformNotSupportedException("Android 安装器只能在 Android Player 中运行。");
#endif
        }
    }
}
