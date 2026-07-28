using System.Threading.Tasks;
using GameIntegration;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameIntegration.Tests.Editor
{
    /// <summary>
    /// Compile-time contracts for the public snippets in Documentation~.
    /// The test deliberately does not execute runtime loading in EditMode.
    /// </summary>
    public sealed class DocumentationExamples
    {
        private sealed class DocumentationPanel : UIPanel
        {
            protected override void OnClose()
            {
            }
        }

        [Test]
        public void PublicExamplesCompile()
        {
            ResLoader loader = ResLoader.Allocate();
            GameObject prefab = loader.LoadSync<GameObject>("Player");
            loader.Add2Load<GameObject>("Player", (success, resource) =>
            {
                GameObject loaded = success ? resource.Asset as GameObject : null;
                _ = loaded;
            });
            loader.LoadAsync();
            loader.Recycle2Cache();

            _ = prefab;
            _ = (System.Action)(() => UIKit.OpenPanel<DocumentationPanel>());
            _ = (System.Action)(() => AudioKit.PlaySound("ButtonClick"));
            _ = (System.Action)(() => AudioKit.PlayMusic("BgmLobby", true));
            _ = (System.Func<Task>)(async () =>
            {
                await YooAssetSceneKit.LoadSceneAsync("Main", LoadSceneMode.Single);
                await YooAssetSceneKit.UnloadSceneAsync("Main");
            });
        }
    }
}
