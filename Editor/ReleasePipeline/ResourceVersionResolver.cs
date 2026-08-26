using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace GameIntegration.Editor
{
    internal static class ResourceVersionResolver
    {
        private static readonly Regex RevisionPattern = new Regex(
            @"^(?<client>v\d+\.\d+\.\d+)-r(?<revision>\d{4,})$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        internal static void Resolve(ReleaseOptions options, string publishedClientVersion,
            string previousResourceVersion, string highestResourceVersion = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.mode == ReleaseMode.HotUpdateOnly)
            {
                if (string.IsNullOrWhiteSpace(publishedClientVersion))
                    throw new InvalidOperationException("HotUpdateOnly requires a published FullPackage client baseline.");
                options.clientVersion = publishedClientVersion.Trim();
            }
            else if (string.IsNullOrWhiteSpace(options.clientVersion))
            {
                options.clientVersion = PlayerSettings.bundleVersion?.Trim();
            }

            if (!ClientVersion.TryParse(options.clientVersion, out _))
                throw new FormatException($"Invalid client version '{options.clientVersion}'. Expected vMAJOR.MINOR.PATCH.");

            string highest = string.IsNullOrWhiteSpace(highestResourceVersion)
                ? previousResourceVersion
                : highestResourceVersion;
            bool automatic = options.automaticResourceVersion;
            if (automatic)
            {
                highest = SelectHigher(options.clientVersion, highest,
                    FindHighestLocalRelease(options));
                options.resourceVersion = Next(options.clientVersion, highest);
            }
            options.resourceVersion = (options.resourceVersion ?? string.Empty).Trim();
            Validate(options.clientVersion, options.resourceVersion);
            if (TryGetRevision(highest, options.clientVersion, out int previousRevision) &&
                TryGetRevision(options.resourceVersion, options.clientVersion, out int currentRevision) &&
                currentRevision <= previousRevision)
                throw new InvalidOperationException(
                    $"Resource version '{options.resourceVersion}' is not newer than the highest published revision '{highest}'.");
        }

        internal static string SuggestNext(ReleaseOptions options)
        {
            if (options == null || !ClientVersion.TryParse(options.clientVersion, out _))
                return string.Empty;
            ResourceReleaseBaseline baseline;
            try
            {
                baseline = ReleaseBaselineStore.Load(options);
            }
            catch
            {
                baseline = null;
            }
            string highest = baseline?.highestResourceVersion ?? baseline?.resourceVersion;
            highest = SelectHigher(options.clientVersion, highest, FindHighestLocalRelease(options));
            return Next(options.clientVersion, highest);
        }

        internal static string Next(string clientVersion, string previousResourceVersion)
        {
            int revision = 0;
            Match previous = RevisionPattern.Match(previousResourceVersion ?? string.Empty);
            if (previous.Success && string.Equals(previous.Groups["client"].Value, clientVersion,
                    StringComparison.OrdinalIgnoreCase))
                int.TryParse(previous.Groups["revision"].Value, out revision);
            return $"{clientVersion}-r{revision + 1:0000}";
        }

        internal static void Validate(string clientVersion, string resourceVersion)
        {
            Match match = RevisionPattern.Match(resourceVersion ?? string.Empty);
            if (!match.Success || !string.Equals(match.Groups["client"].Value, clientVersion,
                    StringComparison.OrdinalIgnoreCase))
                throw new FormatException(
                    $"Resource version '{resourceVersion}' must use '{clientVersion}-rNNNN'.");
        }

        internal static bool TryGetRevision(string resourceVersion, string clientVersion, out int revision)
        {
            revision = 0;
            Match match = RevisionPattern.Match(resourceVersion ?? string.Empty);
            return match.Success && string.Equals(match.Groups["client"].Value, clientVersion,
                       StringComparison.OrdinalIgnoreCase) &&
                   int.TryParse(match.Groups["revision"].Value, out revision);
        }

        private static string FindHighestLocalRelease(ReleaseOptions options)
        {
            string root;
            try
            {
                root = Path.GetFullPath(Path.Combine(options.outputRoot, options.channel,
                    ReleasePipeline.GetPlatformName(options.target), options.clientVersion));
                if (!Directory.Exists(root)) return string.Empty;
            }
            catch
            {
                return string.Empty;
            }

            string highest = string.Empty;
            try
            {
                foreach (string directory in Directory.GetDirectories(root))
                    highest = SelectHigher(options.clientVersion, highest,
                        Path.GetFileName(directory));
                string revisions = Path.Combine(root, "Revisions");
                if (Directory.Exists(revisions))
                    foreach (string directory in Directory.GetDirectories(revisions))
                        highest = SelectHigher(options.clientVersion, highest,
                            Path.GetFileName(directory));
            }
            catch
            {
                return highest;
            }
            return highest;
        }

        private static string SelectHigher(string clientVersion, string left, string right)
        {
            bool hasLeft = TryGetRevision(left, clientVersion, out int leftRevision);
            bool hasRight = TryGetRevision(right, clientVersion, out int rightRevision);
            if (!hasLeft) return hasRight ? right : string.Empty;
            if (!hasRight) return left;
            return rightRevision > leftRevision ? right : left;
        }
    }
}
