using System;
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
            if (string.IsNullOrWhiteSpace(options.resourceVersion))
                options.resourceVersion = Next(options.clientVersion, highest);
            options.resourceVersion = options.resourceVersion.Trim();
            Validate(options.clientVersion, options.resourceVersion);
            if (TryGetRevision(highest, options.clientVersion, out int previousRevision) &&
                TryGetRevision(options.resourceVersion, options.clientVersion, out int currentRevision) &&
                currentRevision <= previousRevision)
                throw new InvalidOperationException(
                    $"Resource version '{options.resourceVersion}' is not newer than the highest published revision '{highest}'.");
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
    }
}
