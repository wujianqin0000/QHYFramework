using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace WJQ.QHYFramework.Editor
{
    [InitializeOnLoad]
    internal static class QHYDependencyInstaller
    {
        private const string HybridClrPackage = "com.code-philosophy.hybridclr";
        private const string YooAssetPackage = "com.tuyoogame.yooasset";
        private const string HybridClrArchive = "com.code-philosophy.hybridclr-8.12.0.tgz";
        private const string YooAssetArchive = "com.tuyoogame.yooasset-3.0.4.tgz";
        private const string DependencyDirectory = "Packages/QHYDependencies";
        private const string EmbeddedHybridClrDirectory = "Packages/com.code-philosophy.hybridclr";
        private const string SessionInstallingKey = "WJQ.QHYFramework.DependencyInstaller.Installing";

        private static AddRequest _request;
        private static string _installingPackage;

        static QHYDependencyInstaller()
        {
            // HybridCLR writes UNITY_IL2CPP_PATH into the Unity Editor process when
            // useGlobalIl2cpp is disabled. A project copied or reopened without the
            // generated LocalIl2CppData directory would then fail in Bee before the
            // main GameIntegration editor assembly can even compile. Keep first import
            // on Unity's built-in IL2CPP until the first Full Package Build installs
            // HybridCLR's patched toolchain.
            EnsureSafeIl2CppConfiguration();
            EditorApplication.delayCall += EnsureDependencies;
        }

        private static void EnsureDependencies()
        {
            EnsureSafeIl2CppConfiguration();
            if (_request != null || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureDependencies;
                return;
            }

            PackageManagerPackageInfo[] registered = PackageManagerPackageInfo.GetAllRegisteredPackages();
            PackageManagerPackageInfo hybridClr = registered.FirstOrDefault(package =>
                string.Equals(package.name, HybridClrPackage, StringComparison.OrdinalIgnoreCase));
            PackageManagerPackageInfo yooAsset = registered.FirstOrDefault(package =>
                string.Equals(package.name, YooAssetPackage, StringComparison.OrdinalIgnoreCase));

            string pendingPackage = SessionState.GetString(SessionInstallingKey, string.Empty);
            if (!string.IsNullOrEmpty(pendingPackage))
            {
                bool registrationCompleted = pendingPackage.StartsWith(HybridClrPackage,
                                                 StringComparison.OrdinalIgnoreCase)
                                             ? hybridClr != null
                                             : pendingPackage.StartsWith(YooAssetPackage,
                                                   StringComparison.OrdinalIgnoreCase) && yooAsset != null;
                if (!registrationCompleted)
                {
                    EditorApplication.delayCall += EnsureDependencies;
                    return;
                }
                SessionState.EraseString(SessionInstallingKey);
            }

            if (hybridClr == null)
            {
                Install(HybridClrPackage, HybridClrArchive);
                return;
            }
            if (!Directory.Exists(Path.GetFullPath(EmbeddedHybridClrDirectory)))
            {
                EmbedHybridClr(hybridClr);
                return;
            }
            if (yooAsset == null)
            {
                Install(YooAssetPackage, YooAssetArchive);
                return;
            }

            SessionState.EraseString(SessionInstallingKey);
        }

        private static void EmbedHybridClr(PackageManagerPackageInfo package)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath) ||
                !Directory.Exists(package.resolvedPath))
                throw new DirectoryNotFoundException("Unable to locate the installed HybridCLR package directory.");

            string destination = Path.GetFullPath(EmbeddedHybridClrDirectory);
            string staging = Path.GetFullPath(DependencyDirectory + "/HybridCLR.staging");
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            CopyDirectory(package.resolvedPath, staging);
            if (Directory.Exists(destination))
                Directory.Delete(destination, true);
            Directory.Move(staging, destination);

            _installingPackage = HybridClrPackage + " (embedded)";
            SessionState.SetString(SessionInstallingKey, _installingPackage);
            Debug.Log(L(
                "[QHY Framework] 正在将 HybridCLR 转为项目内嵌包，以支持其本地 il2cpp 工具…",
                "[QHY Framework] Embedding HybridCLR so its local il2cpp tools can use the physical package path..."));
            _request = Client.Add("file:com.code-philosophy.hybridclr");
            EditorApplication.update += PollRequest;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string relative = directory.Substring(source.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                File.Copy(file, target, true);
            }
        }

        private static void Install(string packageName, string archiveName)
        {
            EnsureSafeIl2CppConfiguration();
            string packageRoot = GetPackageRoot();
            string source = Path.Combine(packageRoot, "Dependencies~", archiveName);
            if (!File.Exists(source))
            {
                Debug.LogError(L(
                    $"[QHY Framework] 缺少离线依赖包：{source}。请重新获取完整的 com.wjq.qhy-framework 包。",
                    $"[QHY Framework] Bundled dependency archive is missing: {source}. Reinstall the complete com.wjq.qhy-framework package."));
                return;
            }

            string destinationDirectory = Path.GetFullPath(DependencyDirectory);
            Directory.CreateDirectory(destinationDirectory);
            string destination = Path.Combine(destinationDirectory, archiveName);
            File.Copy(source, destination, true);

            _installingPackage = packageName;
            SessionState.SetString(SessionInstallingKey, packageName);
            string relativeIdentifier = "file:QHYDependencies/" + archiveName;
            Debug.Log(L(
                $"[QHY Framework] 正在自动安装依赖 {packageName}…",
                $"[QHY Framework] Installing dependency {packageName} automatically..."));
            _request = Client.Add(relativeIdentifier);
            EditorApplication.update += PollRequest;
        }

        private static void PollRequest()
        {
            if (_request == null || !_request.IsCompleted)
                return;

            EditorApplication.update -= PollRequest;
            if (_request.Status == StatusCode.Success)
            {
                Debug.Log(L(
                    $"[QHY Framework] 依赖安装完成：{_installingPackage}",
                    $"[QHY Framework] Dependency installed: {_installingPackage}"));
                _request = null;
                _installingPackage = null;
                EditorApplication.delayCall += EnsureDependencies;
                return;
            }

            string error = _request.Error?.message ?? "Unknown Package Manager error.";
            Debug.LogError(L(
                $"[QHY Framework] 自动安装依赖 {_installingPackage} 失败：{error}\n修正错误后重新导入 com.wjq.qhy-framework 即可重试。",
                $"[QHY Framework] Failed to install dependency {_installingPackage}: {error}\nFix the error and reimport com.wjq.qhy-framework to retry."));
            SessionState.EraseString(SessionInstallingKey);
            _request = null;
            _installingPackage = null;
        }

        private static string GetPackageRoot()
        {
            PackageManagerPackageInfo package = PackageManagerPackageInfo.FindForAssembly(typeof(QHYDependencyInstaller).Assembly);
            if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
                throw new InvalidOperationException("Unable to locate com.wjq.qhy-framework package root.");
            return package.resolvedPath;
        }

        private static void EnsureSafeIl2CppConfiguration()
        {
            if (HasInstalledLocalIl2Cpp())
                return;

            Environment.SetEnvironmentVariable("UNITY_IL2CPP_PATH", string.Empty);
            const string settingsPath = "ProjectSettings/HybridCLRSettings.asset";
            if (File.Exists(settingsPath))
            {
                string yaml = File.ReadAllText(settingsPath);
                string safeYaml = yaml.Replace("  useGlobalIl2cpp: 0", "  useGlobalIl2cpp: 1");
                if (!string.Equals(yaml, safeYaml, StringComparison.Ordinal))
                    File.WriteAllText(settingsPath, safeYaml);
            }

            // If HybridCLR.Editor is already loaded, also update its cached
            // ScriptableObject. Editing only the YAML would otherwise take effect on
            // the next domain reload, which is too late for the current editor process.
            Type settingsType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("HybridCLR.Editor.Settings.HybridCLRSettings", false))
                .FirstOrDefault(type => type != null);
            PropertyInfo instanceProperty = settingsType?.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            object instance = instanceProperty?.GetValue(null);
            FieldInfo useGlobalField = settingsType?.GetField("useGlobalIl2cpp",
                BindingFlags.Public | BindingFlags.Instance);
            if (instance != null && useGlobalField != null && !(bool)useGlobalField.GetValue(instance))
            {
                useGlobalField.SetValue(instance, true);
                settingsType.GetMethod("Save", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            }
        }

        private static bool HasInstalledLocalIl2Cpp()
        {
            string editorPlatform;
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    editorPlatform = "MacEditor";
                    break;
                case RuntimePlatform.LinuxEditor:
                    editorPlatform = "LinuxEditor";
                    break;
                default:
                    editorPlatform = "WindowsEditor";
                    break;
            }

            string localRoot = Path.GetFullPath(Path.Combine("HybridCLRData",
                "LocalIl2CppData-" + editorPlatform, "il2cpp"));
            return Directory.Exists(Path.Combine(localRoot, "libil2cpp", "hybridclr")) &&
                   Directory.Exists(Path.Combine(localRoot, "il2cpp", "bin"));
        }

        private static string L(string chinese, string english)
        {
            return Application.systemLanguage == SystemLanguage.ChineseSimplified ||
                   Application.systemLanguage == SystemLanguage.ChineseTraditional
                ? chinese
                : english;
        }
    }
}
