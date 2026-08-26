using System.IO;
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
                Assert.IsNull(typeof(QHYFrameworkSettings).GetField("ftpHost"));
                Assert.IsNull(typeof(QHYFrameworkSettings).GetField("ftpPort"));
                Assert.IsNull(typeof(QHYFrameworkSettings).GetField("ftpUserName"));
                Assert.IsNull(typeof(QHYFrameworkSettings).GetField("ftpPassword"));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ResourceVersion_IsIndependentAndMonotonic()
        {
            Assert.AreEqual("v1.2.3-r0001",
                GameIntegration.Editor.ResourceVersionResolver.Next("v1.2.3", string.Empty));
            Assert.AreEqual("v1.2.3-r0008",
                GameIntegration.Editor.ResourceVersionResolver.Next("v1.2.3", "v1.2.3-r0007"));
            Assert.Throws<System.FormatException>(() =>
                GameIntegration.Editor.ResourceVersionResolver.Validate("v1.2.3", "v1.2.4-r0001"));
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                clientVersion = "v1.2.3",
                resourceVersion = "v1.2.3-r0007",
                mode = GameIntegration.Editor.ReleaseMode.FullPackage
            };
            Assert.Throws<System.InvalidOperationException>(() =>
                GameIntegration.Editor.ResourceVersionResolver.Resolve(options, string.Empty,
                    "v1.2.3-r0005", "v1.2.3-r0007"));

            options.resourceVersion = string.Empty;
            options.automaticResourceVersion = false;
            Assert.Throws<System.FormatException>(() =>
                GameIntegration.Editor.ResourceVersionResolver.Resolve(options, string.Empty,
                    "v1.2.3-r0005", "v1.2.3-r0007"));
        }

        [Test]
        public void AutomaticResourceVersion_SkipsPublishedAndLocalRevisions()
        {
            string outputRoot = Path.Combine(Path.GetTempPath(), "QHYFramework.ResourceVersionTests",
                System.Guid.NewGuid().ToString("N"));
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                channel = "default",
                clientVersion = "v1.2.3",
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                outputRoot = outputRoot,
                mode = GameIntegration.Editor.ReleaseMode.HotUpdateOnly,
                automaticResourceVersion = true
            };
            string localRevision = Path.Combine(outputRoot, "default", "Windows64", "v1.2.3",
                "Revisions", "v1.2.3-r0004");
            Directory.CreateDirectory(localRevision);
            try
            {
                GameIntegration.Editor.ResourceVersionResolver.Resolve(options, "v1.2.3",
                    "v1.2.3-r0002", "v1.2.3-r0003");
                Assert.AreEqual("v1.2.3-r0005", options.resourceVersion);
            }
            finally
            {
                Directory.Delete(outputRoot, true);
            }
        }

        [Test]
        public void CollectorManagement_DefaultsToInitializeOnly()
        {
            var settings = ScriptableObject.CreateInstance<QHYFrameworkSettings>();
            try
            {
                Assert.AreEqual(CollectorManagementMode.InitializeOnly, settings.collectorManagementMode);
                Assert.AreEqual(4, settings.bundleWarningThresholdMiB);
                Assert.AreEqual(16, settings.bundleErrorThresholdMiB);
                Assert.IsFalse(settings.ignoreTypeTreeChangesForIncrementalBuild);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AotForcePublishAuthorization_IsExplicitAndAuditedOnlyInReport()
        {
            var options = new GameIntegration.Editor.ReleaseOptions();
            Assert.IsNull(options.confirmForceAotMetadataPublish);

            options.confirmForceAotMetadataPublish = _ => true;
            Assert.IsTrue(options.confirmForceAotMetadataPublish("changed hash"));
            StringAssert.DoesNotContain("confirmForceAotMetadataPublish", JsonUtility.ToJson(options));

            var report = new GameIntegration.Editor.ReleaseReportData
            {
                aotMetadataChanged = true,
                aotMetadataPayloadMatched = false,
                aotMetadataForcePublished = true,
                aotMetadataMismatchReason = "changed hash"
            };
            string reportJson = JsonUtility.ToJson(report);
            StringAssert.Contains("\"aotMetadataForcePublished\":true", reportJson);
            StringAssert.Contains("\"aotMetadataMismatchReason\":\"changed hash\"", reportJson);
        }

        [Test]
        public void EmptyHotUpdatePlan_IsNotPublishable()
        {
            var empty = new GameIntegration.Editor.ReleaseUploadPlan
            {
                added = new[]
                {
                    new GameIntegration.Editor.UploadArtifact
                    {
                        relativePath = "GamePackage.version",
                        contentAddressedBundle = false
                    }
                }
            };
            Assert.IsFalse(GameIntegration.Editor.ReleaseContentChangeDetector.HasChanges(empty));

            empty.changed = new[]
            {
                new GameIntegration.Editor.UploadArtifact
                {
                    relativePath = "content.bundle",
                    contentAddressedBundle = true
                }
            };
            Assert.IsTrue(GameIntegration.Editor.ReleaseContentChangeDetector.HasChanges(empty));

            empty.changed = System.Array.Empty<GameIntegration.Editor.UploadArtifact>();
            empty.removed = new[]
            {
                new GameIntegration.Editor.UploadArtifact { relativePath = "removed.bundle" }
            };
            Assert.IsTrue(GameIntegration.Editor.ReleaseContentChangeDetector.HasChanges(empty));
        }

        [Test]
        public void SuccessfullyPublishedResourceVersion_CannotBeUploadedTwice()
        {
            string outputRoot = Path.Combine(Path.GetTempPath(), "QHYFramework.PublishedReleaseTests",
                System.Guid.NewGuid().ToString("N"));
            string releaseRoot = Path.Combine(outputRoot, "default", "Windows64", "v1.2.3",
                "Revisions", "v1.2.3-r0004");
            Directory.CreateDirectory(releaseRoot);
            var plan = new GameIntegration.Editor.ReleaseUploadPlan
            {
                channel = "default",
                platform = "Windows64",
                clientVersion = "v1.2.3",
                resourceVersion = "v1.2.3-r0004"
            };
            try
            {
                Assert.IsFalse(GameIntegration.Editor.ReleaseBaselineStore.IsAlreadyPublished(
                    releaseRoot, plan));
                GameIntegration.Editor.ReleaseBaselineStore.SavePublished(releaseRoot, plan);
                Assert.IsTrue(GameIntegration.Editor.ReleaseBaselineStore.IsAlreadyPublished(
                    releaseRoot, plan));
            }
            finally
            {
                Directory.Delete(outputRoot, true);
            }
        }

        [Test]
        public void ReleaseRoot_ArchivesResourceRevisionsUnderDedicatedDirectory()
        {
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                outputRoot = "Releases",
                channel = "default",
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                clientVersion = "v1.2.3",
                resourceVersion = "v1.2.3-r0004"
            };
            string expected = Path.GetFullPath(Path.Combine("Releases", "default", "Windows64",
                "v1.2.3", "Revisions", "v1.2.3-r0004"));
            Assert.AreEqual(expected, GameIntegration.Editor.ReleasePipeline.GetReleaseRoot(options));
        }

        [Test]
        public void ReleaseSnapshots_ReuseSharedContentAddressedBundles()
        {
            string root = Path.Combine(Path.GetTempPath(), "QHYFramework.SharedBundleTests",
                System.Guid.NewGuid().ToString("N"));
            string source = Path.Combine(root, "Source");
            string clientRoot = Path.Combine(root, "ClientVersion");
            string firstCdn = Path.Combine(clientRoot, "Revisions", "v1.2.3-r0001", "CDN");
            string secondCdn = Path.Combine(clientRoot, "Revisions", "v1.2.3-r0002", "CDN");
            const string bundleName = "content.bundle";
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, bundleName), "immutable-content");
            try
            {
                GameIntegration.Editor.ReleaseSnapshotMaterializer.MaterializeFiles(source, firstCdn,
                    clientRoot, new[] { bundleName });
                GameIntegration.Editor.ReleaseSnapshotMaterializer.MaterializeFiles(source, secondCdn,
                    clientRoot, new[] { bundleName });

                string shared = Path.Combine(clientRoot, "SharedBundles", bundleName);
                Assert.IsTrue(File.Exists(shared));
                Assert.AreEqual("immutable-content", File.ReadAllText(Path.Combine(firstCdn, bundleName)));
                Assert.AreEqual("immutable-content", File.ReadAllText(Path.Combine(secondCdn, bundleName)));
                Assert.AreEqual(GameIntegration.Editor.BundleDeltaAnalyzer.ComputeSha256(shared),
                    GameIntegration.Editor.BundleDeltaAnalyzer.ComputeSha256(Path.Combine(secondCdn, bundleName)));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void RollbackHistory_AllowsAnyPublishedRevisionAndUndo()
        {
            string outputRoot = Path.Combine(Path.GetTempPath(), "QHYFramework.RollbackHistoryTests",
                System.Guid.NewGuid().ToString("N"));
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                outputRoot = outputRoot,
                channel = "default",
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                clientVersion = "v1.2.3"
            };
            try
            {
                GameIntegration.Editor.ReleaseUploadPlan[] plans = Enumerable.Range(1, 4).Select(revision =>
                    new GameIntegration.Editor.ReleaseUploadPlan
                    {
                        channel = "default",
                        platform = "Windows64",
                        clientVersion = "v1.2.3",
                        resourceVersion = $"v1.2.3-r{revision:0000}"
                    }).ToArray();
                string[] roots = plans.Select(plan => Path.Combine(outputRoot, "default", "Windows64",
                    "v1.2.3", "Revisions", plan.resourceVersion)).ToArray();
                for (int index = 0; index < plans.Length; index++)
                {
                    string cdn = Path.Combine(roots[index], "CDN");
                    Directory.CreateDirectory(cdn);
                    string manifestName = plans[index].resourceVersion + ".bytes";
                    string bundleName = $"bundle-{index + 1}.bundle";
                    File.WriteAllText(Path.Combine(cdn, "GamePackage.version"),
                        plans[index].resourceVersion);
                    File.WriteAllText(Path.Combine(cdn, manifestName), "manifest");
                    File.WriteAllText(Path.Combine(cdn, bundleName), "bundle");
                    plans[index].added = new[]
                    {
                        new GameIntegration.Editor.UploadArtifact
                        {
                            relativePath = manifestName,
                            contentAddressedBundle = false
                        },
                        new GameIntegration.Editor.UploadArtifact
                        {
                            relativePath = bundleName,
                            contentAddressedBundle = true
                        }
                    };
                    File.WriteAllText(Path.Combine(roots[index], "upload-plan.json"),
                        JsonUtility.ToJson(plans[index], true));
                }

                GameIntegration.Editor.ReleaseBaselineStore.SavePublished(roots[0], plans[0]);
                GameIntegration.Editor.ReleaseBaselineStore.SavePublished(roots[1], plans[1]);
                GameIntegration.Editor.ReleaseBaselineStore.SavePublished(roots[2], plans[2]);
                GameIntegration.Editor.ReleaseRollbackTarget[] initial =
                    GameIntegration.Editor.ReleaseBaselineStore.GetRollbackTargets(options);
                CollectionAssert.AreEqual(new[] { "v1.2.3-r0002", "v1.2.3-r0001" },
                    initial.Select(item => item.ResourceVersion).ToArray());

                GameIntegration.Editor.ReleaseBaselineStore.SaveRollback(roots[0], plans[0],
                    "v1.2.3-r0003");
                GameIntegration.Editor.ReleaseRollbackTarget[] afterRollback =
                    GameIntegration.Editor.ReleaseBaselineStore.GetRollbackTargets(options);
                CollectionAssert.AreEqual(new[] { "v1.2.3-r0003", "v1.2.3-r0002" },
                    afterRollback.Select(item => item.ResourceVersion).ToArray());
            }
            finally
            {
                if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, true);
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
