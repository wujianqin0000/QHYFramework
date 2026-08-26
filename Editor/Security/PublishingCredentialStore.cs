using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace GameIntegration.Editor
{
    internal static class PublishingCredentialStore
    {
        private static readonly Dictionary<string, string> SessionPasswords =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> PersistentPasswordCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private const uint GenericCredential = 1;
        private const uint PersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;
        private const string CredentialPrefix = "QHYFramework.FTP.";

        internal static string GetPassword(string scope)
        {
            string environment = Environment.GetEnvironmentVariable("QHY_FTP_PASSWORD");
            if (!string.IsNullOrEmpty(environment)) return environment;
            scope = scope ?? string.Empty;
            if (SessionPasswords.TryGetValue(scope, out string password)) return password;
            password = ReadPersistent(scope);
            if (!string.IsNullOrEmpty(password))
            {
                SessionPasswords[scope] = password;
                PersistentPasswordCache[scope] = password;
            }
            return password;
        }

        internal static void SetPassword(string scope, string password, bool persist)
        {
            scope = scope ?? string.Empty;
            if (string.IsNullOrEmpty(password)) SessionPasswords.Remove(scope);
            else SessionPasswords[scope] = password;

            if (!persist || string.IsNullOrEmpty(password))
            {
                DeletePersistent(scope);
                return;
            }
            if (PersistentPasswordCache.TryGetValue(scope, out string cached) &&
                string.Equals(cached, password, StringComparison.Ordinal))
                return;
            WritePersistent(scope, password);
            PersistentPasswordCache[scope] = password;
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

        internal static void DeletePersistent(string scope)
        {
            scope = scope ?? string.Empty;
            PersistentPasswordCache.Remove(scope);
            if (!IsWindows) return;
            if (!CredDelete(GetTarget(scope), GenericCredential, 0))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != ErrorNotFound)
                    throw new InvalidOperationException("Failed to delete the saved FTP credential. Windows error: " +
                                                        error);
            }
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


        private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        private static string GetTarget(string scope)
        {
            return CredentialPrefix + scope;
        }

        private static string ReadPersistent(string scope)
        {
            if (!IsWindows) return string.Empty;
            if (!CredRead(GetTarget(scope), GenericCredential, 0, out IntPtr pointer))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound) return string.Empty;
                throw new InvalidOperationException("Failed to read the saved FTP credential. Windows error: " +
                                                    error);
            }
            try
            {
                NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                    return string.Empty;
                byte[] bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                CredFree(pointer);
            }
        }

        private static void WritePersistent(string scope, string password)
        {
            if (!IsWindows) return;
            byte[] bytes = Encoding.Unicode.GetBytes(password);
            IntPtr blob = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new NativeCredential
                {
                    Type = GenericCredential,
                    TargetName = GetTarget(scope),
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = PersistLocalMachine,
                    UserName = "QHYFramework FTP"
                };
                if (!CredWrite(ref credential, 0))
                    throw new InvalidOperationException(
                        "Failed to save the FTP credential in Windows Credential Manager. Windows error: " +
                        Marshal.GetLastWin32Error());
            }
            finally
            {
                Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
                Marshal.FreeCoTaskMem(blob);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr credential);
    }
}
