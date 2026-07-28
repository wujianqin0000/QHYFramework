using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GameIntegration.Editor
{
    public enum EditorToolLanguage
    {
        Chinese,
        English
    }

    /// <summary>QHY Framework 编辑器工具的轻量本地化入口。</summary>
    public static class EditorLocalization
    {
        private const string LanguageKey = "GameIntegration.Editor.Language";
        private static EditorToolLanguage _language =
            (EditorToolLanguage)EditorPrefs.GetInt(LanguageKey, (int)EditorToolLanguage.Chinese);

        public static event Action LanguageChanged;

        public static EditorToolLanguage Language
        {
            get => _language;
            set
            {
                if (_language == value)
                    return;
                _language = value;
                EditorPrefs.SetInt(LanguageKey, (int)value);
                LanguageChanged?.Invoke();
            }
        }

        public static string Text(string chinese, string english)
        {
            return Language == EditorToolLanguage.English ? english : chinese;
        }

        public static string Format(string chinese, string english, params object[] args)
        {
            return string.Format(Text(chinese, english), args);
        }

        internal static void UseChinese()
        {
            Language = EditorToolLanguage.Chinese;
        }

        internal static void UseEnglish()
        {
            Language = EditorToolLanguage.English;
        }
    }

    /// <summary>根据当前编辑器语言动态注册 QHY Framework 顶栏菜单。</summary>
    [InitializeOnLoad]
    internal static class LocalizedEditorMenu
    {
        private const string Root = "QHY Framework/";
        private static readonly MethodInfo AddMenuItemMethod = FindMenuMethod("AddMenuItem", 6);
        private static readonly MethodInfo RemoveMenuItemMethod = FindMenuMethod("RemoveMenuItem", 1);

        private static readonly string[] AllLocalizedPaths =
        {
            Root + "准备项目",
            Root + "复制最新构建资源包到 StreamingAssets",
            Root + "创建 Boot UI 预制体",
            Root + "重建默认 Boot UI 预制体",
            Root + "发布窗口",
            Root + "语言/中文",
            Root + "语言/English",
            Root + "Prepare Project",
            Root + "Copy Latest Built Package To StreamingAssets",
            Root + "Create Boot UI Prefab",
            Root + "Recreate Default Boot UI Prefab",
            Root + "Release Window",
            Root + "Language/中文",
            Root + "Language/English",
            "Game Integration/发布窗口",
            "Game Integration/语言/中文",
            "Game Integration/语言/English",
            "Game Integration/Release Window",
            "Game Integration/Language/中文",
            "Game Integration/Language/English"
        };

        static LocalizedEditorMenu()
        {
            EditorLocalization.LanguageChanged += ScheduleRebuild;
            ScheduleRebuild();
        }

        private static void ScheduleRebuild()
        {
            EditorApplication.delayCall -= Rebuild;
            EditorApplication.delayCall += Rebuild;
        }

        private static void Rebuild()
        {
            if (AddMenuItemMethod == null || RemoveMenuItemMethod == null)
            {
                Debug.LogError(EditorLocalization.Text(
                    "[QHYFramework] 当前 Unity 版本不支持动态编辑器菜单，无法本地化顶栏下拉菜单。",
                    "[QHYFramework] This Unity version does not support dynamic editor menus, so the top menu cannot be localized."));
                return;
            }

            foreach (string path in AllLocalizedPaths)
                RemoveMenuItemMethod.Invoke(null, new object[] { path });

            bool chinese = EditorLocalization.Language == EditorToolLanguage.Chinese;
            string languageMenu = chinese ? "语言/" : "Language/";
            var items = new List<MenuRegistration>
            {
                new MenuRegistration(chinese ? "发布窗口" : "Release Window", 1, ReleaseWindow.Open),
                new MenuRegistration(languageMenu + "中文", 100, EditorLocalization.UseChinese,
                    EditorLocalization.Language == EditorToolLanguage.Chinese),
                new MenuRegistration(languageMenu + "English", 101, EditorLocalization.UseEnglish,
                    EditorLocalization.Language == EditorToolLanguage.English)
            };

            foreach (MenuRegistration item in items)
            {
                AddMenuItemMethod.Invoke(null, new object[]
                {
                    Root + item.Path,
                    string.Empty,
                    item.Checked,
                    item.Priority,
                    item.Execute,
                    null
                });
            }
        }

        private static MethodInfo FindMenuMethod(string name, int parameterCount)
        {
            foreach (MethodInfo method in typeof(Menu).GetMethods(BindingFlags.Static |
                         BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name == name && method.GetParameters().Length == parameterCount)
                    return method;
            }

            return null;
        }

        private sealed class MenuRegistration
        {
            public readonly string Path;
            public readonly int Priority;
            public readonly Action Execute;
            public readonly bool Checked;

            public MenuRegistration(string path, int priority, Action execute, bool isChecked = false)
            {
                Path = path;
                Priority = priority;
                Execute = execute;
                Checked = isChecked;
            }
        }
    }
}
