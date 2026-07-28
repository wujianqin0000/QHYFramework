using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using YooAsset;
using YooAsset.Editor;

namespace GameIntegration.Editor
{
    public static class IntegrationProjectPreparer
    {
        private const string SettingsPath = QHYFrameworkSettings.DefaultAssetPath;
        private const string HotUpdateOutput = IntegrationProjectPaths.GeneratedHotUpdate;
        private const string AotOutput = IntegrationProjectPaths.GeneratedAotMetadata;
        private const string MainScenePath = IntegrationProjectPaths.MainScene;
        private const string InitializationStatePath = "ProjectSettings/QHYFrameworkProjectState.json";
        private const string EmbeddedHybridClrPackagePath = "Packages/com.code-philosophy.hybridclr/package.json";
        private const string UIRootPath = IntegrationProjectPaths.PackageRoot +
                                          "/Runtime/QFramework/Toolkits/UIKit/Scripts/Resources/UIRoot.prefab";
        private static bool _automaticInitializationRunning;

        private sealed class HotUpdateScriptCompilationPendingException : Exception
        {
        }

        [InitializeOnLoadMethod]
        private static void ScheduleAutomaticInitialization()
        {
            EditorApplication.delayCall += TryAutomaticInitialization;
        }

        private static void TryAutomaticInitialization()
        {
            if (_automaticInitializationRunning || File.Exists(InitializationStatePath))
                return;
            if (!File.Exists(EmbeddedHybridClrPackagePath))
            {
                EditorApplication.delayCall += TryAutomaticInitialization;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += TryAutomaticInitialization;
                return;
            }

            _automaticInitializationRunning = true;
            try
            {
                PrepareProject();
                Directory.CreateDirectory(Path.GetDirectoryName(InitializationStatePath) ?? "ProjectSettings");
                File.WriteAllText(InitializationStatePath,
                    "{\n" +
                    "  \"schemaVersion\": 1,\n" +
                    $"  \"initializedAtUtc\": \"{DateTime.UtcNow:O}\"\n" +
                    "}\n");
                Debug.Log(L(
                    "[QHYFramework] QHY Framework 已自动完成项目初始化，无需手动执行 Prepare Project。",
                    "[QHYFramework] QHY Framework project initialization completed automatically; Prepare Project is not required."));
            }
            catch (HotUpdateScriptCompilationPendingException)
            {
                // Generating the default hot-update script causes a domain reload. This
                // is an expected first-import state, not an initialization failure.
                EditorApplication.delayCall += TryAutomaticInitialization;
            }
            catch (Exception exception)
            {
                Debug.LogError(F(
                    "[QHYFramework] 自动初始化失败：{0}\n修正错误并重新导入 com.wjq.qhy-framework 后会再次执行。",
                    "[QHYFramework] Automatic initialization failed: {0}\nFix the error and reimport com.wjq.qhy-framework to retry.",
                    exception.GetBaseException().Message));
            }
            finally
            {
                _automaticInitializationRunning = false;
            }
        }

        private static void PrepareProject()
        {
            EnsureFolders();
            AssetDatabase.Refresh();
            BootUIPrefabGenerator.EnsureExists();
            QHYFrameworkSettings settings = LoadSettings();
            ConfigureHybridCLR(settings);
            EnsureProjectScenes(settings);
            ConfigureBuildSettings();
            ConfigureCollectors(settings);
            BuildEditorSimulation(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(L("[QHYFramework] 项目准备完成，可以从 Boot 场景运行。",
                "[QHYFramework] Project setup completed. You can now run the Boot scene."));
        }

        public static QHYFrameworkSettings LoadSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<QHYFrameworkSettings>(SettingsPath);
            if (!settings)
            {
                Directory.CreateDirectory(IntegrationProjectPaths.ConfigRoot);
                settings = ScriptableObject.CreateInstance<QHYFrameworkSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
                Debug.Log(F("[QHYFramework] 已创建宿主项目配置：{0}",
                    "[QHYFramework] Created host project settings: {0}", SettingsPath));
            }
            return settings;
        }

        public static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(IntegrationProjectPaths.BootScene, true) };
        }

        public static void ConfigureCollectors(QHYFrameworkSettings settings)
        {
            BundleCollectorSettingData.ClearAll();
            BundleCollectorSetting collectorSettings = BundleCollectorSettingData.Setting;
            collectorSettings.UniqueBundleName = true;

            var package = new BundleCollectorPackage
            {
                PackageName = settings.packageName,
                PackageDesc = "QFramework + HybridCLR + YooAsset game content",
                EnableAddressable = true,
                SupportExtensionless = true,
                LocationToLower = false,
                IncludeAssetGUID = false,
                AutoCollectShaders = true,
                IgnoreRuleName = nameof(NormalIgnoreRule)
            };
            package.Groups.Add(CreateGroup("Common", "Common",
                CreateCollector(IntegrationProjectPaths.CommonContent, nameof(CollectAll)),
                CreateCollector(IntegrationProjectPaths.UIContent, nameof(CollectAll)),
                CreateCollector(IntegrationProjectPaths.AudioContent, nameof(CollectAll)),
                CreateCollector(UIRootPath, nameof(CollectAll))));
            package.Groups.Add(CreateGroup("Scene", "Scene",
                CreateCollector(IntegrationProjectPaths.SceneContent, nameof(CollectScene))));
            package.Groups.Add(CreateGroup("HotUpdate", "HotUpdate",
                CreateCollector(HotUpdateOutput, nameof(CollectAll))));
            package.Groups.Add(CreateGroup("AOTMetadata", "AOTMetadata",
                CreateCollector(AotOutput, nameof(CollectAll))));
            collectorSettings.Packages.Add(package);
            BundleCollectorSettingData.SaveFile();
        }

        public static void CompileAndCopyHotUpdateAssemblies(QHYFrameworkSettings settings, BuildTarget target,
            bool developmentBuild, bool refreshAotMetadata = true)
        {
            CompileDllCommand.CompileDll(target, developmentBuild);
            string sourceRoot = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            RecreateGeneratedDirectory(HotUpdateOutput);
            foreach (HotUpdateAssemblySpec spec in settings.hotUpdateAssemblies ?? Array.Empty<HotUpdateAssemblySpec>())
            {
                if (spec == null || string.IsNullOrWhiteSpace(spec.assemblyName))
                    continue;
                CopyAsBytes(Path.Combine(sourceRoot, spec.assemblyName + ".dll"),
                    Path.Combine(HotUpdateOutput, spec.assemblyName + ".dll.bytes"), true);
                CopyAsBytes(Path.Combine(sourceRoot, spec.assemblyName + ".pdb"),
                    Path.Combine(HotUpdateOutput, spec.assemblyName + ".pdb.bytes"), false);
            }

            // AOT 元数据必须与客户端内实际的 AOT 程序集匹配。
            // 纯热更新只刷新热更 DLL，保留 FullPackage 生成的元数据，避免本地 AOT 改动污染旧客户端热更包。
            if (!refreshAotMetadata)
            {
                RestorePlatformAotMetadata(settings, target);
                AssetDatabase.Refresh();
                return;
            }

            RecreateGeneratedDirectory(AotOutput);
            string aotRoot = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            foreach (string name in settings.aotMetadataAssemblyNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                string fileName = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll";
                CopyAsBytes(Path.Combine(aotRoot, fileName), Path.Combine(AotOutput, fileName + ".bytes"), true);
            }
            SavePlatformAotMetadata(settings, target);
            AssetDatabase.Refresh();
        }

        private static void SavePlatformAotMetadata(QHYFrameworkSettings settings, BuildTarget target)
        {
            string snapshotRoot = GetAotSnapshotRoot(target);
            if (Directory.Exists(snapshotRoot))
                Directory.Delete(snapshotRoot, true);
            Directory.CreateDirectory(snapshotRoot);
            foreach (string name in settings.aotMetadataAssemblyNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                string fileName = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll";
                CopyAsBytes(Path.Combine(AotOutput, fileName + ".bytes"),
                    Path.Combine(snapshotRoot, fileName + ".bytes"), true);
            }
        }

        private static void RestorePlatformAotMetadata(QHYFrameworkSettings settings, BuildTarget target)
        {
            string[] names = settings.aotMetadataAssemblyNames ?? Array.Empty<string>();
            if (!names.Any(name => !string.IsNullOrWhiteSpace(name)))
                return;

            string snapshotRoot = GetAotSnapshotRoot(target);
            if (!Directory.Exists(snapshotRoot))
            {
                throw new InvalidOperationException(F(
                    "{0} 尚未生成对应客户端的 AOT 元数据快照。请先为该平台执行一次完整客户端构建。",
                    "No client AOT metadata snapshot exists for {0}. Run Full Package Build for this platform first.",
                    target));
            }

            RecreateGeneratedDirectory(AotOutput);
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                string fileName = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll";
                CopyAsBytes(Path.Combine(snapshotRoot, fileName + ".bytes"),
                    Path.Combine(AotOutput, fileName + ".bytes"), true);
            }
        }

        private static string GetAotSnapshotRoot(BuildTarget target)
        {
            return Path.GetFullPath(Path.Combine("Library", "GameIntegration", "AOTMetadata", target.ToString()));
        }

        public static void BuildEditorSimulation(QHYFrameworkSettings settings)
        {
            SaveDirtyScenesOrThrow();
            ConfigureCollectors(settings);
            var buildParameters = new PackageBuildParameters(settings.packageName)
            {
                BuildPipelineName = EBuildPipeline.EditorSimulateBuildPipeline.ToString(),
                BuildBundleType = (int)EBundleType.VirtualAssetBundle
            };
            PackageBuildResult result = BundleSimulateBuilder.SimulateBuild(buildParameters);
            settings.editorSimulatePackageRoot = result.PackageRootDirectory;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        public static void SaveDirtyScenesOrThrow()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid() || !scene.isDirty)
                    continue;
                if (string.IsNullOrWhiteSpace(scene.path))
                {
                    throw new InvalidOperationException(F(
                        "检测到尚未保存到项目中的新场景“{0}”。请先保存或关闭该场景后再构建。",
                        "An untitled scene '{0}' has unsaved changes. Save or close it before building.",
                        string.IsNullOrWhiteSpace(scene.name) ? "Untitled" : scene.name));
                }
                if (!EditorSceneManager.SaveScene(scene))
                {
                    throw new IOException(F("构建前保存场景失败：{0}",
                        "Failed to save scene before building: {0}", scene.path));
                }
                Debug.Log(F("[QHYFramework] 构建前已自动保存场景：{0}",
                    "[QHYFramework] Saved scene before build: {0}", scene.path));
            }

            AssetDatabase.SaveAssets();
        }

        private static BundleCollectorGroup CreateGroup(string name, string tag, params BundleCollector[] collectors)
        {
            var group = new BundleCollectorGroup
            {
                GroupName = name,
                GroupDesc = name,
                AssetTags = tag,
                ActiveRuleName = nameof(EnableGroup)
            };
            group.Collectors.AddRange(collectors);
            return group;
        }

        private static BundleCollector CreateCollector(string path, string filterRule)
        {
            return new BundleCollector
            {
                CollectPath = path,
                CollectorGUID = AssetDatabase.AssetPathToGUID(path),
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = nameof(PackDirectory),
                FilterRuleName = filterRule,
                UserData = string.Empty,
                AssetTags = string.Empty
            };
        }

        private static void EnsureFolders()
        {
            string[] folders =
            {
                IntegrationProjectPaths.CommonContent, IntegrationProjectPaths.UIContent,
                IntegrationProjectPaths.AudioContent, IntegrationProjectPaths.SceneContent,
                HotUpdateOutput, AotOutput, IntegrationProjectPaths.ConfigRoot,
                IntegrationProjectPaths.BootRoot, IntegrationProjectPaths.HotUpdateScripts,
                Path.GetDirectoryName(IntegrationProjectPaths.BootScene)
            };
            foreach (string folder in folders)
                if (!string.IsNullOrWhiteSpace(folder))
                    Directory.CreateDirectory(folder);
            EnsureHotUpdateAssemblyDefinition();
            EnsureHotUpdateTestScript();
        }

        private static void EnsureHotUpdateAssemblyDefinition()
        {
            string path = IntegrationProjectPaths.HotUpdateScripts + "/Game.HotUpdate.asmdef";
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                if (!json.Contains("\"Unity.ugui\""))
                {
                    json = json.Replace("    \"YooAsset\"\n", "    \"YooAsset\",\n    \"Unity.ugui\"\n");
                    File.WriteAllText(path, json);
                }
                return;
            }
            File.WriteAllText(path,
                "{\n" +
                "  \"name\": \"Game.HotUpdate\",\n" +
                "  \"rootNamespace\": \"GameIntegration.HotUpdate\",\n" +
                "  \"references\": [\n" +
                "    \"GameIntegration.AOT\",\n" +
                "    \"QFramework\",\n" +
                "    \"QFramework.CoreKit\",\n" +
                "    \"ResKit\",\n" +
                "    \"UIKit\",\n" +
                "    \"AudioKit\",\n" +
                "    \"YooAsset\",\n" +
                "    \"Unity.ugui\"\n" +
                "  ],\n" +
                "  \"autoReferenced\": true\n" +
                "}\n");
        }

        private static void EnsureHotUpdateTestScript()
        {
            string markerPath = IntegrationProjectPaths.HotUpdateScripts + "/GameHotUpdateAssemblyMarker.cs";
            if (File.Exists(markerPath))
            {
                if (!AssetDatabase.DeleteAsset(markerPath))
                {
                    File.Delete(markerPath);
                    if (File.Exists(markerPath + ".meta"))
                        File.Delete(markerPath + ".meta");
                }
            }

            string path = IntegrationProjectPaths.HotUpdateScripts + "/HotUpdateEntry.cs";
            if (File.Exists(path))
                return;

            File.WriteAllText(path,
                "using UnityEngine;\n" +
                "using UnityEngine.UI;\n" +
                "\n" +
                "namespace GameIntegration.HotUpdate\n" +
                "{\n" +
                "    public sealed class HotUpdateEntry : MonoBehaviour\n" +
                "    {\n" +
                "        [SerializeField] private Text targetText;\n" +
                "\n" +
                "        private void Start()\n" +
                "        {\n" +
                "            if (!targetText)\n" +
                "                targetText = GetComponentInChildren<Text>(true);\n" +
                "            if (!targetText)\n" +
                "                targetText = GameObject.Find(\"HotUpdateStatusText\")?.GetComponent<Text>();\n" +
                "            if (!targetText)\n" +
                "            {\n" +
                "                Debug.LogError(\"[Game.HotUpdate] HotUpdateStatusText is missing.\");\n" +
                "                return;\n" +
                "            }\n" +
                "\n" +
                "            targetText.text = \"Game.HotUpdate code is running.\\nChange this text and publish HotUpdateOnly to verify hot update.\";\n" +
                "            Debug.Log(\"[Game.HotUpdate] Text changed by the hot-update assembly.\");\n" +
                "        }\n" +
                "    }\n" +
                "}\n");
        }

        private static void ConfigureHybridCLR(QHYFrameworkSettings settings)
        {
            HybridCLR.Editor.Settings.HybridCLRSettings hybridSettings = SettingsUtil.HybridCLRSettings;
            bool changed = false;
            bool localIl2CppInstalled = Directory.Exists(
                Path.Combine(SettingsUtil.LocalIl2CppDir, "libil2cpp", "hybridclr"));
            bool shouldUseGlobalIl2Cpp = !localIl2CppInstalled;
            if (hybridSettings.useGlobalIl2cpp != shouldUseGlobalIl2Cpp)
            {
                hybridSettings.useGlobalIl2cpp = shouldUseGlobalIl2Cpp;
                changed = true;
            }
            Environment.SetEnvironmentVariable("UNITY_IL2CPP_PATH",
                localIl2CppInstalled ? SettingsUtil.LocalIl2CppDir : string.Empty);

            var configuredNames = new HashSet<string>(
                SettingsUtil.HotUpdateAssemblyNamesExcludePreserved,
                StringComparer.OrdinalIgnoreCase);
            var assemblyNames = (hybridSettings.hotUpdateAssemblies ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            foreach (HotUpdateAssemblySpec spec in settings.hotUpdateAssemblies ?? Array.Empty<HotUpdateAssemblySpec>())
            {
                string assemblyName = spec?.assemblyName?.Trim();
                if (string.IsNullOrEmpty(assemblyName) || !configuredNames.Add(assemblyName))
                    continue;
                assemblyNames.Add(assemblyName);
                changed = true;
            }

            if (!changed)
                return;

            hybridSettings.hotUpdateAssemblies = assemblyNames.ToArray();
            HybridCLR.Editor.Settings.HybridCLRSettings.Save();
            Debug.Log("[QHYFramework] 已自动同步 HybridCLR 热更新程序集配置。");
        }

        private static void EnsureProjectScenes(QHYFrameworkSettings settings)
        {
            // The first AssetDatabase.Refresh after generating HotUpdateEntry schedules
            // a script compilation. Do not touch the user's currently open scene until
            // that component type is actually available; otherwise a domain reload can
            // leave a modified, untitled scene that blocks additive scene creation.
            ResolveHotUpdateEntryType();
            Scene previous = SceneManager.GetActiveScene();
            try
            {
                if (!SceneAssetExistsOrOpen(MainScenePath))
                {
                    Scene untitledScene = FindUntitledOpenScene(previous);
                    if (untitledScene.IsValid())
                        SaveUntitledSceneAsMain(untitledScene);
                    else
                        CreateHostScene(MainScenePath, "Main", null, null);
                }
                else
                {
                    EnsureMainSceneSetup(MainScenePath);
                }
                if (!SceneAssetExistsOrOpen(IntegrationProjectPaths.BootScene))
                {
                    BootUGUIView bootPrefab = AssetDatabase.LoadAssetAtPath<BootUGUIView>(
                        IntegrationProjectPaths.BootPrefab);
                    CreateHostScene(IntegrationProjectPaths.BootScene, "GameBootstrap", settings, bootPrefab);
                }
                else
                {
                    EnsureBootSceneCamera();
                }
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
            }
        }

        private static Scene FindUntitledOpenScene(Scene preferred)
        {
            if (preferred.IsValid() && preferred.isLoaded && string.IsNullOrEmpty(preferred.path))
                return preferred;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded && string.IsNullOrEmpty(scene.path))
                    return scene;
            }
            return default;
        }

        private static bool SceneAssetExistsOrOpen(string scenePath)
        {
            Scene openScene = FindOpenScene(scenePath);
            return openScene.IsValid() && openScene.isLoaded ||
                   AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null ||
                   File.Exists(Path.GetFullPath(scenePath));
        }

        private static Scene FindOpenScene(string scenePath)
        {
            string normalizedPath = scenePath.Replace('\\', '/');
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (string.Equals(scene.path.Replace('\\', '/'), normalizedPath,
                        StringComparison.OrdinalIgnoreCase))
                    return scene;
            }
            return default;
        }

        private static void SaveUntitledSceneAsMain(Scene scene)
        {
            // An untitled scene has never been saved as user content. Treat it as the
            // disposable Unity template scene and build a deterministic Main scene.
            foreach (GameObject root in scene.GetRootGameObjects())
                UnityEngine.Object.DestroyImmediate(root);

            var main = new GameObject("Main");
            SceneManager.MoveGameObjectToScene(main, scene);
            CreateSceneCamera(scene, false);
            CreateOrUpdateMainHotUpdateTest(scene, main);
            if (!EditorSceneManager.SaveScene(scene, MainScenePath))
                throw new IOException($"Failed to create host scene: {MainScenePath}");
            Debug.Log($"[QHYFramework] 已创建宿主场景：{MainScenePath}");
        }

        private static void CreateHostScene(string path, string rootName, QHYFrameworkSettings settings,
            BootUGUIView bootPrefab)
        {
            // Automatic initialization can resume after a script/domain reload while the
            // scene created by the previous pass is still open. Never create a second
            // scene and save it over an already open path.
            if (SceneAssetExistsOrOpen(path))
            {
                if (settings)
                    EnsureBootSceneCamera();
                else
                    EnsureMainSceneSetup(path);
                return;
            }

            if (!settings)
            {
                Scene untitledScene = FindUntitledOpenScene(SceneManager.GetActiveScene());
                if (untitledScene.IsValid())
                {
                    SaveUntitledSceneAsMain(untitledScene);
                    return;
                }
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var root = new GameObject(rootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                if (settings)
                {
                    CreateSceneCamera(scene, true);
                    GameBootstrap bootstrap = root.AddComponent<GameBootstrap>();
                    var serialized = new SerializedObject(bootstrap);
                    serialized.FindProperty("settings").objectReferenceValue = settings;
                    serialized.FindProperty("bootViewPrefab").objectReferenceValue = bootPrefab;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                else
                {
                    CreateSceneCamera(scene, false);
                    CreateOrUpdateMainHotUpdateTest(scene, root);
                }

                if (!EditorSceneManager.SaveScene(scene, path))
                    throw new IOException(F("无法创建宿主场景：{0}",
                        "Failed to create host scene: {0}", path));
                Debug.Log(F("[QHYFramework] 已创建宿主场景：{0}",
                    "[QHYFramework] Created host scene: {0}", path));
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void EnsureBootSceneCamera()
        {
            EnsureSceneCamera(IntegrationProjectPaths.BootScene, "Boot", true);
        }

        private static void EnsureMainSceneSetup(string scenePath)
        {
            Scene scene = FindOpenScene(scenePath);
            bool openedForMigration = !scene.IsValid() || !scene.isLoaded;
            if (openedForMigration)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            try
            {
                GameObject main = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Main");
                if (!main)
                {
                    main = new GameObject("Main");
                    SceneManager.MoveGameObjectToScene(main, scene);
                }

                if (!scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<Camera>(true) != null))
                    CreateSceneCamera(scene, false);
                CreateOrUpdateMainHotUpdateTest(scene, main);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new IOException($"Failed to update Main scene: {scenePath}");
            }
            finally
            {
                if (openedForMigration && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void CreateOrUpdateMainHotUpdateTest(Scene scene, GameObject main)
        {
            Type componentType = ResolveHotUpdateEntryType();

            Text statusText = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Text>(true))
                .FirstOrDefault(text => text.name == "HotUpdateStatusText");
            if (!statusText)
            {
                var canvasObject = new GameObject("HotUpdateTestCanvas", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);

                var textObject = new GameObject("HotUpdateStatusText", typeof(RectTransform), typeof(Text));
                textObject.transform.SetParent(canvasObject.transform, false);
                statusText = textObject.GetComponent<Text>();
                statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                statusText.fontSize = 36;
                statusText.alignment = TextAnchor.MiddleCenter;
                statusText.color = Color.white;
                statusText.text = "Waiting for Game.HotUpdate code...";
                RectTransform rect = statusText.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(1200f, 180f);
            }

            Component entry = main.GetComponent(componentType) ?? main.AddComponent(componentType);
            var serialized = new SerializedObject(entry);
            SerializedProperty targetText = serialized.FindProperty("targetText");
            if (targetText == null)
                throw new InvalidOperationException("HotUpdateEntry must declare a serialized targetText field.");
            targetText.objectReferenceValue = statusText;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == main)
                    continue;
                Component duplicate = root.GetComponent(componentType);
                if (duplicate)
                    UnityEngine.Object.DestroyImmediate(duplicate);
                if (root.name == "HotUpdateEntry" && root.GetComponents<Component>().Length == 1)
                    UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Type ResolveHotUpdateEntryType()
        {
            string scriptPath = IntegrationProjectPaths.HotUpdateScripts + "/HotUpdateEntry.cs";
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            Type componentType = script ? script.GetClass() : null;
            if (componentType == null || !typeof(MonoBehaviour).IsAssignableFrom(componentType))
                throw new HotUpdateScriptCompilationPendingException();
            return componentType;
        }

        private static void EnsureSceneCamera(string scenePath, string sceneName, bool orthographic = false)
        {
            Scene scene = FindOpenScene(scenePath);
            bool openedForMigration = !scene.IsValid() || !scene.isLoaded;
            if (openedForMigration)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            try
            {
                bool hasCamera = scene.GetRootGameObjects()
                    .Any(root => root.GetComponentInChildren<Camera>(true) != null);
                if (hasCamera)
                    return;

                CreateSceneCamera(scene, orthographic);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new IOException($"Failed to add the Main Camera to {scenePath}.");
                Debug.Log($"[QHYFramework] 已为 {sceneName} 场景添加 Main Camera：{scenePath}");
            }
            finally
            {
                if (openedForMigration && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void CreateSceneCamera(Scene scene, bool orthographic)
        {
            var cameraObject = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.075f, 0.115f, 1f);
            camera.orthographic = orthographic;
            cameraObject.AddComponent<AudioListener>();
        }

        private static void RecreateGeneratedDirectory(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(fullPath);
            foreach (string file in Directory.GetFiles(fullPath))
            {
                if (!file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
            }
        }

        private static void CopyAsBytes(string source, string destination, bool required)
        {
            if (!File.Exists(source))
            {
                if (required)
                    throw new FileNotFoundException(F("缺少编译产物：{0}",
                        "Compiled output is missing: {0}", source));
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
            File.Copy(source, destination, true);
        }

        private static string L(string chinese, string english)
        {
            return EditorLocalization.Text(chinese, english);
        }

        private static string F(string chinese, string english, params object[] args)
        {
            return EditorLocalization.Format(chinese, english, args);
        }
    }
}
