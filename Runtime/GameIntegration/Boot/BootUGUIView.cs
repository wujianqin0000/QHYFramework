using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameIntegration
{
    /// <summary>
    /// Boot 专用 UGUI Prefab 视图。Prefab 是内置资源，不依赖 YooAsset，可直接在 Inspector 中定制。
    /// </summary>
    public sealed class BootUGUIView : MonoBehaviour
    {
        public const string DefaultResourcesPath = "Boot/BootUI";

        [Header("Status")]
        [SerializeField] private Text status;
        [SerializeField] private Text detail;
        [SerializeField] private Text error;
        [SerializeField] private Image progressFill;

        [Header("Actions")]
        [SerializeField] private Button confirmButton;
        [SerializeField] private Text confirmText;
        [SerializeField] private Button retryButton;

        private Action _confirm;
        private Action _retry;
        private Sprite _generatedProgressSprite;

        public static BootUGUIView Create(BootUGUIView prefab, Transform owner, Action confirm, Action retry)
        {
            if (!prefab)
                prefab = Resources.Load<BootUGUIView>(DefaultResourcesPath);
            if (!prefab)
            {
                throw new InvalidOperationException(
                    $"缺少 Boot UI Prefab：Resources/{DefaultResourcesPath}.prefab。" +
                    "QHY Framework 自动初始化尚未完成，请检查 Console 后重新导入包。");
            }

            BootUGUIView instance = Instantiate(prefab, owner, false);
            instance.name = prefab.name;
            instance.Initialize(confirm, retry);
            return instance;
        }

        public void Render(StartupProgress progress, string errorMessage, bool busy,
            bool waitingForConfirmation, int totalDownloadCount, long totalDownloadBytes, float bytesPerSecond)
        {
            Render(progress, errorMessage, busy, waitingForConfirmation,
                $"下载更新（{totalDownloadCount} 个文件，{GamePackageRuntime.FormatBytes(totalDownloadBytes)}）",
                bytesPerSecond);
        }

        public void Render(StartupProgress progress, string errorMessage, bool busy,
            bool waitingForConfirmation, string confirmLabel, float bytesPerSecond)
        {
            if (!this)
                return;
            ValidateBindings();

            status.text = string.IsNullOrEmpty(progress.Message) ? "准备启动…" : progress.Message;
            float normalizedProgress = progress.Progress;
            if ((progress.State == StartupState.Downloading ||
                 progress.State == StartupState.DownloadingClient) && progress.TotalBytes > 0)
            {
                normalizedProgress = (float)((double)progress.CurrentBytes / progress.TotalBytes);
            }
            progressFill.fillAmount = Mathf.Clamp01(normalizedProgress);
            detail.text = progress.TotalBytes > 0
                ? $"{progress.CurrentFiles}/{progress.TotalFiles}    " +
                  $"{GamePackageRuntime.FormatBytes(progress.CurrentBytes)}/" +
                  $"{GamePackageRuntime.FormatBytes(progress.TotalBytes)}    " +
                  $"{GamePackageRuntime.FormatBytes((long)Mathf.Max(0, bytesPerSecond))}/s"
                : string.Empty;
            error.text = errorMessage ?? string.Empty;

            confirmButton.gameObject.SetActive(waitingForConfirmation && string.IsNullOrEmpty(errorMessage));
            confirmButton.interactable = !busy;
            confirmText.text = confirmLabel ?? "下载更新";
            retryButton.gameObject.SetActive(!string.IsNullOrEmpty(errorMessage));
            retryButton.interactable = !busy;
        }

        public void DestroyView()
        {
            if (this)
                Destroy(gameObject);
        }

        private void Initialize(Action confirm, Action retry)
        {
            ValidateBindings();
            progressFill.fillAmount = 0f;
            _confirm = confirm;
            _retry = retry;
            confirmButton.onClick.RemoveListener(OnConfirmClicked);
            retryButton.onClick.RemoveListener(OnRetryClicked);
            confirmButton.onClick.AddListener(OnConfirmClicked);
            retryButton.onClick.AddListener(OnRetryClicked);
        }

        private void OnDestroy()
        {
            if (confirmButton)
                confirmButton.onClick.RemoveListener(OnConfirmClicked);
            if (retryButton)
                retryButton.onClick.RemoveListener(OnRetryClicked);
            if (_generatedProgressSprite)
                Destroy(_generatedProgressSprite);
            _confirm = null;
            _retry = null;
        }

        private void OnConfirmClicked()
        {
            _confirm?.Invoke();
        }

        private void OnRetryClicked()
        {
            _retry?.Invoke();
        }

        private void ValidateBindings()
        {
            if (!status || !detail || !error || !progressFill || !confirmButton || !confirmText || !retryButton)
            {
                throw new InvalidOperationException(
                    $"Boot UI Prefab {name} 的组件引用不完整，请检查 BootUGUIView Inspector。 ");
            }

            EnsureProgressFillCanRender();
        }

        private void EnsureProgressFillCanRender()
        {
            // Image 没有 Sprite 时会退回 Graphic 的普通矩形绘制，fillAmount 不参与网格生成。
            // 默认 Boot Prefab 为了不依赖外部资源没有绑定 Sprite，因此在运行时使用 Unity 内置白纹理。
            if (!progressFill.sprite)
            {
                Texture2D texture = Texture2D.whiteTexture;
                _generatedProgressSprite = Sprite.Create(texture,
                    new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                _generatedProgressSprite.name = "BootProgressFill_RuntimeSprite";
                progressFill.sprite = _generatedProgressSprite;
            }

            if (progressFill.type != Image.Type.Filled)
            {
                progressFill.type = Image.Type.Filled;
                progressFill.fillMethod = Image.FillMethod.Horizontal;
                progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
        }
    }
}
