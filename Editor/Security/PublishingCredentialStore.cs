using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GameIntegration.Editor
{
    internal static class PublishingCredentialStore
    {
        private static readonly Dictionary<string, string> SessionPasswords =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static string GetPassword(string scope)
        {
            string environment = Environment.GetEnvironmentVariable("QHY_FTP_PASSWORD");
            if (!string.IsNullOrEmpty(environment)) return environment;
            return SessionPasswords.TryGetValue(scope ?? string.Empty, out string password)
                ? password
                : string.Empty;
        }

        internal static void SetPassword(string scope, string password)
        {
            scope = scope ?? string.Empty;
            if (string.IsNullOrEmpty(password)) SessionPasswords.Remove(scope);
            else SessionPasswords[scope] = password;
        }

        internal static string GetUserName(string configured)
        {
            string environment = Environment.GetEnvironmentVariable("QHY_FTP_USERNAME");
            return string.IsNullOrWhiteSpace(environment) ? configured ?? string.Empty : environment;
        }

        internal static void Clear(string scope)
        {
            SessionPasswords.Remove(scope ?? string.Empty);
        }

        internal static void ThrowIfSecretPersisted(string secret)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 4) return;
            foreach (string root in new[] { "Assets", "ProjectSettings", "UserSettings" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                             .Where(IsTextCandidate))
                {
                    try
                    {
                        if (File.ReadAllText(file).Contains(secret))
                            throw new InvalidDataException(
                                $"Publishing credential residue detected in project file: {file}");
                    }
                    catch (DecoderFallbackException) { }
                    catch (IOException) { }
                }
            }
        }

        internal static string Redact(string message, params string[] secrets)
        {
            string result = message ?? string.Empty;
            foreach (string secret in secrets ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(secret)) result = result.Replace(secret, "***");
            return result;
        }

        private static bool IsTextCandidate(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".asset": case ".json": case ".yaml": case ".yml": case ".txt":
                case ".xml": case ".md": case ".cs": case ".asmdef": case ".meta":
                    return true;
                default: return false;
            }
        }
    }
}
