using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;

namespace GameIntegration.Editor
{
    internal static class WindowsUpdaterBuilder
    {
        private const string SourcePath =
            IntegrationProjectPaths.PackageRoot + "/Editor/WindowsUpdater/WindowsUpdaterProgram.cs.txt";

        public static string BuildAndCopy(string clientRoot)
        {
            string outputRoot = Path.GetFullPath(Path.Combine("Library", "GameIntegration", "WindowsUpdater"));
            Directory.CreateDirectory(outputRoot);
            string output = Path.Combine(outputRoot, "GameIntegration.Updater.exe");
            string csc = FindWindowsFrameworkCompiler();
            string source = Path.GetFullPath(SourcePath);
            if (!File.Exists(source)) throw new FileNotFoundException("找不到 Windows 更新器源码。", source);

            var info = new ProcessStartInfo(csc)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = $"/nologo /target:winexe /platform:anycpu /optimize+ /out:\"{output}\" " +
                            $"/r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll \"{source}\""
            };
            using Process process = Process.Start(info);
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 || !File.Exists(output))
                throw new InvalidOperationException("Windows 更新器编译失败：\n" + stdout + "\n" + stderr);
            VerifyUpdaterCanStart(output);

            string destination = Path.Combine(clientRoot, "GameIntegration.Updater.exe");
            File.Copy(output, destination, true);
            File.WriteAllText(Path.Combine(clientRoot, ".gameintegration-client"), "1");
            return destination;
        }

        private static string FindWindowsFrameworkCompiler()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string framework64 = Path.Combine(windows, "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
            if (File.Exists(framework64)) return framework64;
            string framework = Path.Combine(windows, "Microsoft.NET", "Framework", "v4.0.30319", "csc.exe");
            if (File.Exists(framework)) return framework;
            throw new FileNotFoundException(
                "找不到 Windows .NET Framework 4.x C# 编译器。请安装或修复 .NET Framework 4.8。",
                framework64);
        }

        private static void VerifyUpdaterCanStart(string updater)
        {
            var info = new ProcessStartInfo(updater, "--self-test")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using Process process = Process.Start(info);
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "Windows 更新器原生运行自检失败：\n" + stdout + "\n" + stderr);
        }
    }
}
