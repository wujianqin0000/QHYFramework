using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameIntegration.Editor
{
    public static class BootUIPrefabGenerator
    {
        public const string PrefabPath = IntegrationProjectPaths.BootPrefab;

        public static void EnsureExists()
        {
            BootUGUIView existing = AssetDatabase.LoadAssetAtPath<BootUGUIView>(PrefabPath);
            if (existing)
                return;
            Generate();
            Debug.Log(EditorLocalization.Format("[QHYFramework] 已生成可定制的 Boot UGUI Prefab：{0}",
                "[QHYFramework] Created customizable Boot UGUI prefab: {0}", PrefabPath));
        }

        private static void Generate()
        {
            EnsureAssetFolder(IntegrationProjectPaths.BootRoot);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(IntegrationProjectPaths.BootPrefabTemplate) &&
                AssetDatabase.CopyAsset(IntegrationProjectPaths.BootPrefabTemplate, PrefabPath))
            {
                AssetDatabase.SaveAssets();
                return;
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject root = CreateObject("BootUI", null, typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(EventSystem), typeof(StandaloneInputModule), typeof(BootUGUIView));
            try
            {
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = short.MaxValue;
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;

                GameObject shade = CreateObject("Shade", root.transform, typeof(Image));
                Stretch(shade.GetComponent<RectTransform>());
                shade.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.07f, 1f);

                GameObject panel = CreateObject("Panel", shade.transform, typeof(Image));
                SetRect(panel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                    new Vector2(840, 480), Vector2.zero);
                panel.GetComponent<Image>().color = new Color(0.09f, 0.115f, 0.17f, 0.98f);

                Text title = CreateText("Title", panel.transform, font, 38, TextAnchor.MiddleCenter);
                SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(760, 64),
                    new Vector2(0, -54));
                title.text = "游戏启动与热更新";
                title.color = Color.white;

                Text status = CreateText("Status", panel.transform, font, 25, TextAnchor.MiddleCenter);
                SetRect(status.rectTransform, new Vector2(0.5f, 1f), new Vector2(760, 52),
                    new Vector2(0, -132));
                status.text = "准备启动…";
                status.color = new Color(0.82f, 0.88f, 1f);

                GameObject progressBackground = CreateObject("ProgressBackground", panel.transform, typeof(Image));
                SetRect(progressBackground.GetComponent<RectTransform>(), new Vector2(0.5f, 1f),
                    new Vector2(700, 28), new Vector2(0, -202));
                progressBackground.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.06f, 1f);

                GameObject progressFillObject = CreateObject("ProgressFill", progressBackground.transform,
                    typeof(Image));
                RectTransform fillRect = progressFillObject.GetComponent<RectTransform>();
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.offsetMin = new Vector2(3, 3);
                fillRect.offsetMax = new Vector2(-3, -3);
                Image progressFill = progressFillObject.GetComponent<Image>();
                progressFill.color = new Color(0.2f, 0.65f, 1f, 1f);
                progressFill.type = Image.Type.Filled;
                progressFill.fillMethod = Image.FillMethod.Horizontal;

                Text detail = CreateText("Detail", panel.transform, font, 18, TextAnchor.MiddleCenter);
                SetRect(detail.rectTransform, new Vector2(0.5f, 1f), new Vector2(760, 44),
                    new Vector2(0, -250));
                detail.color = new Color(0.65f, 0.72f, 0.82f);

                Text error = CreateText("Error", panel.transform, font, 18, TextAnchor.MiddleCenter);
                SetRect(error.rectTransform, new Vector2(0.5f, 1f), new Vector2(740, 72),
                    new Vector2(0, -318));
                error.color = new Color(1f, 0.35f, 0.35f);

                Button confirmButton = CreateButton("ConfirmDownload", panel.transform, font,
                    out Text confirmText);
                SetRect(confirmButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                    new Vector2(520, 62), new Vector2(0, 58));
                confirmText.text = "下载更新";
                confirmButton.gameObject.SetActive(false);

                Button retryButton = CreateButton("Retry", panel.transform, font, out Text retryText);
                SetRect(retryButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                    new Vector2(300, 62), new Vector2(0, 58));
                retryText.text = "重试";
                retryButton.gameObject.SetActive(false);

                var serializedView = new SerializedObject(root.GetComponent<BootUGUIView>());
                serializedView.FindProperty("status").objectReferenceValue = status;
                serializedView.FindProperty("detail").objectReferenceValue = detail;
                serializedView.FindProperty("error").objectReferenceValue = error;
                serializedView.FindProperty("progressFill").objectReferenceValue = progressFill;
                serializedView.FindProperty("confirmButton").objectReferenceValue = confirmButton;
                serializedView.FindProperty("confirmText").objectReferenceValue = confirmText;
                serializedView.FindProperty("retryButton").objectReferenceValue = retryButton;
                serializedView.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Button CreateButton(string name, Transform parent, Font font, out Text label)
        {
            GameObject buttonObject = CreateObject(name, parent, typeof(Image), typeof(Button));
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.12f, 0.48f, 0.88f, 1f);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.2f, 0.62f, 1f, 1f);
            colors.pressedColor = new Color(0.08f, 0.35f, 0.7f, 1f);
            colors.disabledColor = new Color(0.25f, 0.28f, 0.34f, 1f);
            button.colors = colors;
            label = CreateText("Label", buttonObject.transform, font, 21, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.color = Color.white;
            return button;
        }

        private static Text CreateText(string name, Transform parent, Font font, int size, TextAnchor anchor)
        {
            GameObject gameObject = CreateObject(name, parent, typeof(Text));
            Text text = gameObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static GameObject CreateObject(string name, Transform parent, params System.Type[] components)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = 5;
            foreach (System.Type component in components)
                gameObject.AddComponent(component);
            if (parent)
                gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent))
                EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
