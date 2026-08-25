using System;
using System.IO;
using UnityEditor;
using YooAsset.Editor;

namespace GameIntegration.Editor
{
    /// <summary>
    /// Keeps root assets independent and groups nested assets by their first directory below the collector.
    /// This is a conservative default based on update correlation rather than one bundle per whole directory.
    /// </summary>
    public sealed class QHYPackByTopDirectoryOrFile : IBundlePackRule
    {
        public BundlePackRuleResult GetPackRuleResult(BundlePackRuleData data)
        {
            string collectPath = data.CollectPath.Replace('\\', '/').TrimEnd('/');
            string assetPath = data.AssetPath.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(collectPath))
                return new BundlePackRuleResult(Path.ChangeExtension(assetPath, null),
                    DefaultBundlePackRule.AssetBundleFileExtension);

            string prefix = collectPath + "/";
            if (!assetPath.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Asset '{assetPath}' is outside collector '{collectPath}'.");

            string relative = assetPath.Substring(prefix.Length);
            int separator = relative.IndexOf('/');
            string bundleName = separator < 0
                ? Path.ChangeExtension(assetPath, null)
                : collectPath + "/" + relative.Substring(0, separator);
            return new BundlePackRuleResult(bundleName, DefaultBundlePackRule.AssetBundleFileExtension);
        }
    }
}
