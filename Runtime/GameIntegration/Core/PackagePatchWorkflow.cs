using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace GameIntegration
{
    /// <summary>
    /// 对应 YooAsset Space Shooter 的补丁状态链，但不依赖 Samples 中的 UniFramework。
    /// 初始化 -> 请求版本 -> 激活清单 -> 创建下载器 -> 下载 -> 清理无用缓存。
    /// </summary>
    internal sealed class PackagePatchWorkflow
    {
        private readonly QHYFrameworkSettings _settings;
        private readonly DistributionRuntimeConfig _distribution;
        private readonly EPlayMode _playMode;
        private readonly Action<StartupState, float, string> _report;
        private readonly Action<DownloadProgressChangedEventArgs> _downloadProgress;
        private readonly Action<DownloadErrorEventArgs> _downloadError;
        private readonly Dictionary<string, string> _downloadFileNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _lastDownloadFailure = string.Empty;
        private bool _packageInitialized;

        public ResourcePackage Package { get; private set; }
        public ResourceDownloaderOperation Downloader { get; private set; }
        public string ActiveVersion { get; private set; } = string.Empty;

        public PackagePatchWorkflow(
            QHYFrameworkSettings settings,
            DistributionRuntimeConfig distribution,
            EPlayMode playMode,
            Action<StartupState, float, string> report,
            Action<DownloadProgressChangedEventArgs> downloadProgress,
            Action<DownloadErrorEventArgs> downloadError)
        {
            _settings = settings;
            _distribution = distribution ?? throw new ArgumentNullException(nameof(distribution));
            _playMode = playMode;
            _report = report;
            _downloadProgress = downloadProgress;
            _downloadError = downloadError;
        }

        public async Task PrepareAsync()
        {
            Report(StartupState.Initializing, "正在初始化资源包…");
            await InitializePackageAsync();

            string requestedVersion = await RequestPackageVersionAsync();
            ActiveVersion = await LoadUsableManifestAsync(requestedVersion);

            Downloader = Package.CreateResourceDownloader(
                new ResourceDownloaderOptions(_settings.downloadConcurrency, _settings.downloadRetryCount));
            if (Downloader.TotalDownloadCount == 0)
                SaveLastGoodVersion(ActiveVersion);
        }

        public async Task DownloadAsync()
        {
            if (Downloader == null || Downloader.TotalDownloadCount == 0)
                return;

            Report(StartupState.Downloading, "正在下载资源文件…");
            _lastDownloadFailure = string.Empty;
            _downloadFileNames.Clear();
            Downloader.DownloadProgressChanged += _downloadProgress;
            Downloader.DownloadFileStarted += OnDownloadFileStarted;
            Downloader.DownloadError += OnDownloadError;
            try
            {
                Downloader.StartDownload();
                await Downloader;
                try
                {
                    YooOperation.EnsureSucceeded(Downloader, StartupState.Downloading, "下载资源文件");
                }
                catch (StartupException) when (!string.IsNullOrWhiteSpace(_lastDownloadFailure))
                {
                    throw new StartupException(StartupState.Downloading, _lastDownloadFailure);
                }
                _report(StartupState.Downloading, 1f, "资源文件下载完成");
            }
            finally
            {
                Downloader.DownloadProgressChanged -= _downloadProgress;
                Downloader.DownloadFileStarted -= OnDownloadFileStarted;
                Downloader.DownloadError -= OnDownloadError;
            }

            if (_settings.clearUnusedCacheAfterUpdate)
                await ClearUnusedCacheAsync();

            // 只有资源完整下载后，才把新清单记为可离线回退版本。
            SaveLastGoodVersion(ActiveVersion);
        }

        private void OnDownloadFileStarted(DownloadFileStartedEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.BundleName) && !string.IsNullOrWhiteSpace(args.FileName))
                _downloadFileNames[args.BundleName] = args.FileName;
        }

        private void OnDownloadError(DownloadErrorEventArgs args)
        {
            string actualFileName = _downloadFileNames.TryGetValue(args.FileName, out string value)
                ? value
                : args.FileName;
            string url;
            try { url = new DistributionPathResolver(_distribution).ResolveYooAssetUrl(actualFileName); }
            catch { url = "（无法解析远端 URL）"; }
            _lastDownloadFailure = $"下载资源文件失败。Bundle：{args.FileName}；远端文件：{actualFileName}；" +
                                   $"URL：{url}；错误：{args.ErrorInfo}";
            _downloadError?.Invoke(args);
        }

        private async Task InitializePackageAsync()
        {
            if (_packageInitialized)
                return;

#if UNITY_EDITOR
            if (_playMode == EPlayMode.OfflinePlayMode || _playMode == EPlayMode.HostPlayMode)
            {
                string versionPath = System.IO.Path.Combine(Application.streamingAssetsPath, "yoo",
                    _settings.packageName, _settings.packageName + ".version");
                if (!System.IO.File.Exists(versionPath))
                {
                    throw new StartupException(StartupState.Initializing,
                        $"{_playMode} 缺少内置首包：{versionPath}。请切换 EditorSimulateMode，或在发布窗口执行 Full Package Build。");
                }
            }
#endif

            if (!YooAssets.IsInitialized)
                YooAssets.Initialize();

            if (!YooAssets.TryGetPackage(_settings.packageName, out ResourcePackage package))
                package = YooAssets.CreatePackage(_settings.packageName);
            Package = package;

            InitializePackageOptions options = CreateInitializeOptions();
            options.AutoUnloadBundleWhenUnused = _settings.autoUnloadBundleWhenUnused;
            InitializePackageOperation operation = Package.InitializePackageAsync(options);
            await operation;
            YooOperation.EnsureSucceeded(operation, StartupState.Initializing, "初始化 YooAsset 包");
            _packageInitialized = true;
        }

        private InitializePackageOptions CreateInitializeOptions()
        {
            switch (_playMode)
            {
                case EPlayMode.EditorSimulateMode:
#if UNITY_EDITOR
                    // 与 YooAsset Space Shooter 示例一致：每次启动都生成当前收集配置的模拟清单。
                    // 正式资源构建会清理旧 Simulate 目录，不能长期依赖序列化的历史路径。
                    PackageBuildResult simulateResult = EditorSimulateBuildInvoker.Build(
                        _settings.packageName, (int)EBundleType.VirtualAssetBundle);
                    if (simulateResult == null || string.IsNullOrWhiteSpace(simulateResult.PackageRootDirectory))
                        throw new StartupException(StartupState.Initializing, "YooAsset 编辑器模拟构建没有返回有效目录。");
                    var editorParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                        simulateResult.PackageRootDirectory);
                    editorParameters.AddParameter(EFileSystemParameter.DownloadMaxConcurrency,
                        _settings.downloadConcurrency);
                    return new EditorSimulateModeOptions
                    {
                        EditorFileSystemParameters = editorParameters
                    };
#else
                    throw new StartupException(StartupState.Initializing,
                        "EditorSimulateMode 只能在 Unity Editor 中使用。");
#endif
                case EPlayMode.OfflinePlayMode:
                    return new OfflinePlayModeOptions
                    {
                        BuiltinFileSystemParameters =
                            FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
                    };
                case EPlayMode.HostPlayMode:
                    return CreateHostPlayModeOptions();
                case EPlayMode.WebPlayMode:
                    return new WebPlayModeOptions
                    {
                        WebServerFileSystemParameters =
                            FileSystemParameters.CreateDefaultWebServerFileSystemParameters()
                    };
                default:
                    throw new StartupException(StartupState.Initializing,
                        $"不支持的 YooAsset 启动模式：{_playMode}。");
            }
        }

        private HostPlayModeOptions CreateHostPlayModeOptions()
        {
            var remoteService = new DistributionRemoteService(new DistributionPathResolver(_distribution));
            FileSystemParameters builtinParameters =
                FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
            builtinParameters.AddParameter(EFileSystemParameter.CopyBuiltinPackageManifest,
                _settings.copyBuiltinPackageManifest);

            FileSystemParameters cacheParameters =
                FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService);
            cacheParameters.AddParameter(EFileSystemParameter.DownloadMaxConcurrency,
                _settings.downloadConcurrency);
            cacheParameters.AddParameter(EFileSystemParameter.DownloadMaxRequestPerFrame,
                _settings.downloadMaxRequestPerFrame);
            cacheParameters.AddParameter(EFileSystemParameter.DownloadWatchdogTimeout,
                _settings.downloadWatchdogTimeoutSeconds);

            return new HostPlayModeOptions
            {
                BuiltinFileSystemParameters = builtinParameters,
                CacheFileSystemParameters = cacheParameters
            };
        }

        private async Task<string> RequestPackageVersionAsync()
        {
            Report(StartupState.CheckingVersion, "正在请求资源版本…");
            bool appendTimeTicks = _playMode == EPlayMode.HostPlayMode ||
                                   _playMode == EPlayMode.WebPlayMode;
            RequestPackageVersionOperation operation = Package.RequestPackageVersionAsync(
                new RequestPackageVersionOptions(appendTimeTicks, _settings.requestTimeoutSeconds));
            await operation;
            if (operation.Status == EOperationStatus.Succeeded)
            {
                return operation.PackageVersion;
            }

            if (_playMode == EPlayMode.HostPlayMode)
            {
                string cachedVersion = GetLastGoodVersion();
                if (!string.IsNullOrWhiteSpace(cachedVersion))
                {
                    Debug.LogWarning($"[QHYFramework] 远端版本请求失败，回退缓存版本 " +
                                     $"{cachedVersion}：{operation.Error}");
                    return cachedVersion;
                }
                throw new StartupException(StartupState.CheckingVersion,
                    $"远端版本请求失败且没有可用缓存：{operation.Error}");
            }

            throw new StartupException(StartupState.CheckingVersion,
                $"{_playMode} 请求资源版本失败：{operation.Error}");
        }

        private async Task<string> LoadUsableManifestAsync(string requestedVersion)
        {
            Report(StartupState.LoadingManifest, $"正在激活资源清单 {requestedVersion}…");
            LoadPackageManifestOperation operation = Package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(requestedVersion, _settings.requestTimeoutSeconds));
            await operation;
            if (operation.Status == EOperationStatus.Succeeded)
                return requestedVersion;

            if (_playMode == EPlayMode.HostPlayMode)
            {
                string cachedVersion = GetLastGoodVersion();
                if (!string.IsNullOrWhiteSpace(cachedVersion) &&
                    !string.Equals(cachedVersion, requestedVersion, StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[QHYFramework] 清单 {requestedVersion} 激活失败，回退缓存版本 " +
                                     $"{cachedVersion}：{operation.Error}");
                    Report(StartupState.LoadingManifest, $"正在回退资源清单 {cachedVersion}…");
                    LoadPackageManifestOperation fallback = Package.LoadPackageManifestAsync(
                        new LoadPackageManifestOptions(cachedVersion, _settings.requestTimeoutSeconds));
                    await fallback;
                    if (fallback.Status == EOperationStatus.Succeeded)
                        return cachedVersion;

                    throw new StartupException(StartupState.LoadingManifest,
                        $"远端清单和缓存清单均不可用。远端：{operation.Error}；缓存：{fallback.Error}");
                }
            }
            throw new StartupException(StartupState.LoadingManifest,
                $"激活资源清单 {requestedVersion} 失败：{operation.Error}");
        }

        private async Task ClearUnusedCacheAsync()
        {
            Report(StartupState.ClearingCache, "正在清理无用缓存文件…");
            var options = new ClearCacheOptions(ClearCacheMethods.ClearUnusedBundleFiles);
            ClearCacheOperation operation = Package.ClearCacheAsync(options);
            await operation;

            // 与官方示例一致：缓存清理失败不破坏已经完成的更新。
            if (operation.Status != EOperationStatus.Succeeded)
                Debug.LogWarning($"[QHYFramework] 清理无用缓存失败：{operation.Error}");
            _report(StartupState.ClearingCache, 1f, "资源更新处理完成");
        }

        private string GetLastGoodVersion()
        {
            string value = PlayerPrefs.GetString(_settings.GetLocalVersionKey(_playMode, _distribution), string.Empty);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            return string.Empty;
        }

        private void SaveLastGoodVersion(string version)
        {
            PlayerPrefs.SetString(_settings.GetLocalVersionKey(_playMode, _distribution), version);
            PlayerPrefs.Save();
        }

        private void Report(StartupState state, string message)
        {
            _report(state, 0f, message);
        }

        private sealed class DistributionRemoteService : IRemoteService
        {
            private readonly DistributionPathResolver _resolver;

            public DistributionRemoteService(DistributionPathResolver resolver)
            {
                _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            }

            public IReadOnlyList<string> GetRemoteUrls(string fileName)
            {
                return new[] { _resolver.ResolveYooAssetUrl(fileName) };
            }
        }
    }
}
