using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HybridCLR;
using UnityEngine;
using YooAsset;

namespace GameIntegration
{
    /// <summary>
    /// 从 YooAsset 加载 AOT 补充元数据和热更程序集。记录已加载项，保证失败重试不会重复装载 DLL。
    /// </summary>
    internal sealed class HotUpdateAssemblyLoader
    {
        private readonly ResourcePackage _package;
        private readonly QHYFrameworkSettings _settings;
        private readonly EPlayMode _playMode;
        private readonly Action<StartupState, float, string> _report;
        private readonly HashSet<string> _loadedMetadata = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _loadedAssemblies = new HashSet<string>(StringComparer.Ordinal);

        public HotUpdateAssemblyLoader(ResourcePackage package, QHYFrameworkSettings settings, EPlayMode playMode,
            Action<StartupState, float, string> report)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _playMode = playMode;
            _report = report;
        }

        public async Task LoadAsync()
        {
#if UNITY_EDITOR
            // EditorSimulateMode 直接使用 Unity Editor 已编译并加载的程序集。
            // 新项目尚未产生热更 DLL 资源时也不应尝试从 YooAsset 清单动态加载。
            if (_playMode == EPlayMode.EditorSimulateMode)
            {
                _report(StartupState.LoadingHotUpdateAssemblies, 1f,
                    "EditorSimulateMode 使用编辑器内已加载的热更新程序集");
                return;
            }
#endif
            await LoadMetadataAsync();
            await LoadHotUpdateAssembliesAsync();
        }

        private async Task LoadMetadataAsync()
        {
            _report(StartupState.LoadingMetadata, 0f, "正在加载 AOT 补充元数据…");
            string[] names = _settings.aotMetadataAssemblyNames ?? Array.Empty<string>();
            for (int index = 0; index < names.Length; index++)
            {
                string address = names[index]?.Trim();
                if (string.IsNullOrEmpty(address) || _loadedMetadata.Contains(address))
                    continue;

                byte[] bytes = await LoadBinaryAssetAsync(address, StartupState.LoadingMetadata);
                LoadImageErrorCode result = RuntimeApi.LoadMetadataForAOTAssembly(
                    bytes, HomologousImageMode.SuperSet);
                if (result != LoadImageErrorCode.OK &&
                    result != LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED)
                {
                    throw new StartupException(StartupState.LoadingMetadata,
                        $"加载 AOT 元数据 {address} 失败：{result}");
                }

                _loadedMetadata.Add(address);
                _report(StartupState.LoadingMetadata, (index + 1f) / names.Length,
                    $"已加载 AOT 元数据 {address}");
            }
        }

        private async Task LoadHotUpdateAssembliesAsync()
        {
            _report(StartupState.LoadingHotUpdateAssemblies, 0f, "正在加载热更新程序集…");
            HotUpdateAssemblySpec[] specs = _settings.hotUpdateAssemblies ?? Array.Empty<HotUpdateAssemblySpec>();
            for (int index = 0; index < specs.Length; index++)
            {
                HotUpdateAssemblySpec spec = specs[index];
                if (spec == null || string.IsNullOrWhiteSpace(spec.assetAddress))
                    continue;

                string assemblyName = spec.assemblyName?.Trim();
                if (!string.IsNullOrEmpty(assemblyName) && IsAssemblyLoaded(assemblyName))
                {
                    _loadedAssemblies.Add(assemblyName);
                    continue;
                }

                byte[] bytes = await LoadBinaryAssetAsync(
                    spec.assetAddress.Trim(), StartupState.LoadingHotUpdateAssemblies);
                Assembly assembly = Assembly.Load(bytes);
                _loadedAssemblies.Add(assembly.GetName().Name);
                _report(StartupState.LoadingHotUpdateAssemblies, (index + 1f) / specs.Length,
                    $"已加载热更新程序集 {assembly.GetName().Name}");
            }
        }

        private bool IsAssemblyLoaded(string assemblyName)
        {
            if (_loadedAssemblies.Contains(assemblyName))
                return true;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private async Task<byte[]> LoadBinaryAssetAsync(string address, StartupState state)
        {
            AssetHandle handle = _package.LoadAssetAsync<TextAsset>(address);
            await handle;
            try
            {
                YooOperation.EnsureSucceeded(handle, state, $"加载二进制资源 {address}");
                TextAsset asset = handle.GetAssetObject<TextAsset>();
                if (!asset)
                    throw new StartupException(state, $"二进制资源 {address} 不是 TextAsset。");
                return asset.bytes;
            }
            finally
            {
                handle.Release();
            }
        }
    }
}
