using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using HybridCLR.Editor;
using UnityEditor;
using UnityEngine;

[assembly: InternalsVisibleTo("GameIntegration.Tests.Editor")]

namespace GameIntegration.Editor
{
    internal sealed class GeneratedAssemblySnapshot
    {
        public BuildTarget Target { get; }
        public string HotUpdateDirectory { get; }
        public string AotDirectory { get; }
        public IReadOnlyList<string> HotUpdateAssemblies { get; }
        public IReadOnlyList<string> AotAssemblies { get; }

        public GeneratedAssemblySnapshot(BuildTarget target, string hotUpdateDirectory, string aotDirectory,
            IReadOnlyList<string> hotUpdateAssemblies, IReadOnlyList<string> aotAssemblies)
        {
            Target = target;
            HotUpdateDirectory = hotUpdateDirectory;
            AotDirectory = aotDirectory;
            HotUpdateAssemblies = hotUpdateAssemblies;
            AotAssemblies = aotAssemblies;
        }
    }

    internal static class GeneratedAssemblyCatalog
    {
        private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

        internal static GeneratedAssemblySnapshot Scan(BuildTarget target)
        {
            string hotUpdateDirectory = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            string aotDirectory = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            return Scan(target, hotUpdateDirectory, aotDirectory,
                SettingsUtil.HotUpdateAssemblyNamesExcludePreserved ?? new List<string>());
        }

        internal static GeneratedAssemblySnapshot Scan(BuildTarget target, string hotUpdateDirectory,
            string aotDirectory, IEnumerable<string> configuredHotUpdateAssemblies)
        {
            var configured = new HashSet<string>(
                (configuredHotUpdateAssemblies ?? Array.Empty<string>())
                .Select(NormalizeAssemblyName)
                .Where(name => !string.IsNullOrEmpty(name)), NameComparer);

            IReadOnlyList<string> hotUpdate = EnumerateDllNames(hotUpdateDirectory, false)
                .Select(NormalizeAssemblyName)
                .Where(configured.Contains)
                .Distinct(NameComparer)
                .OrderBy(name => name, NameComparer)
                .ToArray();
            IReadOnlyList<string> aot = EnumerateDllNames(aotDirectory, true)
                .Distinct(NameComparer)
                .OrderBy(name => name, NameComparer)
                .ToArray();
            return new GeneratedAssemblySnapshot(target, hotUpdateDirectory, aotDirectory, hotUpdate, aot);
        }

        internal static string NormalizeAssemblyName(string value)
        {
            string name = Path.GetFileName(value?.Trim() ?? string.Empty);
            return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - 4)
                : name;
        }

        internal static string NormalizeDllFileName(string value)
        {
            string name = NormalizeAssemblyName(value);
            return string.IsNullOrEmpty(name) ? string.Empty : name + ".dll";
        }

        private static IEnumerable<string> EnumerateDllNames(string directory, bool includeExtension)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return Array.Empty<string>();
            try
            {
                return Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                    .Select(path => includeExtension ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path))
                    .ToArray();
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }
    }

    [CustomEditor(typeof(QHYFrameworkSettings))]
    public sealed class QHYFrameworkSettingsEditor : UnityEditor.Editor
    {
        private GeneratedAssemblySnapshot _assemblies;
        private AotMetadataAnalysis _aotAnalysis;
        private string _assemblySearch = string.Empty;
        private bool _showAdvancedAddresses;

        private void OnEnable()
        {
            EditorLocalization.LanguageChanged += OnLanguageChanged;
            RefreshAssemblies(false);
        }

        private void OnDisable()
        {
            EditorLocalization.LanguageChanged -= OnLanguageChanged;
        }

        public override void OnInspectorGUI()
        {
            if (_assemblies == null || _assemblies.Target != EditorUserBuildSettings.activeBuildTarget)
                RefreshAssemblies(false);

            serializedObject.Update();
            NormalizeConfiguredAssemblies();

            EditorGUILayout.LabelField(L("平台资源地址", "Platform Resource URLs"), EditorStyles.boldLabel);
            Draw("gameDirectory", "游戏资源目录", "Game Resource Directory");
            DrawCurrentPlatformResourceProfile();
            EditorGUILayout.HelpBox(L(
                "BaseURL 只填写服务器资源总根；QHY 自动生成 /{游戏资源目录}/{platform}/cdn 和 /{游戏资源目录}/{platform}/origin。一个服务器根可放置多个游戏。",
                "Enter only the server resource root. QHY derives /{gameDirectory}/{platform}/cdn and /{gameDirectory}/{platform}/origin, allowing multiple games under one server root."),
                MessageType.Info);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(L("YooAsset 配置", "YooAsset Settings"), EditorStyles.boldLabel);
            Draw("editorSimulatePackageRoot", "编辑器模拟目录", "Editor Simulation Root");
            Draw("requestTimeoutSeconds", "请求超时（秒）", "Request Timeout (seconds)");
            Draw("downloadConcurrency", "下载并发数", "Download Concurrency");
            Draw("downloadRetryCount", "下载重试次数", "Download Retry Count");
            Draw("downloadMaxRequestPerFrame", "每帧最大请求数", "Max Requests Per Frame");
            Draw("downloadWatchdogTimeoutSeconds", "下载看门狗超时（秒）", "Download Watchdog Timeout (seconds)");
            Draw("copyBuiltinPackageManifest", "复制内置清单", "Copy Built-in Package Manifest");
            Draw("clearUnusedCacheAfterUpdate", "更新后清理无用缓存", "Clear Unused Cache After Update");
            Draw("refreshHostManifestEveryStartup", "每次启动刷新远端清单", "Refresh Host Manifest Every Startup");
            Draw("autoUnloadBundleWhenUnused", "自动卸载未使用 Bundle", "Auto Unload Unused Bundles");

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(L("资源收集与增量构建", "Collection and Incremental Build"),
                EditorStyles.boldLabel);
            Draw("collectorManagementMode", "Collector 管理模式", "Collector Management Mode");
            Draw("bundleWarningThresholdMiB", "Bundle 警告阈值（MiB）", "Bundle Warning Threshold (MiB)");
            Draw("bundleErrorThresholdMiB", "Bundle 错误阈值（MiB）", "Bundle Error Threshold (MiB)");
            Draw("ignoreTypeTreeChangesForIncrementalBuild", "忽略 TypeTree 变化（高级）",
                "Ignore TypeTree Changes (Advanced)");
            if (serializedObject.FindProperty("ignoreTypeTreeChangesForIncrementalBuild").boolValue)
                EditorGUILayout.HelpBox(L(
                    "高风险选项：YooAsset 3.0.4 的 SBP 不支持直接忽略 TypeTree 变化，本选项只启用诊断标记。必须完成旧客户端加载新 Bundle 的兼容测试。",
                    "High risk: YooAsset 3.0.4 SBP cannot directly ignore TypeTree changes. This option only enables diagnostics. Test old-client compatibility with new bundles."),
                    MessageType.Warning);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(L("启动与热更新", "Startup and Hot Update"), EditorStyles.boldLabel);
            Draw("startupSceneAddress", "首个业务场景地址", "Startup Scene Address");
            DrawAssemblySelection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawCurrentPlatformResourceProfile()
        {
            IntegrationPlatform currentPlatform = QHYFrameworkSettings.GetCurrentPlatform();
            EditorGUILayout.LabelField(L("当前构建平台", "Active Build Platform"),
                currentPlatform.ToString());
            if (currentPlatform == IntegrationPlatform.Unknown)
            {
                EditorGUILayout.HelpBox(L(
                    "当前 Unity BuildTarget 不受支持，请先切换到受支持的平台。",
                    "The active Unity BuildTarget is unsupported. Switch to a supported platform first."),
                    MessageType.Error);
                return;
            }

            SerializedProperty profiles = serializedObject.FindProperty("platformResourceProfiles");
            var matches = new List<int>();
            for (int index = 0; index < profiles.arraySize; index++)
            {
                SerializedProperty platform = profiles.GetArrayElementAtIndex(index)
                    .FindPropertyRelative("platform");
                if ((IntegrationPlatform)platform.intValue == currentPlatform) matches.Add(index);
            }

            if (matches.Count == 0)
            {
                EditorGUILayout.HelpBox(string.Format(L(
                    "尚未创建 {0} 的资源配置。",
                    "No resource profile exists for {0}."), currentPlatform), MessageType.Info);
                if (GUILayout.Button(L("创建当前平台配置", "Create Active Platform Profile")))
                {
                    int newIndex = profiles.arraySize;
                    profiles.InsertArrayElementAtIndex(newIndex);
                    SerializedProperty item = profiles.GetArrayElementAtIndex(newIndex);
                    item.FindPropertyRelative("platform").intValue = (int)currentPlatform;
                    item.FindPropertyRelative("baseUrl").stringValue = string.Empty;
                }
                return;
            }

            if (matches.Count > 1)
            {
                EditorGUILayout.HelpBox(string.Format(L(
                    "{0} 存在重复配置，发布前必须只保留一项。",
                    "{0} has duplicate profiles. Keep exactly one before publication."), currentPlatform),
                    MessageType.Error);
                if (GUILayout.Button(L("保留第一项并删除重复配置", "Keep First and Remove Duplicates")))
                {
                    for (int index = matches.Count - 1; index >= 1; index--)
                        profiles.DeleteArrayElementAtIndex(matches[index]);
                }
                return;
            }

            SerializedProperty profile = profiles.GetArrayElementAtIndex(matches[0]);
            SerializedProperty urlProperty = profile.FindPropertyRelative("baseUrl");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(urlProperty, new GUIContent("BaseURL",
                L("可填写 aco.ai20.top 或 https://aco.ai20.top；裸域名会自动补全 HTTPS。不要填写平台、cdn 或 origin 目录。",
                    "Enter aco.ai20.top or https://aco.ai20.top; bare hosts are completed as HTTPS. Do not include platform, cdn, or origin folders.")));
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(urlProperty.stringValue))
            {
                try
                {
                    urlProperty.stringValue = QHYFrameworkSettings.NormalizeResourceBaseUrl(
                        urlProperty.stringValue);
                }
                catch
                {
                    // Keep invalid text visible so the inline validation below can explain it.
                }
            }
            if (!string.IsNullOrWhiteSpace(urlProperty.stringValue))
            {
                try
                {
                    string root = QHYFrameworkSettings.NormalizeResourceBaseUrl(urlProperty.stringValue);
                    string gameRoot = DistributionPathResolver.CombineUrl(root,
                        ((QHYFrameworkSettings)target).GetGameDirectoryOrThrow());
                    string platformRoot = DistributionPathResolver.CombineUrl(gameRoot,
                        DistributionPathResolver.GetPlatformSegment(currentPlatform));
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField("CDN Root", platformRoot + "/cdn");
                        EditorGUILayout.TextField("Origin Root", platformRoot + "/origin");
                    }
                }
                catch (Exception exception)
                {
                    EditorGUILayout.HelpBox(exception.GetBaseException().Message, MessageType.Error);
                }
            }
        }

        private void NormalizeConfiguredAssemblies()
        {
            SerializedProperty hotUpdate = serializedObject.FindProperty("hotUpdateAssemblies");
            var hotUpdateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < hotUpdate.arraySize; index++)
            {
                SerializedProperty item = hotUpdate.GetArrayElementAtIndex(index);
                SerializedProperty nameProperty = item.FindPropertyRelative("assemblyName");
                string name = GeneratedAssemblyCatalog.NormalizeAssemblyName(nameProperty.stringValue);
                if (string.IsNullOrEmpty(name) || !hotUpdateNames.Add(name))
                {
                    hotUpdate.DeleteArrayElementAtIndex(index--);
                    continue;
                }
                nameProperty.stringValue = name;
            }

            SerializedProperty aot = serializedObject.FindProperty("aotMetadataExtraAssemblyNames");
            var aotNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < aot.arraySize; index++)
            {
                SerializedProperty item = aot.GetArrayElementAtIndex(index);
                string name = GeneratedAssemblyCatalog.NormalizeDllFileName(item.stringValue);
                if (string.IsNullOrEmpty(name) || !aotNames.Add(name))
                {
                    aot.DeleteArrayElementAtIndex(index--);
                    continue;
                }
                item.stringValue = name;
            }
        }

        private void DrawAssemblySelection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(L("生成程序集选择", "Generated Assembly Selection"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(L("当前构建平台", "Active Build Target"), _assemblies.Target.ToString());
            DrawPath(L("热更新目录", "Hot-update directory"), _assemblies.HotUpdateDirectory);
            DrawPath(L("AOT 目录", "AOT directory"), _assemblies.AotDirectory);

            EditorGUILayout.BeginHorizontal();
            _assemblySearch = EditorGUILayout.TextField(
                new GUIContent(L("搜索", "Search")), _assemblySearch, GUI.skin.FindStyle("SearchTextField"));
            if (GUILayout.Button(L("刷新", "Refresh"), GUILayout.Width(72)))
            {
                serializedObject.ApplyModifiedProperties();
                RefreshAssemblies(true);
                serializedObject.Update();
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            DrawMissingDirectoryWarnings();
            DrawHotUpdateSelection();
            DrawAotSelection();
            EditorGUILayout.EndVertical();
        }

        private void DrawHotUpdateSelection()
        {
            SerializedProperty array = serializedObject.FindProperty("hotUpdateAssemblies");
            EditorGUILayout.Space(5);
            DrawSelectionHeader(L("热更新程序集", "Hot-update Assemblies"), () => ClearVisibleHotUpdate(array));

            foreach (string name in _assemblies.HotUpdateAssemblies.Where(MatchesSearch))
            {
                bool selected = FindHotUpdate(array, name) >= 0;
                bool next = EditorGUILayout.ToggleLeft(name, selected);
                if (next != selected)
                    SetHotUpdateSelected(array, name, next);
            }

            string[] unavailable = GetUnavailableHotUpdate(array);
            if (unavailable.Length > 0)
            {
                EditorGUILayout.HelpBox(L(
                    "以下配置未在当前平台生成或未被 HybridCLR 配置。切换平台或刷新不会自动删除它们。",
                    "The following entries are not generated for this target or are not configured in HybridCLR. Switching target or refreshing will not remove them."), MessageType.Warning);
                foreach (string name in unavailable.Where(MatchesSearch))
                {
                    if (!EditorGUILayout.ToggleLeft(name, true))
                        SetHotUpdateSelected(array, name, false);
                }
            }

            _showAdvancedAddresses = EditorGUILayout.Foldout(_showAdvancedAddresses,
                L("高级 Address 配置", "Advanced Address Configuration"), true);
            if (_showAdvancedAddresses)
                DrawHotUpdateAddresses(array);
        }

        private void DrawAotSelection()
        {
            SerializedProperty automatic = serializedObject.FindProperty("aotMetadataAutoAssemblyNames");
            SerializedProperty extras = serializedObject.FindProperty("aotMetadataExtraAssemblyNames");
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(L("自动检测的 AOT 元数据程序集", "Automatically Detected AOT Metadata"),
                EditorStyles.boldLabel);

            QHYFrameworkSettings settings = (QHYFrameworkSettings)target;
            bool targetMatches = string.Equals(settings.aotMetadataAnalysisTarget,
                EditorUserBuildSettings.activeBuildTarget.ToString(), StringComparison.Ordinal);
            if (!targetMatches && !string.IsNullOrEmpty(settings.aotMetadataAnalysisTarget))
                EditorGUILayout.HelpBox(L(
                    $"当前结果来自 {settings.aotMetadataAnalysisTarget}，与当前平台不一致。请执行 HybridCLR Generate/All 或点击刷新重新分析。",
                    $"The stored result is for {settings.aotMetadataAnalysisTarget}, not the active target. Run HybridCLR Generate/All or click Refresh."),
                    MessageType.Warning);
            if (_aotAnalysis != null && !_aotAnalysis.Succeeded)
                EditorGUILayout.HelpBox(L("AOT 自动分析失败：", "AOT analysis failed: ") + _aotAnalysis.Error +
                                        "\n" + _aotAnalysis.SourcePath, MessageType.Warning);
            else if (_aotAnalysis != null && targetMatches &&
                     !string.Equals(settings.aotMetadataAnalysisHash, _aotAnalysis.SourceHash,
                         StringComparison.OrdinalIgnoreCase))
                EditorGUILayout.HelpBox(L("HybridCLR 分析文件已经变化，请点击刷新同步最新结果。",
                    "The HybridCLR analysis file has changed. Click Refresh to synchronize the latest result."),
                    MessageType.Warning);

            EditorGUILayout.LabelField(L("分析平台", "Analysis Target"),
                string.IsNullOrEmpty(settings.aotMetadataAnalysisTarget) ? L("尚未分析", "Not analyzed") :
                    settings.aotMetadataAnalysisTarget);
            if (!string.IsNullOrEmpty(settings.aotMetadataAnalysisUtc))
                EditorGUILayout.LabelField(L("分析时间（UTC）", "Analyzed At (UTC)"), settings.aotMetadataAnalysisUtc);
            if (!string.IsNullOrEmpty(settings.aotMetadataAnalysisHash))
                EditorGUILayout.LabelField(L("分析哈希", "Analysis Hash"),
                    settings.aotMetadataAnalysisHash.Substring(0,
                        Math.Min(12, settings.aotMetadataAnalysisHash.Length)));

            using (new EditorGUI.DisabledScope(true))
            {
                for (int index = 0; index < automatic.arraySize; index++)
                {
                    string name = automatic.GetArrayElementAtIndex(index).stringValue;
                    if (MatchesSearch(name))
                        EditorGUILayout.ToggleLeft(name, true);
                }
            }
            if (automatic.arraySize == 0)
                EditorGUILayout.HelpBox(L("尚未检测到需要补充元数据的 AOT 程序集。",
                    "No AOT assembly requiring supplemental metadata has been detected."), MessageType.Info);

            EditorGUILayout.Space(5);
            DrawSelectionHeader(L("额外 AOT 元数据程序集", "Additional AOT Metadata Assemblies"),
                () => ClearVisibleAot(extras));
            var automaticNames = new HashSet<string>(ReadStringArray(automatic), StringComparer.OrdinalIgnoreCase);

            foreach (string fileName in _assemblies.AotAssemblies.Where(MatchesSearch))
            {
                if (automaticNames.Contains(fileName))
                    continue;
                bool selected = FindAot(extras, fileName) >= 0;
                bool next = EditorGUILayout.ToggleLeft(fileName, selected);
                if (next != selected)
                    SetAotSelected(extras, fileName, next);
            }

            string[] unavailable = GetUnavailableAot(extras);
            if (unavailable.Length > 0)
            {
                EditorGUILayout.HelpBox(L(
                    "以下额外 AOT 配置在当前平台的裁剪目录中不存在，Full Package Build 将阻止构建。",
                    "The following additional AOT entries are missing from this target's stripped directory. Full Package Build will be blocked."), MessageType.Warning);
                foreach (string fileName in unavailable.Where(MatchesSearch))
                {
                    if (!EditorGUILayout.ToggleLeft(fileName, true))
                        SetAotSelected(extras, fileName, false);
                }
            }

            WriteStringArray(serializedObject.FindProperty("aotMetadataAssemblyNames"),
                AotMetadataAutomation.Normalize(ReadStringArray(automatic).Concat(ReadStringArray(extras))));
        }

        private void DrawHotUpdateAddresses(SerializedProperty array)
        {
            EditorGUI.indentLevel++;
            for (int index = 0; index < array.arraySize; index++)
            {
                SerializedProperty item = array.GetArrayElementAtIndex(index);
                string name = GeneratedAssemblyCatalog.NormalizeAssemblyName(
                    item.FindPropertyRelative("assemblyName").stringValue);
                if (!MatchesSearch(name))
                    continue;
                EditorGUILayout.PropertyField(item.FindPropertyRelative("assetAddress"), new GUIContent(name));
            }
            EditorGUI.indentLevel--;
        }

        private void DrawSelectionHeader(string title, Action clearVisible)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (GUILayout.Button(L("清空筛选项", "Clear Visible"), GUILayout.Width(100)))
                clearVisible();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMissingDirectoryWarnings()
        {
            if (!Directory.Exists(_assemblies.HotUpdateDirectory))
                DrawGenerationWarning(L("热更新程序集目录不存在：", "Hot-update assembly directory does not exist: "),
                    _assemblies.HotUpdateDirectory);
            else if (_assemblies.HotUpdateAssemblies.Count == 0)
                EditorGUILayout.HelpBox(L(
                    "没有找到同时满足“HybridCLR 已配置”和“当前平台已生成”的热更新程序集。",
                    "No hot-update assembly is both configured in HybridCLR and generated for the active target."), MessageType.Info);

            if (!Directory.Exists(_assemblies.AotDirectory))
                DrawGenerationWarning(L("AOT 裁剪程序集目录不存在：", "Stripped AOT assembly directory does not exist: "),
                    _assemblies.AotDirectory);
            else if (_assemblies.AotAssemblies.Count == 0)
                EditorGUILayout.HelpBox(L("当前平台的 AOT 裁剪目录中没有 DLL。",
                    "The active target's stripped AOT directory contains no DLL files."), MessageType.Info);
        }

        private static void DrawGenerationWarning(string prefix, string path)
        {
            EditorGUILayout.HelpBox(prefix + path + "\n" + L(
                "请先执行 HybridCLR Generate/All 或 QHY Framework 完整构建，然后点击刷新。",
                "Run HybridCLR Generate/All or a QHY Framework full build, then click Refresh."), MessageType.Warning);
        }

        private static void DrawPath(string label, string path)
        {
            EditorGUILayout.LabelField(new GUIContent(label, path), new GUIContent(path), EditorStyles.miniLabel);
        }

        private void SetHotUpdateSelected(SerializedProperty array, string name, bool selected)
        {
            int index = FindHotUpdate(array, name);
            if (selected && index < 0)
            {
                index = array.arraySize;
                array.InsertArrayElementAtIndex(index);
                SerializedProperty item = array.GetArrayElementAtIndex(index);
                item.FindPropertyRelative("assemblyName").stringValue = name;
                item.FindPropertyRelative("assetAddress").stringValue = name + ".dll";
                SortHotUpdate(array);
            }
            else if (!selected && index >= 0)
            {
                array.DeleteArrayElementAtIndex(index);
            }
        }

        private void SetAotSelected(SerializedProperty array, string fileName, bool selected)
        {
            string canonical = GeneratedAssemblyCatalog.NormalizeDllFileName(fileName);
            int index = FindAot(array, canonical);
            if (selected && index < 0)
            {
                array.InsertArrayElementAtIndex(array.arraySize);
                array.GetArrayElementAtIndex(array.arraySize - 1).stringValue = canonical;
                SortStringArray(array);
            }
            else if (!selected && index >= 0)
            {
                array.DeleteArrayElementAtIndex(index);
            }
        }

        private void ClearVisibleHotUpdate(SerializedProperty array)
        {
            for (int index = array.arraySize - 1; index >= 0; index--)
            {
                string name = array.GetArrayElementAtIndex(index).FindPropertyRelative("assemblyName").stringValue;
                if (MatchesSearch(name))
                    array.DeleteArrayElementAtIndex(index);
            }
        }

        private void ClearVisibleAot(SerializedProperty array)
        {
            for (int index = array.arraySize - 1; index >= 0; index--)
            {
                if (MatchesSearch(array.GetArrayElementAtIndex(index).stringValue))
                    array.DeleteArrayElementAtIndex(index);
            }
        }

        private static int FindHotUpdate(SerializedProperty array, string name)
        {
            string normalized = GeneratedAssemblyCatalog.NormalizeAssemblyName(name);
            for (int index = 0; index < array.arraySize; index++)
            {
                string current = array.GetArrayElementAtIndex(index).FindPropertyRelative("assemblyName").stringValue;
                if (string.Equals(GeneratedAssemblyCatalog.NormalizeAssemblyName(current), normalized,
                        StringComparison.OrdinalIgnoreCase))
                    return index;
            }
            return -1;
        }

        private static int FindAot(SerializedProperty array, string name)
        {
            string normalized = GeneratedAssemblyCatalog.NormalizeDllFileName(name);
            for (int index = 0; index < array.arraySize; index++)
            {
                if (string.Equals(GeneratedAssemblyCatalog.NormalizeDllFileName(
                        array.GetArrayElementAtIndex(index).stringValue), normalized, StringComparison.OrdinalIgnoreCase))
                    return index;
            }
            return -1;
        }

        private string[] GetUnavailableHotUpdate(SerializedProperty array)
        {
            var available = new HashSet<string>(_assemblies.HotUpdateAssemblies, StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            for (int index = 0; index < array.arraySize; index++)
            {
                string name = GeneratedAssemblyCatalog.NormalizeAssemblyName(
                    array.GetArrayElementAtIndex(index).FindPropertyRelative("assemblyName").stringValue);
                if (!string.IsNullOrEmpty(name) && !available.Contains(name))
                    result.Add(name);
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private string[] GetUnavailableAot(SerializedProperty array)
        {
            var available = new HashSet<string>(_assemblies.AotAssemblies, StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            for (int index = 0; index < array.arraySize; index++)
            {
                string name = GeneratedAssemblyCatalog.NormalizeDllFileName(array.GetArrayElementAtIndex(index).stringValue);
                if (!string.IsNullOrEmpty(name) && !available.Contains(name))
                    result.Add(name);
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void SortHotUpdate(SerializedProperty array)
        {
            for (int i = 0; i < array.arraySize - 1; i++)
            for (int j = i + 1; j < array.arraySize; j++)
            {
                string a = array.GetArrayElementAtIndex(i).FindPropertyRelative("assemblyName").stringValue;
                string b = array.GetArrayElementAtIndex(j).FindPropertyRelative("assemblyName").stringValue;
                if (StringComparer.OrdinalIgnoreCase.Compare(a, b) > 0)
                    array.MoveArrayElement(j, i);
            }
        }

        private static void SortStringArray(SerializedProperty array)
        {
            for (int i = 0; i < array.arraySize - 1; i++)
            for (int j = i + 1; j < array.arraySize; j++)
            {
                if (StringComparer.OrdinalIgnoreCase.Compare(array.GetArrayElementAtIndex(i).stringValue,
                        array.GetArrayElementAtIndex(j).stringValue) > 0)
                    array.MoveArrayElement(j, i);
            }
        }

        private bool MatchesSearch(string value)
        {
            return string.IsNullOrWhiteSpace(_assemblySearch) ||
                   (value ?? string.Empty).IndexOf(_assemblySearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshAssemblies(bool synchronizeAot)
        {
            BuildTarget activeTarget = EditorUserBuildSettings.activeBuildTarget;
            try
            {
                _assemblies = GeneratedAssemblyCatalog.Scan(activeTarget);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _assemblies = new GeneratedAssemblySnapshot(activeTarget, string.Empty, string.Empty,
                    Array.Empty<string>(), Array.Empty<string>());
            }

            QHYFrameworkSettings settings = (QHYFrameworkSettings)target;
            try
            {
                _aotAnalysis = synchronizeAot
                    ? AotMetadataAutomation.Synchronize(settings, activeTarget, false)
                    : AotMetadataAutomation.Inspect(settings, activeTarget);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QHYFramework] " + exception.GetBaseException().Message);
                _aotAnalysis = AotMetadataAutomation.Inspect(settings, activeTarget);
                _aotAnalysis.Error = exception.GetBaseException().Message;
            }
            Repaint();
        }

        private static string[] ReadStringArray(SerializedProperty array)
        {
            var values = new string[array.arraySize];
            for (int index = 0; index < array.arraySize; index++)
                values[index] = array.GetArrayElementAtIndex(index).stringValue;
            return values;
        }

        private static void WriteStringArray(SerializedProperty array, IReadOnlyList<string> values)
        {
            array.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
                array.GetArrayElementAtIndex(index).stringValue = values[index];
        }

        private void OnLanguageChanged()
        {
            Repaint();
        }

        private void Draw(string propertyName, string chinese, string english, bool includeChildren = false,
            string chineseTooltip = null, string englishTooltip = null)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
                return;
            EditorGUILayout.PropertyField(property, new GUIContent(L(chinese, english),
                L(chineseTooltip ?? string.Empty, englishTooltip ?? string.Empty)), includeChildren);
        }

        private static string L(string chinese, string english)
        {
            return EditorLocalization.Text(chinese, english);
        }
    }
}
