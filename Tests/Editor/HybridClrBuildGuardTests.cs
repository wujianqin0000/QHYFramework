using System.IO;
using System.Text;
using GameIntegration.Editor;
using NUnit.Framework;

namespace GameIntegration.Tests.Editor
{
    public sealed class HybridClrBuildGuardTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "QHY-HybridCLR-Guard-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "libil2cpp", "hybridclr"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void CurrentHybridClrLayout_IsAccepted()
        {
            CreateFile("build/deploy/il2cpp.exe");
            Assert.IsTrue(HybridClrBuildGuard.IsLocalIl2CppLayoutValid(_root, out string reason), reason);
        }

        [Test]
        public void UnityIl2CppDllLayout_IsAccepted()
        {
            CreateFile("build/deploy/net471/Unity.IL2CPP.dll");
            Assert.IsTrue(HybridClrBuildGuard.IsLocalIl2CppLayoutValid(_root, out string reason), reason);
        }

        [Test]
        public void DoubledIl2CppBinWithoutRealCompiler_IsRejected()
        {
            Directory.CreateDirectory(Path.Combine(_root, "il2cpp", "bin"));
            Assert.IsFalse(HybridClrBuildGuard.IsLocalIl2CppLayoutValid(_root, out string reason));
            StringAssert.Contains("编译器", reason);
        }

        [Test]
        public void MarkerScanner_FindsMarkerAcrossBufferBoundary()
        {
            byte[] prefix = new byte[64 * 1024 - 5];
            byte[] marker = Encoding.ASCII.GetBytes(HybridClrBuildGuard.InterpreterMarker);
            using var stream = new MemoryStream();
            stream.Write(prefix, 0, prefix.Length);
            stream.Write(marker, 0, marker.Length);
            stream.Position = 0;
            Assert.IsTrue(HybridClrBuildGuard.StreamContainsMarker(stream,
                HybridClrBuildGuard.InterpreterMarker));
        }

        [Test]
        public void MarkerScanner_RejectsStockIl2CppBinary()
        {
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes("LoadMetadataForAOTAssembly only"));
            Assert.IsFalse(HybridClrBuildGuard.StreamContainsMarker(stream,
                HybridClrBuildGuard.InterpreterMarker));
        }

        private void CreateFile(string relative)
        {
            string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[] { 1 });
        }
    }
}
