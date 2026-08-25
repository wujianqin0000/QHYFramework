using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using YooAsset.Editor;

namespace GameIntegration.Editor
{
    internal static class BundleSizeAnalyzer
    {
        internal static void Analyze(BuildReport report, QHYFrameworkSettings settings)
        {
            long warning = Math.Max(0, settings.bundleWarningThresholdMiB) * 1024L * 1024L;
            long error = Math.Max(0, settings.bundleErrorThresholdMiB) * 1024L * 1024L;
            var oversized = report.BundleInfos.Where(bundle => warning > 0 && bundle.FileSize >= warning)
                .OrderByDescending(bundle => bundle.FileSize).ToArray();
            foreach (ReportBundleInfo bundle in oversized)
            {
                string[] largest = bundle.BundleContents.Select(asset => new
                    {
                        asset.AssetPath,
                        Size = File.Exists(asset.AssetPath) ? new FileInfo(asset.AssetPath).Length : 0L
                    }).OrderByDescending(item => item.Size).Take(10)
                    .Select(item => $"  - {item.AssetPath} ({GamePackageRuntime.FormatBytes(item.Size)})")
                    .ToArray();
                var message = new StringBuilder()
                    .AppendLine($"[QHYFramework] Oversized bundle: {bundle.BundleName}")
                    .AppendLine($"Size: {GamePackageRuntime.FormatBytes(bundle.FileSize)}")
                    .AppendLine("Main assets:")
                    .AppendLine(string.Join("\n", bundle.BundleContents.Select(item => "  - " + item.AssetPath)))
                    .AppendLine("Dependencies: " + string.Join(", ", bundle.DependBundles))
                    .AppendLine("Referenced by: " + string.Join(", ", bundle.ReferenceBundles))
                    .AppendLine("Largest source assets:")
                    .AppendLine(string.Join("\n", largest))
                    .Append("Recommendation: split by panel, atlas, feature, or update correlation.")
                    .ToString();
                if (error > 0 && bundle.FileSize >= error)
                    Debug.LogError(message);
                else
                    Debug.LogWarning(message);
            }

            if (error > 0 && oversized.Any(bundle => bundle.FileSize >= error))
                throw new InvalidOperationException(
                    $"One or more bundles exceed the configured error threshold ({settings.bundleErrorThresholdMiB} MiB). See Console diagnostics.");
        }
    }
}
