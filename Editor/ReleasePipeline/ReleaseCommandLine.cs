using System;
using UnityEditor;

namespace GameIntegration.Editor
{
    public static class ReleaseCommandLine
    {
        public static void Build()
        {
            string[] args = Environment.GetCommandLineArgs();
            string resourceVersion = Read(args, "-resourceVersion", string.Empty);
            var options = new ReleaseOptions
            {
                channel = Read(args, "-releaseChannel", "default"),
                clientVersion = Read(args, "-clientVersion",
                    Read(args, "-releaseVersion", PlayerSettings.bundleVersion)),
                resourceVersion = resourceVersion,
                automaticResourceVersion = string.IsNullOrWhiteSpace(resourceVersion),
                outputRoot = Read(args, "-releaseOutput", "Releases"),
                developmentBuild = bool.TryParse(Read(args, "-development", "false"), out bool development) && development
            };
            if (Enum.TryParse(Read(args, "-releaseMode", ReleaseMode.FullPackage.ToString()), true,
                    out ReleaseMode mode))
                options.mode = mode;
            if (Enum.TryParse(Read(args, "-buildTarget", EditorUserBuildSettings.activeBuildTarget.ToString()), true,
                    out BuildTarget target))
                options.target = target;
            ReleasePipeline.Run(options);
        }

        private static string Read(string[] args, string key, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return fallback;
        }
    }
}
