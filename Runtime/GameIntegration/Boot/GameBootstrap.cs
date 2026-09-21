using System;
using System.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace GameIntegration
{
    [DefaultExecutionOrder(-10000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public EPlayMode PlayMode = EPlayMode.EditorSimulateMode;
        [SerializeField] private QHYFrameworkSettings settings;
        [Tooltip("可选。为空时使用内置 Resources/Boot/BootUI.prefab。")]
        [SerializeField] private BootUGUIView bootViewPrefab;

        private GamePackageRuntime _runtime;
        private ClientUpdateService _clientUpdate;
        private StartupProgress _progress;
        private bool _busy;
        private string _error = string.Empty;
        private long _lastBytes;
        private float _lastSampleTime;
        private float _bytesPerSecond;
        private BootUGUIView _view;
        private bool _leavingBoot;
        private bool _waitingClientUpdate;
        private DistributionRuntimeConfig _distribution;

        private void Awake()
        {
#if !UNITY_EDITOR
            // EditorSimulateMode 仅编辑器可用；打包时只转换该模式，保留显式配置的其它模式。
            if (PlayMode == EPlayMode.EditorSimulateMode)
                PlayMode = EPlayMode.HostPlayMode;
#endif
            Debug.Log($"[QHYFramework] Resource system play mode: {PlayMode}.");
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
        }

        private async void Start()
        {
            _view = BootUGUIView.Create(bootViewPrefab, transform,
                () => _ = ConfirmCurrentUpdateAsync(), () => _ = RetryAsync());
            if (!settings)
            {
                _error = "Boot 未绑定 QHYFrameworkSettings。请检查 QHY Framework 自动初始化日志后重新导入包。";
                RenderView();
                return;
            }

            await BeginStartupAsync();
        }

        private void OnDestroy()
        {
            if (_runtime != null)
            {
                _runtime.ProgressChanged -= OnProgressChanged;
                // 切换到 Main 时只销毁 Boot 表现层；资源包运行时由纯 C# 静态入口继续持有。
                if (!_leavingBoot)
                    _runtime.Dispose();
            }
            _view?.DestroyView();
            _view = null;
        }

        private async Task BeginStartupAsync()
        {
            if (_busy)
                return;
            _busy = true;
            _error = string.Empty;
            try
            {
                _distribution ??= DistributionRuntimeConfigLoader.Load(settings, PlayMode);
                if (PlayMode == EPlayMode.HostPlayMode)
                {
                    _clientUpdate ??= new ClientUpdateService(settings, _distribution, OnProgressChanged);
                    if (await _clientUpdate.CheckAsync())
                    {
                        _waitingClientUpdate = true;
                        return;
                    }
                }

                _waitingClientUpdate = false;
                if (_runtime == null)
                {
                    _runtime = new GamePackageRuntime(settings, PlayMode, _distribution);
                    _runtime.ProgressChanged += OnProgressChanged;
                }
                await _runtime.StartAsync();
                if (!_runtime.NeedsDownloadConfirmation)
                    await ContinueIntoGameAsync();
            }
            catch (Exception exception)
            {
                _error = exception.GetBaseException().Message;
            }
            finally
            {
                _busy = false;
                RenderView();
            }
        }

        private async Task ConfirmCurrentUpdateAsync()
        {
            if (_waitingClientUpdate)
            {
                await ConfirmClientUpdateAsync();
                return;
            }
            await ConfirmDownloadAsync();
        }

        private async Task RetryAsync()
        {
            if (_clientUpdate?.HasUpdate == true)
                await ConfirmClientUpdateAsync();
            else
                await BeginStartupAsync();
        }

        private async Task ConfirmClientUpdateAsync()
        {
            if (_busy) return;
            _busy = true;
            _error = string.Empty;
            try
            {
                await _clientUpdate.DownloadAsync();
                await _clientUpdate.InstallAndRestartAsync();
            }
            catch (Exception exception)
            {
                _error = exception.GetBaseException().Message;
            }
            finally
            {
                _busy = false;
                RenderView();
            }
        }

        private async Task ConfirmDownloadAsync()
        {
            if (_busy)
                return;
            _busy = true;
            _error = string.Empty;
            try
            {
                await _runtime.ConfirmDownloadAsync();
                await ContinueIntoGameAsync();
            }
            catch (Exception exception)
            {
                _error = exception.GetBaseException().Message;
            }
            finally
            {
                _busy = false;
                RenderView();
            }
        }

        private async Task ContinueIntoGameAsync()
        {
            await _runtime.LoadHotUpdateAssembliesAsync();
            _leavingBoot = true;
            try
            {
                await _runtime.LoadStartupSceneAsync();
            }
            catch
            {
                // 场景加载失败时 Boot 仍存在，允许继续显示错误和重试。
                _leavingBoot = false;
                throw;
            }
        }

        private void OnProgressChanged(StartupProgress value)
        {
            _progress = value;
            if (value.State == StartupState.Downloading || value.State == StartupState.DownloadingClient)
            {
                float now = Time.realtimeSinceStartup;
                float delta = now - _lastSampleTime;
                if (delta >= 0.25f)
                {
                    _bytesPerSecond = (value.CurrentBytes - _lastBytes) / delta;
                    _lastBytes = value.CurrentBytes;
                    _lastSampleTime = now;
                }
            }
            RenderView();
        }

        private void RenderView()
        {
            if (!this || _view == null)
                return;
            bool waiting = _waitingClientUpdate || _runtime?.NeedsDownloadConfirmation == true;
            string label = _waitingClientUpdate && _clientUpdate?.Manifest != null
                ? $"更新客户端到 {_clientUpdate.Manifest.Version}（{GamePackageRuntime.FormatBytes(_clientUpdate.Manifest.SizeBytes)}）"
                : $"下载资源更新（{_runtime?.TotalDownloadCount ?? 0} 个文件，" +
                  $"{GamePackageRuntime.FormatBytes(_runtime?.TotalDownloadBytes ?? 0)}）";
            _view?.Render(_progress, _error, _busy, waiting, label, _bytesPerSecond);
        }
    }
}
