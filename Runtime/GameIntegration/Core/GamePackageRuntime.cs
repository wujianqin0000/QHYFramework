using System;
using System.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace GameIntegration
{
    /// <summary>
    /// 启动流程门面。公共 API 保持稳定，具体补丁状态链与 HybridCLR 加载职责分别委托给独立对象。
    /// </summary>
    public sealed class GamePackageRuntime : IDisposable
    {
        private readonly QHYFrameworkSettings _settings;
        private readonly PackagePatchWorkflow _patchWorkflow;
        private HotUpdateAssemblyLoader _assemblyLoader;
        private bool _adaptersInstalled;

        public static GamePackageRuntime Current { get; private set; }
        public QHYFrameworkSettings Settings => _settings;
        public EPlayMode PlayMode { get; }
        public DistributionRuntimeConfig Distribution { get; }
        public ResourcePackage Package => _patchWorkflow.Package;
        public StartupState State { get; private set; } = StartupState.Idle;
        public string Error { get; private set; } = string.Empty;
        public bool NeedsDownloadConfirmation => State == StartupState.WaitingForDownloadConfirmation;
        public int TotalDownloadCount => _patchWorkflow.Downloader?.TotalDownloadCount ?? 0;
        public long TotalDownloadBytes => _patchWorkflow.Downloader?.TotalDownloadBytes ?? 0;

        public event Action<StartupProgress> ProgressChanged;
        public event Action<StartupState> StateChanged;

        public GamePackageRuntime(QHYFrameworkSettings settings)
            : this(settings, GetDefaultPlayMode())
        {
        }

        public GamePackageRuntime(QHYFrameworkSettings settings, EPlayMode playMode)
            : this(settings, playMode, DistributionRuntimeConfigLoader.Load(settings, playMode))
        {
        }

        public GamePackageRuntime(QHYFrameworkSettings settings, EPlayMode playMode,
            DistributionRuntimeConfig distribution)
        {
            _settings = settings ? settings : throw new ArgumentNullException(nameof(settings));
            PlayMode = playMode;
            Distribution = distribution ?? throw new ArgumentNullException(nameof(distribution));
            _patchWorkflow = new PackagePatchWorkflow(
                _settings, Distribution, PlayMode, ReportStage, OnDownloadProgressChanged, OnDownloadError);
            Current = this;
        }

        /// <summary>
        /// 初始化包、检查版本、激活清单并创建下载器。有更新时停在等待确认状态。
        /// </summary>
        public async Task StartAsync()
        {
            try
            {
                Error = string.Empty;
                _settings.ValidateOrThrow(PlayMode);
                await _patchWorkflow.PrepareAsync();
                InstallAdapters();

                if (TotalDownloadCount > 0)
                {
                    ReportStage(StartupState.WaitingForDownloadConfirmation, 0f,
                        $"发现 {TotalDownloadCount} 个更新文件（{FormatBytes(TotalDownloadBytes)}）");
                }
                else
                {
                    ReportStage(StartupState.LoadingManifest, 1f, "资源已经是最新版本");
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
                throw;
            }
        }

        /// <summary>
        /// 对应官方示例的 DownloadPackageFiles -> DownloadPackageOver -> ClearCacheBundle。
        /// </summary>
        public async Task ConfirmDownloadAsync()
        {
            if (TotalDownloadCount == 0)
                return;
            if (State != StartupState.WaitingForDownloadConfirmation)
                throw new InvalidOperationException($"当前状态 {State} 不能开始下载。");

            try
            {
                Error = string.Empty;
                await _patchWorkflow.DownloadAsync();
            }
            catch (Exception exception)
            {
                Fail(exception);
                throw;
            }
        }

        public async Task LoadHotUpdateAssembliesAsync()
        {
            try
            {
                if (Package == null)
                    throw new StartupException(StartupState.LoadingHotUpdateAssemblies,
                        "资源包尚未初始化，不能加载热更新程序集。");

                if (_assemblyLoader == null)
                    _assemblyLoader = new HotUpdateAssemblyLoader(Package, _settings, PlayMode, ReportStage);
                await _assemblyLoader.LoadAsync();
            }
            catch (Exception exception)
            {
                Fail(exception);
                throw;
            }
        }

        public async Task LoadStartupSceneAsync()
        {
            try
            {
                ReportStage(StartupState.LoadingStartupScene, 0f,
                    $"正在进入 {_settings.startupSceneAddress}…");
                await YooAssetSceneKit.LoadSceneAsync(_settings.startupSceneAddress);
                ReportStage(StartupState.Completed, 1f, "启动完成");
            }
            catch (Exception exception)
            {
                Fail(exception);
                throw;
            }
        }

        public void Dispose()
        {
            if (ReferenceEquals(Current, this))
                Current = null;
        }

        private void InstallAdapters()
        {
            if (_adaptersInstalled)
                return;
            QFrameworkYooAssetAdapters.Install(this, new ResourceAddressResolver());
            _adaptersInstalled = true;
        }

        private void OnDownloadProgressChanged(DownloadProgressChangedEventArgs args)
        {
            SetState(StartupState.Downloading);
            ProgressChanged?.Invoke(new StartupProgress(StartupState.Downloading, args.Progress,
                args.CurrentDownloadCount, args.TotalDownloadCount, args.CurrentDownloadBytes,
                args.TotalDownloadBytes, "正在下载资源文件…"));
        }

        private void OnDownloadError(DownloadErrorEventArgs args)
        {
            Debug.LogWarning($"[QHYFramework] 下载 {args.FileName} 失败，将按策略重试：{args.ErrorInfo}");
        }

        private void ReportStage(StartupState state, float progress, string message)
        {
            SetState(state);
            bool downloadStage = state == StartupState.WaitingForDownloadConfirmation ||
                                 state == StartupState.Downloading || state == StartupState.ClearingCache;
            ProgressChanged?.Invoke(new StartupProgress(state, progress, 0,
                downloadStage ? TotalDownloadCount : 0, 0,
                downloadStage ? TotalDownloadBytes : 0, message));
        }

        private void SetState(StartupState state)
        {
            if (State == state)
                return;
            State = state;
            StateChanged?.Invoke(state);
        }

        private void Fail(Exception exception)
        {
            Error = exception.GetBaseException().Message;
            SetState(StartupState.Failed);
            ProgressChanged?.Invoke(new StartupProgress(State, 0f, 0, TotalDownloadCount, 0,
                TotalDownloadBytes, Error));
            Debug.LogException(exception);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024L * 1024) return $"{bytes / 1024f:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024f * 1024f):F1} MB";
            return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
        }

        private static EPlayMode GetDefaultPlayMode()
        {
#if UNITY_EDITOR
            return EPlayMode.EditorSimulateMode;
#else
            return EPlayMode.HostPlayMode;
#endif
        }
    }
}
