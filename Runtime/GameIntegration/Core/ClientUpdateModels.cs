using System;

namespace GameIntegration
{
    [Serializable]
    public sealed class ClientUpdateManifest
    {
        public int SchemaVersion = 1;
        public string Platform = string.Empty;
        public string Version = string.Empty;
        public int AndroidVersionCode;
        public string PackageUrl = string.Empty;
        public string PackageType = string.Empty;
        public string FileName = string.Empty;
        public long SizeBytes;
        public string Sha256 = string.Empty;
        public string EntryExecutable = string.Empty;
        public string PublishedAtUtc = string.Empty;
        public bool Mandatory = true;
    }

    public readonly struct ClientVersion : IComparable<ClientVersion>
    {
        public readonly int Major;
        public readonly int Minor;
        public readonly int Patch;

        public ClientVersion(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int AndroidVersionCode => checked(Major * 1000000 + Minor * 1000 + Patch);

        public int CompareTo(ClientVersion other)
        {
            int result = Major.CompareTo(other.Major);
            if (result != 0) return result;
            result = Minor.CompareTo(other.Minor);
            return result != 0 ? result : Patch.CompareTo(other.Patch);
        }

        public override string ToString() => $"v{Major}.{Minor}.{Patch}";

        public static bool TryParse(string value, out ClientVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string text = value.Trim();
            if (text.Length < 2 || (text[0] != 'v' && text[0] != 'V')) return false;
            string[] parts = text.Substring(1).Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int major) ||
                !int.TryParse(parts[1], out int minor) || !int.TryParse(parts[2], out int patch) ||
                major < 0 || minor < 0 || minor > 999 || patch < 0 || patch > 999 || major > 2099)
                return false;
            version = new ClientVersion(major, minor, patch);
            return true;
        }

        public static ClientVersion Parse(string value)
        {
            if (!TryParse(value, out ClientVersion version))
                throw new FormatException($"版本必须使用 v主版本.次版本.修订版本 格式：{value}");
            return version;
        }
    }
}
