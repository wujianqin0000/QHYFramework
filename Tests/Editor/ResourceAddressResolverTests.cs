using System;
using System.IO;
using System.Linq;
using GameIntegration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameIntegration.Tests.Editor
{
    public sealed class ResourceAddressResolverTests
    {
        private readonly ResourceAddressResolver _resolver = new ResourceAddressResolver();

        [TestCase("Hero", "Hero")]
        [TestCase("Resources/UI/Login.prefab", "UI/Login")]
        [TestCase("Audio\\Click.wav", "Audio/Click")]
        [TestCase("Game.HotUpdate.dll", "Game.HotUpdate")]
        public void Resolve_NormalizesLegacyNames(string input, string expected)
        {
            Assert.AreEqual(expected, _resolver.Resolve(input, "ignored_bundle", typeof(UnityEngine.Object)));
        }
    }

    public sealed class GeneratedAssemblyCatalogTests
    {
        private string _root;
        private string _hotUpdateDirectory;
        private string _aotDirectory;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "QHYGeneratedAssemblyCatalogTests", Guid.NewGuid().ToString("N"));
            _hotUpdateDirectory = Path.Combine(_root, "HotUpdate");
            _aotDirectory = Path.Combine(_root, "AOT");
            Directory.CreateDirectory(_hotUpdateDirectory);
            Directory.CreateDirectory(_aotDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void Scan_HotUpdateOnlyIncludesConfiguredGeneratedAssemblies()
        {
            File.WriteAllBytes(Path.Combine(_hotUpdateDirectory, "Game.HotUpdate.dll"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(_hotUpdateDirectory, "Dependency.dll"), Array.Empty<byte>());

            GeneratedAssemblySnapshot result = GeneratedAssemblyCatalog.Scan(BuildTarget.StandaloneWindows64,
                _hotUpdateDirectory, _aotDirectory, new[] { "Game.HotUpdate", "NotGenerated" });

            CollectionAssert.AreEqual(new[] { "Game.HotUpdate" }, result.HotUpdateAssemblies);
        }

        [Test]
        public void Scan_AotIncludesGeneratedDllsAndSortsCaseInsensitively()
        {
            File.WriteAllBytes(Path.Combine(_aotDirectory, "System.dll"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(_aotDirectory, "mscorlib.dll"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(_aotDirectory, "readme.txt"), Array.Empty<byte>());

            GeneratedAssemblySnapshot result = GeneratedAssemblyCatalog.Scan(BuildTarget.Android,
                _hotUpdateDirectory, _aotDirectory, Array.Empty<string>());

            CollectionAssert.AreEqual(new[] { "mscorlib.dll", "System.dll" }, result.AotAssemblies);
        }

        [TestCase("Game.HotUpdate", "Game.HotUpdate.dll")]
        [TestCase(" Game.HotUpdate.DLL ", "Game.HotUpdate.dll")]
        [TestCase("folder/Game.HotUpdate.dll", "Game.HotUpdate.dll")]
        public void NormalizeDllFileName_ReturnsCanonicalFileName(string input, string expected)
        {
            Assert.AreEqual(expected, GeneratedAssemblyCatalog.NormalizeDllFileName(input));
        }

        [Test]
        public void Scan_MissingDirectoriesReturnsEmptyLists()
        {
            GeneratedAssemblySnapshot result = GeneratedAssemblyCatalog.Scan(BuildTarget.Android,
                Path.Combine(_root, "MissingHotUpdate"), Path.Combine(_root, "MissingAot"),
                new[] { "Game.HotUpdate" });

            Assert.IsEmpty(result.HotUpdateAssemblies);
            Assert.IsEmpty(result.AotAssemblies);
        }
    }

    public sealed class AotMetadataAutomationTests
    {
        private string _root;
        private string _source;
        private string _stripped;
        private QHYFrameworkSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "QHYAotMetadataAutomationTests", Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_root, "AOTGenericReferences.cs");
            _stripped = Path.Combine(_root, "Stripped");
            Directory.CreateDirectory(_stripped);
            _settings = ScriptableObject.CreateInstance<QHYFrameworkSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings)
                UnityEngine.Object.DestroyImmediate(_settings);
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void Inspect_UsesPatchedListAndMergesExtras()
        {
            WriteReferences("UIKit.dll", "mscorlib.dll", "QFramework.CoreKit.dll");
            foreach (string name in new[] { "UIKit.dll", "mscorlib.dll", "QFramework.CoreKit.dll", "System.dll" })
                File.WriteAllBytes(Path.Combine(_stripped, name), Array.Empty<byte>());
            _settings.aotMetadataExtraAssemblyNames = new[] { "System", "uikit.dll" };

            AotMetadataAnalysis result = AotMetadataAutomation.Inspect(_settings, BuildTarget.Android,
                _source, _stripped);

            Assert.IsTrue(result.Succeeded, result.Error);
            CollectionAssert.AreEqual(new[] { "mscorlib.dll", "QFramework.CoreKit.dll", "UIKit.dll" },
                result.AutomaticAssemblies);
            CollectionAssert.AreEqual(
                new[] { "mscorlib.dll", "QFramework.CoreKit.dll", "System.dll", "UIKit.dll" },
                result.EffectiveAssemblies);
            Assert.IsEmpty(result.MissingAssemblies);
        }

        [Test]
        public void Inspect_ReportsMissingGeneratedAssembly()
        {
            WriteReferences("mscorlib.dll");

            AotMetadataAnalysis result = AotMetadataAutomation.Inspect(_settings,
                BuildTarget.StandaloneWindows64, _source, _stripped);

            CollectionAssert.AreEqual(new[] { "mscorlib.dll" }, result.MissingAssemblies);
        }

        [Test]
        public void ParseAutomaticAssemblies_RejectsDamagedGeneratedFile()
        {
            Assert.Throws<InvalidDataException>(() =>
                AotMetadataAutomation.ParseAutomaticAssemblies("public class AOTGenericReferences {}"));
        }

        [Test]
        public void Snapshot_RestoresFrozenAssemblyListInsteadOfCurrentAnalysis()
        {
            string generated = Path.Combine(_root, "Generated");
            string snapshot = Path.Combine(_root, "WindowsSnapshot");
            string restored = Path.Combine(_root, "Restored");
            Directory.CreateDirectory(generated);
            File.WriteAllBytes(Path.Combine(generated, "mscorlib.dll.bytes"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(generated, "UIKit.dll.bytes"), new byte[] { 4, 5, 6 });

            AotMetadataSnapshotStore.Save(BuildTarget.StandaloneWindows64, generated,
                new[] { "UIKit.dll", "mscorlib.dll" }, "analysis-hash", snapshot);
            string[] names = AotMetadataSnapshotStore.Restore(BuildTarget.StandaloneWindows64,
                restored, snapshot);

            CollectionAssert.AreEqual(new[] { "mscorlib.dll", "UIKit.dll" }, names);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 },
                File.ReadAllBytes(Path.Combine(restored, "mscorlib.dll.bytes")));
        }

        [Test]
        public void Snapshot_LegacyDirectoryRecoversNamesFromBytesFiles()
        {
            string snapshot = Path.Combine(_root, "LegacySnapshot");
            string restored = Path.Combine(_root, "LegacyRestored");
            Directory.CreateDirectory(snapshot);
            File.WriteAllBytes(Path.Combine(snapshot, "QFramework.CoreKit.dll.bytes"), new byte[] { 7 });

            string[] names = AotMetadataSnapshotStore.Restore(BuildTarget.Android, restored, snapshot);

            CollectionAssert.AreEqual(new[] { "QFramework.CoreKit.dll" }, names);
            Assert.IsTrue(File.Exists(Path.Combine(restored, "QFramework.CoreKit.dll.bytes")));
        }

        [Test]
        public void Snapshot_RejectsTamperedAotMetadataBySha256()
        {
            string generated = Path.Combine(_root, "GeneratedTamper");
            string snapshot = Path.Combine(_root, "TamperSnapshot");
            string restored = Path.Combine(_root, "TamperRestored");
            Directory.CreateDirectory(generated);
            File.WriteAllBytes(Path.Combine(generated, "mscorlib.dll.bytes"), new byte[] { 1, 2, 3 });
            AotMetadataSnapshotStore.Save(BuildTarget.StandaloneWindows64, generated,
                new[] { "mscorlib.dll" }, "analysis-hash", snapshot);
            File.WriteAllBytes(Path.Combine(snapshot, "mscorlib.dll.bytes"), new byte[] { 9, 9, 9 });

            Assert.Throws<InvalidDataException>(() => AotMetadataSnapshotStore.Restore(
                BuildTarget.StandaloneWindows64, restored, snapshot));
        }

        [Test]
        public void SnapshotPayload_MatchingRestoredFilesAreAccepted()
        {
            string generated = Path.Combine(_root, "PayloadGenerated");
            string snapshot = Path.Combine(_root, "PayloadSnapshot");
            string restored = Path.Combine(_root, "PayloadRestored");
            Directory.CreateDirectory(generated);
            File.WriteAllBytes(Path.Combine(generated, "mscorlib.dll.bytes"), new byte[] { 1, 2, 3 });
            AotMetadataSnapshotStore.Save(BuildTarget.Android, generated,
                new[] { "mscorlib.dll" }, "analysis-hash", snapshot);
            AotMetadataSnapshotStore.Restore(BuildTarget.Android, restored, snapshot);

            Assert.IsTrue(AotMetadataSnapshotStore.MatchesSnapshot(BuildTarget.Android,
                restored, snapshot, out string reason), reason);
        }

        [Test]
        public void SnapshotPayload_ChangedFileIsRejected()
        {
            string generated = Path.Combine(_root, "ChangedGenerated");
            string snapshot = Path.Combine(_root, "ChangedSnapshot");
            string restored = Path.Combine(_root, "ChangedRestored");
            Directory.CreateDirectory(generated);
            File.WriteAllBytes(Path.Combine(generated, "mscorlib.dll.bytes"), new byte[] { 1, 2, 3 });
            AotMetadataSnapshotStore.Save(BuildTarget.Android, generated,
                new[] { "mscorlib.dll" }, "analysis-hash", snapshot);
            AotMetadataSnapshotStore.Restore(BuildTarget.Android, restored, snapshot);
            File.WriteAllBytes(Path.Combine(restored, "mscorlib.dll.bytes"), new byte[] { 1, 2, 4 });

            Assert.IsFalse(AotMetadataSnapshotStore.MatchesSnapshot(BuildTarget.Android,
                restored, snapshot, out string reason));
            StringAssert.Contains("payload differs", reason);
        }

        [Test]
        public void SnapshotPayload_ExtraAssemblyIsRejected()
        {
            string generated = Path.Combine(_root, "ExtraGenerated");
            string snapshot = Path.Combine(_root, "ExtraSnapshot");
            string restored = Path.Combine(_root, "ExtraRestored");
            Directory.CreateDirectory(generated);
            File.WriteAllBytes(Path.Combine(generated, "mscorlib.dll.bytes"), new byte[] { 1 });
            AotMetadataSnapshotStore.Save(BuildTarget.Android, generated,
                new[] { "mscorlib.dll" }, "analysis-hash", snapshot);
            AotMetadataSnapshotStore.Restore(BuildTarget.Android, restored, snapshot);
            File.WriteAllBytes(Path.Combine(restored, "System.dll.bytes"), new byte[] { 2 });

            Assert.IsFalse(AotMetadataSnapshotStore.MatchesSnapshot(BuildTarget.Android,
                restored, snapshot, out string reason));
            StringAssert.Contains("assembly set differs", reason);
        }

        private void WriteReferences(params string[] names)
        {
            string entries = string.Join(Environment.NewLine, names.Select(name => $"\t\t\"{name}\","));
            File.WriteAllText(_source,
                "using System.Collections.Generic; public class AOTGenericReferences { " +
                "public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string> {" +
                entries + "}; }");
        }
    }
}
