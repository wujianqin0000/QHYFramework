using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GameIntegration.Tests.Editor
{
    public sealed class ReleaseValidationTests
    {
        [Test]
        public void ProjectConfiguration_IsValidBeforeGeneratedFiles()
        {
            var settings = GameIntegration.Editor.IntegrationProjectPreparer.LoadSettings();
            GameIntegration.Editor.IntegrationProjectPreparer.ConfigureBuildSettings();
            GameIntegration.Editor.IntegrationProjectPreparer.ConfigureCollectors(settings);
            var errors = GameIntegration.Editor.ReleaseValidation.Validate(settings, false);
            Assert.IsEmpty(errors, string.Join("\n", errors.ToArray()));
        }

        [Test]
        public void RemotePackageUrl_AppendsPlayerVersionToPlatformBaseUrl()
        {
            QHYFrameworkSettings settings = CreateIsolatedSettings();
            try
            {
                string url = settings.GetRemotePackageUrl(IntegrationPlatform.Windows, "v1.0.0");
                Assert.AreEqual("https://example.com/CDN/PC/v1.0.0", url);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ClientVersion_IsStrictAndComparable()
        {
            Assert.IsTrue(ClientVersion.TryParse("v1.2.3", out ClientVersion version));
            Assert.AreEqual(1002003, version.AndroidVersionCode);
            Assert.Greater(ClientVersion.Parse("v2.0.0").CompareTo(version), 0);
            Assert.IsFalse(ClientVersion.TryParse("v1.2", out _));
            Assert.IsFalse(ClientVersion.TryParse("1.2.3", out _));
        }

        [Test]
        public void ClientManifestUrl_IsStableAndDoesNotContainVersion()
        {
            QHYFrameworkSettings settings = CreateIsolatedSettings();
            try
            {
                Assert.AreEqual("https://example.com/Client/PC/latest.json",
                    settings.GetClientManifestUrl(IntegrationPlatform.Windows));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void NewSettings_DoNotContainPublisherRemoteConfiguration()
        {
            var settings = ScriptableObject.CreateInstance<QHYFrameworkSettings>();
            try
            {
                foreach (IntegrationPlatform platform in System.Enum.GetValues(typeof(IntegrationPlatform)))
                {
                    Assert.IsEmpty(settings.GetRemoteBaseUrl(platform));
                    Assert.IsEmpty(settings.GetClientUpdateBaseUrl(platform));
                    Assert.IsEmpty(settings.GetRemotePackageUrl(platform, "v1.0.0"));
                }
                Assert.IsEmpty(settings.ftpHost);
                Assert.AreEqual(21, settings.ftpPort);
                Assert.IsEmpty(settings.ftpUserName);
                Assert.IsEmpty(settings.ftpPassword);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        private static QHYFrameworkSettings CreateIsolatedSettings()
        {
            var settings = ScriptableObject.CreateInstance<QHYFrameworkSettings>();
            settings.platformProfiles = new[]
            {
                new PlatformRemoteProfile
                {
                    platform = IntegrationPlatform.Windows,
                    remoteBaseUrl = "https://example.com/CDN/PC",
                    clientUpdateBaseUrl = "https://example.com/Client/PC"
                }
            };
            return settings;
        }

        [Test]
        public void EditorLocalization_SwitchesChineseAndEnglish()
        {
            GameIntegration.Editor.EditorToolLanguage previous =
                GameIntegration.Editor.EditorLocalization.Language;
            try
            {
                GameIntegration.Editor.EditorLocalization.Language =
                    GameIntegration.Editor.EditorToolLanguage.Chinese;
                Assert.AreEqual("中文", GameIntegration.Editor.EditorLocalization.Text("中文", "English"));

                GameIntegration.Editor.EditorLocalization.Language =
                    GameIntegration.Editor.EditorToolLanguage.English;
                Assert.AreEqual("English", GameIntegration.Editor.EditorLocalization.Text("中文", "English"));
            }
            finally
            {
                GameIntegration.Editor.EditorLocalization.Language = previous;
            }
        }
    }
}
