using System;
using System.Collections;
using QFramework;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

namespace GameIntegration
{
    public static class QFrameworkYooAssetAdapters
    {
        private static bool _installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _installed = false;
        }

        public static void Install(GamePackageRuntime runtime, IResourceAddressResolver resolver)
        {
            if (_installed)
                return;
            if (runtime?.Package == null)
                throw new ArgumentNullException(nameof(runtime));

            ResFactory.mResCreators.RemoveAll(item => item is YooAssetResCreator);
            ResFactory.mResCreators.Insert(0, new YooAssetResCreator(runtime.Package, resolver));
            UIKit.Config = new YooAssetUIKitConfig(runtime.Package, resolver);
            AudioKit.Config.AudioLoaderPool = new YooAssetAudioLoaderPool(runtime.Package, resolver);
            _installed = true;
        }
    }

    public sealed class YooAssetResCreator : IResCreator
    {
        private readonly ResourcePackage _package;
        private readonly IResourceAddressResolver _resolver;

        public YooAssetResCreator(ResourcePackage package, IResourceAddressResolver resolver)
        {
            _package = package;
            _resolver = resolver;
        }

        public bool Match(ResSearchKeys keys)
        {
            string name = keys?.OriginalAssetName ?? string.Empty;
            return !name.StartsWith("netimage:", StringComparison.OrdinalIgnoreCase) &&
                   !name.StartsWith("localimage:", StringComparison.OrdinalIgnoreCase) &&
                   keys?.AssetType != typeof(AssetBundle);
        }

        public IRes Create(ResSearchKeys keys)
        {
            string address = _resolver.Resolve(keys.OriginalAssetName, keys.OwnerBundle, keys.AssetType);
            return new YooAssetRes(_package, address, keys.AssetName, keys.OwnerBundle, keys.AssetType);
        }
    }

    public sealed class YooAssetRes : Res
    {
        private readonly ResourcePackage _package;
        private readonly string _address;
        private AssetHandle _handle;

        public YooAssetRes(ResourcePackage package, string address, string qFrameworkAssetName,
            string ownerBundle, Type assetType) : base(qFrameworkAssetName)
        {
            _package = package;
            _address = address;
            OwnerBundleName = ownerBundle;
            AssetType = assetType ?? typeof(Object);
        }

        public override bool LoadSync()
        {
            if (!CheckLoadAble())
                return State == ResState.Ready;
            State = ResState.Loading;
            _handle = _package.LoadAssetSync(_address, AssetType);
            if (_handle.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[QHYFramework] 加载资源 {_address} 失败：{_handle.Error}");
                _handle.Release();
                _handle = null;
                OnResLoadFaild();
                return false;
            }
            mAsset = _handle.AssetObject;
            State = ResState.Ready;
            return true;
        }

        public override void LoadAsync()
        {
            if (!CheckLoadAble())
                return;
            State = ResState.Loading;
            _handle = _package.LoadAssetAsync(_address, AssetType);
            _handle.Completed += OnHandleCompleted;
        }

        public override IEnumerator DoLoadAsync(Action finishCallback)
        {
            if (!CheckLoadAble())
            {
                finishCallback?.Invoke();
                yield break;
            }
            State = ResState.Loading;
            _handle = _package.LoadAssetAsync(_address, AssetType);
            yield return _handle;
            CompleteHandle();
            finishCallback?.Invoke();
        }

        protected override float CalculateProgress()
        {
            return _handle?.Progress ?? 0f;
        }

        protected override void OnReleaseRes()
        {
            mAsset = null;
            if (_handle != null)
            {
                _handle.Release();
                _handle = null;
            }
        }

        private void OnHandleCompleted(AssetHandle handle)
        {
            CompleteHandle();
        }

        private void CompleteHandle()
        {
            if (_handle != null && _handle.Status == EOperationStatus.Succeeded)
            {
                mAsset = _handle.AssetObject;
                State = ResState.Ready;
            }
            else
            {
                Debug.LogError($"[QHYFramework] 异步加载资源 {_address} 失败：{_handle?.Error}");
                _handle?.Release();
                _handle = null;
                OnResLoadFaild();
            }
        }
    }

    internal sealed class YooAssetUIKitConfig : UIKitConfig
    {
        private readonly ResourcePackage _package;
        private readonly IResourceAddressResolver _resolver;
        private UIRoot _root;
        private AssetHandle _rootHandle;

        public YooAssetUIKitConfig(ResourcePackage package, IResourceAddressResolver resolver)
        {
            _package = package;
            _resolver = resolver;
            PanelLoaderPool = new YooAssetPanelLoaderPool(package, resolver);
        }

        public override UIRoot Root
        {
            get
            {
                if (_root)
                    return _root;
                _root = Object.FindObjectOfType<UIRoot>();
                if (_root)
                    return _root;

                string address = _resolver.Resolve("UIRoot", null, typeof(GameObject));
                _rootHandle = _package.LoadAssetSync<GameObject>(address);
                YooOperation.EnsureSucceeded(_rootHandle, StartupState.LoadingStartupScene, "加载 UIRoot");
                GameObject instance = Object.Instantiate(_rootHandle.GetAssetObject<GameObject>());
                _root = instance.GetComponent<UIRoot>();
                if (!_root)
                    throw new InvalidOperationException("UIRoot 资源缺少 QFramework.UIRoot 组件。");
                instance.name = "UIRoot";
                Object.DontDestroyOnLoad(instance);
                return _root;
            }
        }
    }

    public sealed class YooAssetPanelLoaderPool : AbstractPanelLoaderPool
    {
        private readonly ResourcePackage _package;
        private readonly IResourceAddressResolver _resolver;

        public YooAssetPanelLoaderPool(ResourcePackage package, IResourceAddressResolver resolver)
        {
            _package = package;
            _resolver = resolver;
        }

        protected override IPanelLoader CreatePanelLoader()
        {
            return new Loader(_package, _resolver);
        }

        private sealed class Loader : IPanelLoader
        {
            private readonly ResourcePackage _package;
            private readonly IResourceAddressResolver _resolver;
            private AssetHandle _handle;

            public Loader(ResourcePackage package, IResourceAddressResolver resolver)
            {
                _package = package;
                _resolver = resolver;
            }

            public GameObject LoadPanelPrefab(PanelSearchKeys keys)
            {
                string name = !string.IsNullOrWhiteSpace(keys.GameObjName) ? keys.GameObjName : keys.PanelType?.Name;
                string address = _resolver.Resolve(name, keys.AssetBundleName, typeof(GameObject));
                _handle = _package.LoadAssetSync<GameObject>(address);
                YooOperation.EnsureSucceeded(_handle, StartupState.Completed, $"加载 UI 面板 {address}");
                GameObject prefab = _handle.GetAssetObject<GameObject>();
                EnsurePanelComponent(prefab, address);
                return prefab;
            }

            public void LoadPanelPrefabAsync(PanelSearchKeys keys, Action<GameObject> onPanelPrefabLoad)
            {
                string name = !string.IsNullOrWhiteSpace(keys.GameObjName) ? keys.GameObjName : keys.PanelType?.Name;
                string address = _resolver.Resolve(name, keys.AssetBundleName, typeof(GameObject));
                _handle = _package.LoadAssetAsync<GameObject>(address);
                _handle.Completed += handle =>
                {
                    if (handle.Status != EOperationStatus.Succeeded)
                    {
                        Debug.LogError($"[QHYFramework] 加载 UI 面板 {address} 失败：{handle.Error}");
                        onPanelPrefabLoad?.Invoke(null);
                        return;
                    }

                    GameObject prefab = handle.GetAssetObject<GameObject>();
                    try
                    {
                        EnsurePanelComponent(prefab, address);
                        onPanelPrefabLoad?.Invoke(prefab);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        onPanelPrefabLoad?.Invoke(null);
                    }
                };
            }

            private static void EnsurePanelComponent(GameObject prefab, string address)
            {
                if (!prefab)
                    throw new InvalidOperationException($"UI 面板资源 {address} 加载成功，但 Prefab 为空。");
                if (!prefab.GetComponent<UIPanel>())
                {
                    throw new InvalidOperationException(
                        $"UI 面板资源 {address} 的根节点没有挂载 UIPanel 派生组件。" +
                        "请把对应 Panel 脚本挂到 Prefab 根节点后重新构建 YooAsset。");
                }
            }

            public void Unload()
            {
                _handle?.Release();
                _handle = null;
            }
        }
    }

    public sealed class YooAssetAudioLoaderPool : AbstractAudioLoaderPool
    {
        private readonly ResourcePackage _package;
        private readonly IResourceAddressResolver _resolver;

        public YooAssetAudioLoaderPool(ResourcePackage package, IResourceAddressResolver resolver)
        {
            _package = package;
            _resolver = resolver;
        }

        protected override IAudioLoader CreateLoader()
        {
            return new Loader(_package, _resolver);
        }

        private sealed class Loader : IAudioLoader
        {
            private readonly ResourcePackage _package;
            private readonly IResourceAddressResolver _resolver;
            private AssetHandle _handle;
            public AudioClip Clip { get; private set; }

            public Loader(ResourcePackage package, IResourceAddressResolver resolver)
            {
                _package = package;
                _resolver = resolver;
            }

            public AudioClip LoadClip(AudioSearchKeys keys)
            {
                string address = _resolver.Resolve(keys.AssetName, keys.AssetBundleName, typeof(AudioClip));
                _handle = _package.LoadAssetSync<AudioClip>(address);
                if (_handle.Status == EOperationStatus.Succeeded)
                    Clip = _handle.GetAssetObject<AudioClip>();
                else
                    Debug.LogError($"[QHYFramework] 加载音频 {address} 失败：{_handle.Error}");
                return Clip;
            }

            public void LoadClipAsync(AudioSearchKeys keys, Action<bool, AudioClip> onLoad)
            {
                string address = _resolver.Resolve(keys.AssetName, keys.AssetBundleName, typeof(AudioClip));
                _handle = _package.LoadAssetAsync<AudioClip>(address);
                _handle.Completed += handle =>
                {
                    Clip = handle.GetAssetObject<AudioClip>();
                    if (handle.Status != EOperationStatus.Succeeded)
                        Debug.LogError($"[QHYFramework] 加载音频 {address} 失败：{handle.Error}");
                    onLoad?.Invoke(Clip != null, Clip);
                };
            }

            public void Unload()
            {
                Clip = null;
                _handle?.Release();
                _handle = null;
            }
        }
    }
}
