using NUnit.Framework;
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
            Assert.AreEqual(expected, _resolver.Resolve(input, "ignored_bundle", typeof(Object)));
        }
    }
}
