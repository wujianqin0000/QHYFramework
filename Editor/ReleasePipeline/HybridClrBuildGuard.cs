using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HybridCLR.Editor;
using HybridCLR.Editor.Installer;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GameIntegration.Editor
{
    /// <summary>
    /// Keeps a release build pinned to HybridCLR's patched IL2CPP toolchain and verifies that
    /// the generated native player actually contains the HybridCLR interpreter.
    /// </summary>
    public static class HybridClrBuildGuard
    {
        public const string ReleaseSessionKey = "WJQ.QHYFramework.HybridCLR.ReleaseInProgress";
        public const string InterpreterMarker = "InterpreterImage::GetClassFromToken";

        private static readonly string[] CompilerCandidates =
        {
            "build/deploy/il2cpp.exe",
            "build/deploy/il2cpp",
            "build/deploy/net471/Unity.IL2CPP.dll",
            "build/deploy/il2cppcore/Unity.IL2CPP.dll",
            "bin/il2cpp.exe",
            "bin/il2cpp"
        };

        public static bool IsLocalIl2CppLayoutValid(string localRoot, out string reason)
        {
            if (string.IsNullOrWhiteSpace(localRoot))
            {
                reason = "本地 IL2CPP 根目录为空。";
                return false;
            }

            string root;
            try
            {
                root = Path.GetFullPath(localRoot);
            }
            catch (Exception exception)
            {
                reason = "本地 IL2CPP 路径无效：" + exception.Message;
                return false;
            }

            string runtime = Path.Combine(root, "libil2cpp", "hybridclr");
            if (!Directory.Exists(runtime))
            {
                reason = "缺少 HybridCLR 运行时目录：" + runtime;
                return false;
            }

            string compiler = CompilerCandidates
                .Select(relative => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)))
                .FirstOrDefault(File.Exists);
            if (compiler == null)
            {
                reason = "缺少本地 IL2CPP 编译器。已检查：" +
                         string.Join("；", CompilerCandidates.Select(relative => Path.Combine(root, relative)));
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static void EnsureInstalledAndVersionMatches()
        {
            string root = SettingsUtil.LocalIl2CppDir;
            if (!IsLocalIl2CppLayoutValid(root, out string layoutError))
                throw new BuildFailedException("HybridCLR 本地 IL2CPP 安装不完整：" + layoutError);

            var installer = new InstallerController();
            string packageVersion = installer.PackageVersion?.Trim();
            string installedVersion = installer.InstalledLibil2cppVersion?.Trim();
            if (string.IsNullOrEmpty(packageVersion) || string.IsNullOrEmpty(installedVersion) ||
                !string.Equals(packageVersion, installedVersion, StringComparison.Ordinal))
            {
                throw new BuildFailedException(
                    $"HybridCLR 包与本地 libil2cpp 版本不一致（Package={packageVersion ?? "<null>"}，" +
                    $"Installed={installedVersion ?? "<null>"}）。请重新安装 HybridCLR 本地 IL2CPP 后再构建。");
            }
        }

        public static void ReassertLocalToolchain()
        {
            EnsureInstalledAndVersionMatches();

            HybridCLRSettings settings = SettingsUtil.HybridCLRSettings;
            settings.enable = true;
            settings.useGlobalIl2cpp = false;
            HybridCLRSettings.Save();

            string expected = NormalizePath(SettingsUtil.LocalIl2CppDir);
            Environment.SetEnvironmentVariable("UNITY_IL2CPP_PATH", expected,
                EnvironmentVariableTarget.Process);
            string effective = NormalizePath(Environment.GetEnvironmentVariable(
                "UNITY_IL2CPP_PATH", EnvironmentVariableTarget.Process));

            // Reload the serialized settings to detect any competing initializer that rewrote them.
            settings = HybridCLRSettings.LoadOrCreate();
            if (!settings.enable || settings.useGlobalIl2cpp ||
                !string.Equals(expected, effective, PathComparison))
            {
                throw new BuildFailedException(
                    "无法锁定 HybridCLR 本地 IL2CPP。" +
                    $"enable={settings.enable}，useGlobalIl2cpp={settings.useGlobalIl2cpp}，" +
                    $"UNITY_IL2CPP_PATH={effective}，expected={expected}");
            }

            Debug.Log("[QHYFramework] 已在 BuildPlayer 前锁定 HybridCLR 本地 IL2CPP：" + expected);
        }

        public static void ValidateNativePlayer(BuildTarget target, string playerLocation)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    ValidateWindowsPlayer(playerLocation);
                    return;
                case BuildTarget.Android:
                    ValidateAndroidPlayer(playerLocation);
                    return;
                default:
                    Debug.LogWarning("[QHYFramework] 当前平台尚未配置 HybridCLR 原生标记校验：" + target);
                    return;
            }
        }

        public static bool StreamContainsMarker(Stream stream, string marker)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (string.IsNullOrEmpty(marker)) throw new ArgumentException("Marker cannot be empty.", nameof(marker));

            byte[] pattern = Encoding.ASCII.GetBytes(marker);
            byte[] buffer = new byte[64 * 1024];
            int matched = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int index = 0; index < read; index++)
                {
                    byte value = buffer[index];
                    while (matched > 0 && value != pattern[matched])
                        matched = PrefixLength(pattern, matched - 1);
                    if (value == pattern[matched])
                        matched++;
                    if (matched == pattern.Length)
                        return true;
                }
            }
            return false;
        }

        private static void ValidateWindowsPlayer(string playerLocation)
        {
            string directory = Directory.Exists(playerLocation)
                ? Path.GetFullPath(playerLocation)
                : Path.GetDirectoryName(Path.GetFullPath(playerLocation));
            string nativeLibrary = Path.Combine(directory ?? string.Empty, "GameAssembly.dll");
            if (!File.Exists(nativeLibrary))
                throw new BuildFailedException("Windows Player 缺少 GameAssembly.dll：" + nativeLibrary);

            using FileStream stream = File.OpenRead(nativeLibrary);
            if (!StreamContainsMarker(stream, InterpreterMarker))
                throw MissingInterpreterMarker(nativeLibrary);
            Debug.Log("[QHYFramework] HybridCLR 原生运行时校验通过：" + nativeLibrary);
        }

        private static void ValidateAndroidPlayer(string playerLocation)
        {
            if (!File.Exists(playerLocation))
                throw new BuildFailedException("Android Player 产物不存在：" + playerLocation);

            using FileStream file = File.OpenRead(playerLocation);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, false);
            ZipArchiveEntry[] libraries = archive.Entries
                .Where(entry => entry.FullName.EndsWith("/libil2cpp.so", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (libraries.Length == 0)
                throw new BuildFailedException("Android APK/AAB 中未找到 libil2cpp.so：" + playerLocation);

            foreach (ZipArchiveEntry library in libraries)
            {
                using Stream stream = library.Open();
                if (!StreamContainsMarker(stream, InterpreterMarker))
                    throw MissingInterpreterMarker(playerLocation + "!/" + library.FullName);
            }
            Debug.Log($"[QHYFramework] HybridCLR 原生运行时校验通过：{playerLocation}（{libraries.Length} ABI）");
        }

        private static BuildFailedException MissingInterpreterMarker(string path)
        {
            return new BuildFailedException(
                $"构建产物未包含 HybridCLR 解释器标记 '{InterpreterMarker}'：{path}。" +
                "Player 很可能错误使用了 Unity 全局 IL2CPP，已阻止发布。请重新执行完整构建。");
        }

        private static int PrefixLength(byte[] pattern, int end)
        {
            // The marker has no useful repeated prefix; this compact fallback remains correct for it.
            for (int length = end; length > 0; length--)
            {
                bool matches = true;
                for (int index = 0; index < length; index++)
                {
                    if (pattern[index] == pattern[end - length + 1 + index]) continue;
                    matches = false;
                    break;
                }
                if (matches) return length;
            }
            return 0;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static StringComparison PathComparison =>
            Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
    }
}
