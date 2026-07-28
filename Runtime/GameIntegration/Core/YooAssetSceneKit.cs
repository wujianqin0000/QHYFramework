using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace GameIntegration
{
    public static class YooAssetSceneKit
    {
        private static SceneHandle _singleSceneHandle;
        private static readonly Dictionary<string, SceneHandle> AdditiveHandles =
            new Dictionary<string, SceneHandle>(StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _singleSceneHandle = null;
            AdditiveHandles.Clear();
        }

        public static SceneHandle LoadSceneSync(string address, LoadSceneMode mode = LoadSceneMode.Single,
            LocalPhysicsMode physicsMode = LocalPhysicsMode.None)
        {
            ResourcePackage package = RequirePackage();
            SceneHandle previous = mode == LoadSceneMode.Single ? _singleSceneHandle : null;
            SceneHandle handle = package.LoadSceneSync(address, mode, physicsMode);
            YooOperation.EnsureSucceeded(handle, StartupState.LoadingStartupScene, $"加载场景 {address}");
            Track(address, mode, handle, previous);
            return handle;
        }

        public static async Task<SceneHandle> LoadSceneAsync(string address,
            LoadSceneMode mode = LoadSceneMode.Single, LocalPhysicsMode physicsMode = LocalPhysicsMode.None,
            bool allowSceneActivation = true, uint priority = 0)
        {
            ResourcePackage package = RequirePackage();
            SceneHandle previous = mode == LoadSceneMode.Single ? _singleSceneHandle : null;
            SceneHandle handle = package.LoadSceneAsync(address, mode, physicsMode, allowSceneActivation, priority);
            await handle;
            YooOperation.EnsureSucceeded(handle, StartupState.LoadingStartupScene, $"加载场景 {address}");
            Track(address, mode, handle, previous);
            return handle;
        }

        public static async Task UnloadSceneAsync(string address)
        {
            if (!AdditiveHandles.TryGetValue(address, out SceneHandle handle))
                return;
            AdditiveHandles.Remove(address);
            var operation = handle.UnloadSceneAsync();
            await operation;
            YooOperation.EnsureSucceeded(operation, StartupState.LoadingStartupScene, $"卸载场景 {address}");
        }

        private static ResourcePackage RequirePackage()
        {
            if (GamePackageRuntime.Current?.Package == null)
                throw new InvalidOperationException("GamePackage 尚未初始化。");
            return GamePackageRuntime.Current.Package;
        }

        private static void Track(string address, LoadSceneMode mode, SceneHandle handle, SceneHandle previous)
        {
            if (mode == LoadSceneMode.Single)
            {
                _singleSceneHandle = handle;
                if (previous != null && previous.IsValid)
                    previous.Release();
            }
            else
            {
                AdditiveHandles[address] = handle;
            }
        }
    }
}
