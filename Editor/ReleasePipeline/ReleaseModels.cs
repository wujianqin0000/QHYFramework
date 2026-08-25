using System;
using System.Collections.Generic;
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
        public string clientVersion = "";
        public string resourceVersion = "";
        public ReleaseMode mode = ReleaseMode.FullPackage;
        public BuildTarget target = BuildTarget.StandaloneWindows64;
        public bool developmentBuild;
        public string outputRoot = "Releases";

        [Obsolete("Use clientVersion and resourceVersion. packageVersion is retained for source compatibility only.")]
        public string packageVersion
        {
            get => string.IsNullOrWhiteSpace(resourceVersion) ? clientVersion : resourceVersion;
            set
            {
                resourceVersion = value;
                if (string.IsNullOrWhiteSpace(clientVersion))
                    clientVersion = value;
            }
        }
    }

    [Serializable]
    public sealed class UploadArtifact
    {
        public string relativePath;
        public string bundleName;
        public string sha256;
        public long length;
        public string changeType;
        public bool contentAddressedBundle;
    }

    [Serializable]
    public sealed class ReleaseUploadPlan
    {
        public string channel;
        public string platform;
        public string clientVersion;
        public string resourceVersion;
        public string previousResourceVersion;
        public UploadArtifact[] added = Array.Empty<UploadArtifact>();
        public UploadArtifact[] changed = Array.Empty<UploadArtifact>();
        public UploadArtifact[] unchanged = Array.Empty<UploadArtifact>();
        public UploadArtifact[] removed = Array.Empty<UploadArtifact>();
        public long snapshotBytes;
        public long uploadBytes;
        public long estimatedClientDownloadBytes;
    }

    [Serializable]
    public sealed class BundleDeltaRecord
    {
        public string bundleName;
        public string previousFileName;
        public string currentFileName;
        public long previousSize;
        public long currentSize;
        public string changeType;
        public string[] mainAssets = Array.Empty<string>();
        public string[] dependencyAssets = Array.Empty<string>();
        public string[] referencedBy = Array.Empty<string>();
        public bool suspectedTypeTreeChange;
    }

    [Serializable]
    public sealed class ReleaseReportData
    {
        public string channel;
        public string version;
        public string resourceVersion;
        public string previousResourceVersion;
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
        public long snapshotBytes;
        public long uploadBytes;
        public long estimatedClientDownloadBytes;
        public int bundleCount;
        public int addedBundleCount;
        public int changedBundleCount;
        public int unchangedBundleCount;
        public int removedBundleCount;
        public bool hotUpdateDllChanged;
        public bool aotMetadataChanged;
        public bool aotClientBaselineMatched;
        public BundleDeltaRecord[] addedBundles = Array.Empty<BundleDeltaRecord>();
        public BundleDeltaRecord[] changedBundles = Array.Empty<BundleDeltaRecord>();
        public BundleDeltaRecord[] unchangedBundles = Array.Empty<BundleDeltaRecord>();
        public BundleDeltaRecord[] removedBundles = Array.Empty<BundleDeltaRecord>();
    }
}
