using System;
using UnityEditor;

namespace GameIntegration.Editor
{
    public enum ReleaseMode
    {
        FullPackage,
        HotUpdateOnly
    }

    [Serializable]
    public sealed class ReleaseOptions
    {
        public string channel = "default";
        public string packageVersion = "";
        public ReleaseMode mode = ReleaseMode.FullPackage;
        public BuildTarget target = BuildTarget.StandaloneWindows64;
        public bool developmentBuild;
        public string outputRoot = "Releases";
    }

    [Serializable]
    public sealed class ReleaseReportData
    {
        public string channel;
        public string version;
        public string platform;
        public string mode;
        public string unityVersion;
        public string clientVersion;
        public string remoteBaseUrl;
        public string clientManifestUrl;
        public string timestampUtc;
        public string aotBaselineSha256;
        public string[] aotMetadataAssemblies;
        public string[] artifacts;
    }
}
