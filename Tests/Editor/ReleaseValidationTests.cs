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
        public void DistributionRuntimeConfig_UsesIndependentPlatformRoots()
        {
            QHYFrameworkSettings settings = CreateIsolatedSettings();
            try
            {
                DistributionRuntimeConfig config = settings.CreateRuntimeConfig(
                    IntegrationPlatform.Windows, "v1.0.0");
                Assert.AreEqual("https://example.com/test-game/windows/cdn", config.cdnRoot);
                Assert.AreEqual("https://example.com/test-game/windows/origin", config.originRoot);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void DistributionRuntimeConfig_AcceptsDerivedRootsAndRejectsWrongFolders()
        {
            var config = new DistributionRuntimeConfig
            {
                gameDirectory = "test-game",
                platform = "android",
                clientVersion = "v1.0.0",
                cdnRoot = "https://example.com/test-game/android/cdn",
                originRoot = "https://example.com/test-game/android/origin"
            };
            Assert.DoesNotThrow(config.ValidateOrThrow);

            config.cdnRoot = "https://example.com/test-game/android/origin";
            Assert.Throws<System.InvalidOperationException>(config.ValidateOrThrow);
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
                Assert.AreEqual("https://example.com/test-game/windows/origin/current/client.json",
                    settings.CreateRuntimeConfig(IntegrationPlatform.Windows,
                        "v1.0.0").ClientManifestUrl);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void DistributionPathResolver_RoutesPointerManifestAndBundle()
        {
            QHYFrameworkSettings settings = CreateIsolatedSettings();
            try
            {
                var resolver = new DistributionPathResolver(settings.CreateRuntimeConfig(
                    IntegrationPlatform.Windows, "v1.0.0"));
                Assert.AreEqual("https://example.com/test-game/windows/origin/current/v1.0.0.version",
                    resolver.ResolveYooAssetUrl("GamePackage.version"));
                Assert.AreEqual("https://example.com/test-game/windows/cdn/versions/v1.0.0/v1.0.0-r0002.bytes",
                    resolver.ResolveYooAssetUrl("GamePackage_v1.0.0-r0002.bytes"));
                Assert.AreEqual("https://example.com/test-game/windows/cdn/bundles/abcdef0123456789.bundle",
                    resolver.ResolveYooAssetUrl("abcdef0123456789.bundle"));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void PathSegment_RejectsPathCharacters()
        {
            Assert.Throws<System.InvalidOperationException>(() =>
                DistributionPathResolver.ValidateSegment("../version", "Version", false));
            Assert.DoesNotThrow(() => DistributionPathResolver.ValidateSegment("v1.0.0-r0001",
                "Version", false));
        }

        [Test]
        public void GameDirectory_IsOneStrictLowercasePathSegment()
        {
            Assert.DoesNotThrow(() => DistributionPathResolver.ValidateSegment(
                "game-one_1", "GameDirectory", true));
            Assert.Throws<System.InvalidOperationException>(() =>
                DistributionPathResolver.ValidateSegment("GameOne", "GameDirectory", true));
            Assert.Throws<System.InvalidOperationException>(() =>
                DistributionPathResolver.ValidateSegment("games/one", "GameDirectory", true));
        }

        [Test]
        public void NewSettings_DoNotContainPublisherRemoteConfiguration()
        {
            var settings = ScriptableObject.CreateInstance<QHYFrameworkSettings>();
            try
            {
                Assert.IsNull(typeof(QHYFrameworkSettings).GetField("resourceBaseUrl"));
                Assert.IsNotNull(typeof(QHYFrameworkSettings).GetField("platformResourceProfiles"));
                Assert.IsNull(typeof(QHYFrameworkSettings).GetField("gameId"));
                Assert.IsNull(typeof(QHYFrameworkSettings).GetField("platformProfiles"));
                Assert.IsNull(typeof(QHYFrameworkSettings).GetField("ftpHost"));
                Assert.IsNull(typeof(QHYFrameworkSettings).GetField("ftpPort"));
                Assert.IsNull(typeof(QHYFrameworkSettings).GetField("ftpUserName"));
                Assert.IsNull(typeof(QHYFrameworkSettings).GetField("ftpPassword"));
                Assert.IsNull(typeof(GameIntegration.Editor.FtpUploadOptions).GetField("RemoteRoot"));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void PlatformProfiles_RejectDuplicatesAndUnknownButAllowEveryConcretePlatform()
        {
            QHYFrameworkSettings settings = CreateIsolatedSettings();
            try
            {
                Assert.DoesNotThrow(() => settings.ValidatePlatformResourceProfilesOrThrow());
                settings.platformResourceProfiles = settings.platformResourceProfiles
                    .Concat(new[] { new PlatformResourceProfile
                        { platform = IntegrationPlatform.Android, baseUrl = "https://duplicate.example/android" } })
                    .ToArray();
                Assert.Throws<System.InvalidOperationException>(() =>
                    settings.ValidatePlatformResourceProfilesOrThrow());
                settings.platformResourceProfiles = new[]
                {
                    new PlatformResourceProfile
                        { platform = IntegrationPlatform.Android, baseUrl = "https://example.com" },
                    new PlatformResourceProfile
                        { platform = IntegrationPlatform.IOS, baseUrl = "https://example.com" }
                };
                Assert.DoesNotThrow(() => settings.ValidatePlatformResourceProfilesOrThrow());
                Assert.AreEqual("https://example.com/test-game/ios/cdn",
                    settings.CreateRuntimeConfig(IntegrationPlatform.IOS, "v1.0.0").cdnRoot);
                Assert.Throws<System.InvalidOperationException>(() =>
                    settings.CreateRuntimeConfig(IntegrationPlatform.Windows, "v1.0.0"));
                settings.platformResourceProfiles = settings.platformResourceProfiles.Concat(new[]
                {
                    new PlatformResourceProfile
                        { platform = IntegrationPlatform.Unknown, baseUrl = "https://example.com/unknown" }
                }).ToArray();
                Assert.Throws<System.InvalidOperationException>(() =>
                    settings.ValidatePlatformResourceProfilesOrThrow());
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void ResourceBaseUrl_NormalizesCommonInputAndRejectsUnsafeRoots()
        {
            Assert.AreEqual("https://aco.ai20.top",
                QHYFrameworkSettings.NormalizeResourceBaseUrl("aco.ai20.top"));
            Assert.AreEqual("https://aco.ai20.top",
                QHYFrameworkSettings.NormalizeResourceBaseUrl(
                    "[https://aco.ai20.top](https://aco.ai20.top)"));
            Assert.AreEqual("https://aco.ai20.top",
                QHYFrameworkSettings.NormalizeResourceBaseUrl("<https://aco.ai20.top/>"));
            Assert.Throws<System.InvalidOperationException>(() =>
                QHYFrameworkSettings.NormalizeResourceBaseUrl("https://example.com?token=x"));
            Assert.Throws<System.InvalidOperationException>(() =>
                QHYFrameworkSettings.NormalizeResourceBaseUrl("https://user:pass@example.com"));
            Assert.Throws<System.InvalidOperationException>(() =>
                QHYFrameworkSettings.NormalizeResourceBaseUrl("https://example.com/android/cdn"));
            Assert.Throws<System.InvalidOperationException>(() =>
                QHYFrameworkSettings.NormalizeResourceBaseUrl("https://example.com/android"));
            Assert.Throws<System.InvalidOperationException>(() =>
                QHYFrameworkSettings.NormalizeResourceBaseUrl("ftp://example.com"));
            Assert.AreEqual("https://example.com",
                QHYFrameworkSettings.NormalizeResourceBaseUrl(" https://example.com/ "));
        }

        [Test]
        public void PlatformProfiles_TargetValidationDoesNotBlockOnInactiveUrl()
        {
            QHYFrameworkSettings settings = CreateIsolatedSettings();
            try
            {
                settings.GetPlatformResourceProfile(IntegrationPlatform.Android).baseUrl =
                    "ftp://invalid.example";
                Assert.DoesNotThrow(() => settings.ValidatePlatformResourceProfilesOrThrow(
                    IntegrationPlatform.Windows));
                Assert.Throws<System.InvalidOperationException>(() =>
                    settings.ValidatePlatformResourceProfilesOrThrow());
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void ReleaseBuildTargetSwitcher_MapsSupportedGroupsAndRejectsNoTarget()
        {
            Assert.AreEqual(UnityEditor.BuildTargetGroup.Standalone,
                GameIntegration.Editor.ReleaseBuildTargetSwitcher.GetTargetGroupOrThrow(
                    UnityEditor.BuildTarget.StandaloneWindows64));
            Assert.AreEqual(UnityEditor.BuildTargetGroup.Android,
                GameIntegration.Editor.ReleaseBuildTargetSwitcher.GetTargetGroupOrThrow(
                    UnityEditor.BuildTarget.Android));
            Assert.Throws<System.InvalidOperationException>(() =>
                GameIntegration.Editor.ReleaseBuildTargetSwitcher.GetTargetGroupOrThrow(
                    UnityEditor.BuildTarget.NoTarget));
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
            string outputRoot = Path.GetFullPath(Path.Combine("Temp", "QHYFramework.ResourceVersionTests",
                System.Guid.NewGuid().ToString("N")));
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                clientVersion = "v1.2.3",
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                outputRoot = outputRoot,
                buildRootOverride = Path.Combine(outputRoot, "QHYBuilds"),
                mode = GameIntegration.Editor.ReleaseMode.HotUpdateOnly,
                automaticResourceVersion = true
            };
            string localRevision = Path.Combine(outputRoot, "QHYBuilds", "windows",
                "v1.2.3", "v1.2.3-r0004");
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
        public void ExternalAnimationAssets_RequireFullAnimationModulePreservation()
        {
            Assert.IsTrue(GameIntegration.Editor.EngineStrippingLinkerAutomation.IsAnimationAsset(
                "Assets/Game/Content/Characters/Hero.anim", typeof(AnimationClip)));
            Assert.IsTrue(GameIntegration.Editor.EngineStrippingLinkerAutomation.IsAnimationAsset(
                "Assets/Game/Content/Characters/Hero.controller", typeof(RuntimeAnimatorController)));
            Assert.IsTrue(GameIntegration.Editor.EngineStrippingLinkerAutomation.IsAnimationAsset(
                "Assets/Game/Content/Characters/Hero.fbx", typeof(GameObject), true));
            Assert.IsFalse(GameIntegration.Editor.EngineStrippingLinkerAutomation.IsAnimationAsset(
                "Assets/Game/Content/UI/Icon.png", typeof(Texture2D)));

            string xml = GameIntegration.Editor.EngineStrippingLinkerAutomation.BuildLinkXml(
                new[] { GameIntegration.Editor.EngineStrippingLinkerAutomation.AnimationModule });
            Assert.IsTrue(GameIntegration.Editor.EngineStrippingLinkerAutomation.PreservesAssembly(xml,
                GameIntegration.Editor.EngineStrippingLinkerAutomation.AnimationModule));
            StringAssert.DoesNotContain("HybridCLRGenerate", xml);
        }

        [Test]
        public void EngineStrippingValidation_BlocksMissingAnimationProtectionOnlyWhenEnabled()
        {
            var analysis = new GameIntegration.Editor.EngineStrippingAnalysis
            {
                AnimationAssetPaths = new[] { "Assets/Game/Content/Characters/Hero.controller" },
                RequiredAssemblies = new[]
                {
                    GameIntegration.Editor.EngineStrippingLinkerAutomation.AnimationModule
                }
            };
            Assert.IsEmpty(GameIntegration.Editor.EngineStrippingLinkerAutomation.ValidateProtection(
                analysis, false, string.Empty));
            var errors = GameIntegration.Editor.EngineStrippingLinkerAutomation.ValidateProtection(
                analysis, true, "<linker />");
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("UnityEngine.AnimationModule", errors[0]);

            string valid = GameIntegration.Editor.EngineStrippingLinkerAutomation.BuildLinkXml(
                analysis.RequiredAssemblies);
            Assert.IsEmpty(GameIntegration.Editor.EngineStrippingLinkerAutomation.ValidateProtection(
                analysis, true, valid));
            Assert.AreEqual("Assets/Game/Generated/QHYLink/link.xml",
                GameIntegration.Editor.IntegrationProjectPaths.GeneratedQhyLinkXml);
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
            string outputRoot = Path.GetFullPath(Path.Combine("Temp", "QHYFramework.PublishedReleaseTests",
                System.Guid.NewGuid().ToString("N")));
            string releaseRoot = Path.Combine(outputRoot, "QHYBuilds", "windows",
                "v1.2.3", "v1.2.3-r0004");
            Directory.CreateDirectory(releaseRoot);
            var plan = new GameIntegration.Editor.ReleaseUploadPlan
            {
                platform = "windows",
                clientVersion = "v1.2.3",
                resourceVersion = "v1.2.3-r0004",
                releasesRoot = Path.Combine(outputRoot, "Releases"),
                stateRoot = Path.Combine(outputRoot, "State")
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
        public void ReleaseRoot_UsesPrivateQhyBuildsHierarchy()
        {
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                outputRoot = "Releases",
                buildRootOverride = "QHYBuilds",
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                clientVersion = "v1.2.3",
                resourceVersion = "v1.2.3-r0004"
            };
            string expected = Path.GetFullPath(Path.Combine("QHYBuilds",
                "windows", "v1.2.3", "v1.2.3-r0004"));
            Assert.AreEqual(expected, GameIntegration.Editor.ReleasePipeline.GetReleaseRoot(options));
        }

        [Test]
        public void ContentAddressedBundlePath_IsSharedAndFlat()
        {
            var first = new GameIntegration.Editor.ReleaseOptions { outputRoot = "Releases",
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                clientVersion = "v1.2.3", resourceVersion = "v1.2.3-r0001" };
            var second = new GameIntegration.Editor.ReleaseOptions { outputRoot = "Releases",
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                clientVersion = "v1.2.3", resourceVersion = "v1.2.3-r0002" };
            const string bundle = "abcdef0123456789.bundle";
            string left = GameIntegration.Editor.DistributionReleaseLayout.BundlePath(first, bundle);
            string right = GameIntegration.Editor.DistributionReleaseLayout.BundlePath(second, bundle);
            Assert.AreEqual(left, right);
            StringAssert.Contains(Path.Combine("bundles", bundle), left);
        }

        [Test]
        public void SchemaV5_WrapsGameAndSeparatesCdnAndOriginLayout()
        {
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                outputRoot = "Releases",
                target = UnityEditor.BuildTarget.Android,
                clientVersion = "v1.2.3",
                resourceVersion = "v1.2.3-r0004"
            };
            StringAssert.EndsWith(Path.Combine("Releases", "game", "android", "cdn", "bundles", "abcdef.bundle"),
                GameIntegration.Editor.DistributionReleaseLayout.BundlePath(options, "abcdef.bundle"));
            StringAssert.EndsWith(Path.Combine("Releases", "game", "android", "cdn", "versions", "v1.2.3",
                    "v1.2.3-r0004.bytes"),
                GameIntegration.Editor.DistributionReleaseLayout.VersionPath(options, ".bytes"));
            StringAssert.EndsWith(Path.Combine("Releases", "game", "android", "cdn", "clients", "v1.2.3.apk"),
                GameIntegration.Editor.DistributionReleaseLayout.ClientPath(options, ".apk"));
            StringAssert.EndsWith(Path.Combine("Releases", "game", "android", "origin", "current", "v1.2.3.version"),
                GameIntegration.Editor.DistributionReleaseLayout.CurrentVersionPath(options));
            StringAssert.EndsWith(Path.Combine("Releases", "game", "android", "origin", "qhy.json"),
                GameIntegration.Editor.DistributionReleaseLayout.IndexPath(options));
        }

        [Test]
        public void SchemaV5_DifferentGamesDoNotShareServerMirrorPaths()
        {
            var first = new GameIntegration.Editor.ReleaseOptions
            {
                outputRoot = "Releases", gameDirectory = "game-one",
                target = UnityEditor.BuildTarget.Android
            };
            var second = new GameIntegration.Editor.ReleaseOptions
            {
                outputRoot = "Releases", gameDirectory = "game-two",
                target = UnityEditor.BuildTarget.Android
            };
            Assert.AreNotEqual(
                GameIntegration.Editor.DistributionReleaseLayout.BundlePath(first, "abcdef.bundle"),
                GameIntegration.Editor.DistributionReleaseLayout.BundlePath(second, "abcdef.bundle"));
            StringAssert.Contains(Path.Combine("game-one", "android"),
                GameIntegration.Editor.DistributionReleaseLayout.PlatformRoot(first));
            StringAssert.Contains(Path.Combine("game-two", "android"),
                GameIntegration.Editor.DistributionReleaseLayout.PlatformRoot(second));
        }

        [Test]
        public void ManualIncrementalUpload_SeparatesFilesAndPublishPointers()
        {
            string root = Path.GetFullPath(Path.Combine("Temp", "QHYFramework.UploadDeltaTests",
                System.Guid.NewGuid().ToString("N")));
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                outputRoot = Path.Combine(root, "Releases"),
                buildRootOverride = Path.Combine(root, "QHYBuilds"),
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                clientVersion = "v1.0.0",
                resourceVersion = "v1.0.0-r0001"
            };
            string bundle = GameIntegration.Editor.DistributionReleaseLayout.BundlePath(options,
                "abcdef.bundle");
            string pointer = GameIntegration.Editor.DistributionReleaseLayout.CurrentVersionPath(options);
            Directory.CreateDirectory(Path.GetDirectoryName(bundle));
            Directory.CreateDirectory(Path.GetDirectoryName(pointer));
            File.WriteAllText(bundle, "bundle");
            File.WriteAllText(pointer, "v1.0.0-r0001");
            var plan = new GameIntegration.Editor.ReleaseUploadPlan
            {
                releasesRoot = GameIntegration.Editor.DistributionReleaseLayout.ReleasesRoot(options),
                added = new[]
                {
                    new GameIntegration.Editor.UploadArtifact
                    {
                        relativePath = "game/windows/cdn/bundles/abcdef.bundle",
                        resourcePath = "cdn/bundles/abcdef.bundle"
                    },
                    new GameIntegration.Editor.UploadArtifact
                    {
                        relativePath = "game/windows/origin/current/v1.0.0.version",
                        resourcePath = "origin/current/v1.0.0.version",
                        pointer = true
                    }
                }
            };
            try
            {
                GameIntegration.Editor.IncrementalUploadMaterializer.Synchronize(options, plan);
                string payload = Path.Combine(GameIntegration.Editor.DistributionReleaseLayout.UploadFilesRoot(options),
                    "game", "windows", "cdn", "bundles", "abcdef.bundle");
                string activation = Path.Combine(GameIntegration.Editor.DistributionReleaseLayout.UploadPublishRoot(options),
                    "game", "windows", "origin", "current", "v1.0.0.version");
                Assert.IsTrue(File.Exists(payload));
                Assert.IsTrue(File.Exists(activation));
                File.WriteAllText(pointer, "v1.0.0-r0002");
                Assert.AreEqual("v1.0.0-r0001", File.ReadAllText(activation));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void ManualUploadFolderOpen_RequiresExactExistingDirectory()
        {
            string root = Path.GetFullPath(Path.Combine("Temp", "QHYFramework.UploadOpenTests",
                System.Guid.NewGuid().ToString("N")));
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                buildRootOverride = root,
                target = UnityEditor.BuildTarget.Android,
                clientVersion = "v1.0.0",
                resourceVersion = "v1.0.0-r0001"
            };
            try
            {
                Assert.IsFalse(GameIntegration.Editor.DistributionReleaseLayout
                    .TryGetExistingUploadRoot(options, false, out string missing));
                StringAssert.EndsWith(Path.Combine("Upload", "1-Files"), missing);
                Directory.CreateDirectory(missing);
                Assert.IsTrue(GameIntegration.Editor.DistributionReleaseLayout
                    .TryGetExistingUploadRoot(options, false, out string existing));
                Assert.AreEqual(Path.GetFullPath(missing), existing);
                Assert.IsFalse(GameIntegration.Editor.DistributionReleaseLayout
                    .TryGetExistingUploadRoot(options, true, out _));

                options.resourceVersion = string.Empty;
                Assert.IsFalse(GameIntegration.Editor.DistributionReleaseLayout
                    .TryGetExistingUploadRoot(options, false, out string invalid));
                Assert.IsEmpty(invalid);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void VersionControlIgnoreManager_PreservesUserRulesAndIsIdempotent()
        {
            string root = Path.GetFullPath(Path.Combine("Temp", "QHYFramework.IgnoreTests",
                System.Guid.NewGuid().ToString("N")));
            string path = Path.Combine(root, "ignore.conf");
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "Library\n# user rule\nCustomCache\n");
            string[] rules = { "/Releases", "/releases", "/QHYBuilds", "/qhybuilds" };
            try
            {
                Assert.IsTrue(GameIntegration.Editor.VersionControlIgnoreManager
                    .EnsureManagedBlock(path, rules));
                string first = File.ReadAllText(path);
                StringAssert.Contains("Library", first);
                StringAssert.Contains("CustomCache", first);
                StringAssert.Contains(GameIntegration.Editor.VersionControlIgnoreManager.BeginMarker, first);
                StringAssert.Contains("/Releases", first);
                Assert.IsFalse(GameIntegration.Editor.VersionControlIgnoreManager
                    .EnsureManagedBlock(path, rules));
                Assert.AreEqual(first, File.ReadAllText(path));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void VersionControlIgnoreManager_RejectsMalformedManagedBlock()
        {
            string root = Path.GetFullPath(Path.Combine("Temp", "QHYFramework.IgnoreMalformedTests",
                System.Guid.NewGuid().ToString("N")));
            string path = Path.Combine(root, "ignore.conf");
            Directory.CreateDirectory(root);
            File.WriteAllText(path, GameIntegration.Editor.VersionControlIgnoreManager.BeginMarker +
                                    "\n/Releases\n");
            try
            {
                Assert.Throws<System.IO.InvalidDataException>(() =>
                    GameIntegration.Editor.VersionControlIgnoreManager.EnsureManagedBlock(path,
                        new[] { "/Releases", "/QHYBuilds" }));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void SchemaV5_PreflightRejectsLegacyLayoutWithoutDeletingIt()
        {
            string output = Path.GetFullPath(Path.Combine("Temp", "QHYFramework.SchemaV5Tests",
                System.Guid.NewGuid().ToString("N")));
            string legacy = Path.Combine(output, "Server");
            Directory.CreateDirectory(legacy);
            var options = new GameIntegration.Editor.ReleaseOptions { outputRoot = output,
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                clientVersion = "v1.0.0", resourceVersion = "v1.0.0-r0001",
                mode = GameIntegration.Editor.ReleaseMode.FullPackage };
            try
            {
                Assert.Throws<System.InvalidOperationException>(() =>
                    GameIntegration.Editor.DistributionReleaseLayout.ValidateCleanOrV5(options));
                Assert.IsTrue(Directory.Exists(legacy));
            }
            finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
        }

        [Test]
        public void SchemaV5_PreflightRejectsRootLevelPlatformLayout()
        {
            string output = Path.GetFullPath(Path.Combine("Temp", "QHYFramework.SchemaV5PlatformTests",
                System.Guid.NewGuid().ToString("N")));
            string legacy = Path.Combine(output, "android");
            Directory.CreateDirectory(legacy);
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                outputRoot = output,
                gameDirectory = "my-game",
                target = UnityEditor.BuildTarget.Android,
                clientVersion = "v1.0.0",
                resourceVersion = "v1.0.0-r0001",
                mode = GameIntegration.Editor.ReleaseMode.FullPackage
            };
            try
            {
                Assert.Throws<System.InvalidOperationException>(() =>
                    GameIntegration.Editor.DistributionReleaseLayout.ValidateCleanOrV5(options));
                Assert.IsTrue(Directory.Exists(legacy));
            }
            finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
        }

        [Test]
        public void SchemaV5_PreflightRejectsOldIndex()
        {
            string root = Path.GetFullPath(Path.Combine("Temp", "QHYFramework.SchemaV5IndexTests",
                System.Guid.NewGuid().ToString("N")));
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                outputRoot = root,
                stateRootOverride = Path.Combine(root, "State"),
                target = UnityEditor.BuildTarget.Android,
                clientVersion = "v1.0.0",
                resourceVersion = "v1.0.0-r0001",
                mode = GameIntegration.Editor.ReleaseMode.FullPackage
            };
            string index = GameIntegration.Editor.DistributionReleaseLayout.IndexPath(options);
            Directory.CreateDirectory(Path.GetDirectoryName(index));
            File.WriteAllText(index, "{\"schemaVersion\":3}");
            try
            {
                Assert.Throws<System.InvalidOperationException>(() =>
                    GameIntegration.Editor.DistributionReleaseLayout.ValidateCleanOrV5(options));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void SchemaV5_PreflightRejectsOldPublicationState()
        {
            string root = Path.GetFullPath(Path.Combine("Temp", "QHYFramework.SchemaV5StateTests",
                System.Guid.NewGuid().ToString("N")));
            string state = Path.Combine(root, "State");
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "publication-state.json"), "{\"schemaVersion\":3}");
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                outputRoot = Path.Combine(root, "Releases"),
                stateRootOverride = state,
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                clientVersion = "v1.0.0",
                resourceVersion = "v1.0.0-r0001",
                mode = GameIntegration.Editor.ReleaseMode.FullPackage
            };
            try
            {
                Assert.Throws<System.InvalidOperationException>(() =>
                    GameIntegration.Editor.DistributionReleaseLayout.ValidateCleanOrV5(options));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void RollbackHistory_AllowsAnyPublishedRevisionAndUndo()
        {
            string outputRoot = Path.GetFullPath(Path.Combine("Temp", "QHYFramework.RollbackHistoryTests",
                System.Guid.NewGuid().ToString("N")));
            var options = new GameIntegration.Editor.ReleaseOptions
            {
                outputRoot = outputRoot,
                stateRootOverride = Path.Combine(outputRoot, "State"),
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                clientVersion = "v1.2.3"
            };
            try
            {
                GameIntegration.Editor.ReleaseUploadPlan[] plans = Enumerable.Range(1, 4).Select(revision =>
                    new GameIntegration.Editor.ReleaseUploadPlan
                    {
                        platform = "windows",
                        clientVersion = "v1.2.3",
                        resourceVersion = $"v1.2.3-r{revision:0000}",
                        releasesRoot = Path.Combine(outputRoot, "Releases"),
                        stateRoot = Path.Combine(outputRoot, "State")
                    }).ToArray();
                string[] roots = plans.Select(plan => Path.Combine(outputRoot, "QHYBuilds",
                    "windows", "v1.2.3", plan.resourceVersion)).ToArray();
                for (int index = 0; index < plans.Length; index++)
                {
                    Directory.CreateDirectory(roots[index]);
                    File.WriteAllText(Path.Combine(roots[index], "publish-plan.json"),
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
            settings.gameDirectory = "test-game";
            settings.platformResourceProfiles = new[]
            {
                new PlatformResourceProfile
                    { platform = IntegrationPlatform.Android, baseUrl = "https://example.com" },
                new PlatformResourceProfile
                    { platform = IntegrationPlatform.Windows, baseUrl = "https://example.com" }
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
