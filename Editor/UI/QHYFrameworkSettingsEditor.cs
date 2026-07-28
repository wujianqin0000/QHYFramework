using UnityEditor;
using UnityEngine;

namespace GameIntegration.Editor
{
    [CustomEditor(typeof(QHYFrameworkSettings))]
    public sealed class QHYFrameworkSettingsEditor : UnityEditor.Editor
    {
        private void OnEnable()
        {
            EditorLocalization.LanguageChanged += Repaint;
        }

        private void OnDisable()
        {
            EditorLocalization.LanguageChanged -= Repaint;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField(L("YooAsset 配置", "YooAsset Settings"), EditorStyles.boldLabel);
            Draw("packageName", "资源包名称", "Package Name");
            DrawPlatformProfiles();
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
            EditorGUILayout.LabelField(L("启动与热更新", "Startup and Hot Update"), EditorStyles.boldLabel);
            Draw("startupSceneAddress", "首个业务场景地址", "Startup Scene Address");
            DrawHotUpdateAssemblies();
            DrawStringArray("aotMetadataAssemblyNames", "补充 AOT 元数据程序集",
                "Supplemental AOT Metadata Assemblies", "程序集名称", "Assembly Name");

            EditorGUILayout.Space(10);
            DrawFtpSettings();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawFtpSettings()
        {
            EditorGUILayout.LabelField(L("FTP 上传", "FTP Upload"), EditorStyles.boldLabel);
            Draw("ftpHost", "FTP 主机", "FTP Host");
            Draw("ftpPort", "FTP 端口", "FTP Port");
            Draw("ftpUserName", "FTP 用户名", "FTP Username");

            SerializedProperty password = serializedObject.FindProperty("ftpPassword");
            if (password != null)
                password.stringValue = EditorGUILayout.PasswordField(
                    new GUIContent(L("FTP 密码", "FTP Password")), password.stringValue);
        }

        private void Draw(string propertyName, string chinese, string english, bool includeChildren = false,
            string chineseTooltip = null, string englishTooltip = null)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
                return;
            var label = new GUIContent(L(chinese, english), L(chineseTooltip ?? string.Empty,
                englishTooltip ?? string.Empty));
            EditorGUILayout.PropertyField(property, label, includeChildren);
        }

        private void DrawPlatformProfiles()
        {
            SerializedProperty array = serializedObject.FindProperty("platformProfiles");
            DrawArrayHeader(array, "平台远端配置", "Platform Remote Profiles");
            for (int index = 0; index < array.arraySize; index++)
            {
                SerializedProperty item = array.GetArrayElementAtIndex(index);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.PropertyField(item.FindPropertyRelative("platform"),
                    new GUIContent(L("平台", "Platform")));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("remoteBaseUrl"),
                    new GUIContent(L("远端根地址（不含版本）", "Remote Base URL (without version)"),
                        L("发布和运行时会自动追加 Player Settings/Version。",
                            "Publishing and runtime automatically append Player Settings/Version.")));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("clientUpdateBaseUrl"),
                    new GUIContent(L("客户端更新根地址", "Client Update Base URL"),
                        L("用于请求 latest.json，不要包含客户端版本。",
                            "Used to request latest.json; do not include the client version.")));
                bool removed = DrawRemoveButton(array, index);
                EditorGUILayout.EndVertical();
                if (removed)
                    break;
            }
            DrawAddButton(array, "添加平台", "Add Platform");
        }

        private void DrawHotUpdateAssemblies()
        {
            SerializedProperty array = serializedObject.FindProperty("hotUpdateAssemblies");
            DrawArrayHeader(array, "热更新程序集", "Hot-update Assemblies");
            for (int index = 0; index < array.arraySize; index++)
            {
                SerializedProperty item = array.GetArrayElementAtIndex(index);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.PropertyField(item.FindPropertyRelative("assemblyName"),
                    new GUIContent(L("程序集名称", "Assembly Name")));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("assetAddress"),
                    new GUIContent(L("资源地址", "Asset Address")));
                bool removed = DrawRemoveButton(array, index);
                EditorGUILayout.EndVertical();
                if (removed)
                    break;
            }
            DrawAddButton(array, "添加程序集", "Add Assembly");
        }

        private void DrawStringArray(string propertyName, string chineseTitle, string englishTitle,
            string chineseItem, string englishItem)
        {
            SerializedProperty array = serializedObject.FindProperty(propertyName);
            DrawArrayHeader(array, chineseTitle, englishTitle);
            for (int index = 0; index < array.arraySize; index++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(array.GetArrayElementAtIndex(index),
                    new GUIContent($"{L(chineseItem, englishItem)} {index + 1}"));
                if (GUILayout.Button("−", GUILayout.Width(28)))
                {
                    array.DeleteArrayElementAtIndex(index);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
            DrawAddButton(array, "添加程序集", "Add Assembly");
        }

        private static void DrawArrayHeader(SerializedProperty array, string chinese, string english)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(L(chinese, english), EditorStyles.boldLabel);
            array.arraySize = Mathf.Max(0, EditorGUILayout.IntField(array.arraySize, GUILayout.Width(48)));
            EditorGUILayout.EndHorizontal();
        }

        private static bool DrawRemoveButton(SerializedProperty array, int index)
        {
            if (GUILayout.Button(L("移除", "Remove"), GUILayout.Width(80)))
            {
                array.DeleteArrayElementAtIndex(index);
                return true;
            }
            return false;
        }

        private static void DrawAddButton(SerializedProperty array, string chinese, string english)
        {
            if (GUILayout.Button(L(chinese, english)))
                array.InsertArrayElementAtIndex(array.arraySize);
        }

        private static string L(string chinese, string english)
        {
            return EditorLocalization.Text(chinese, english);
        }
    }
}
