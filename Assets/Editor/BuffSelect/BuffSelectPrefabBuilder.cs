using System;
using GameLogic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FreshGuard.Editor
{
    /// <summary>通过 Editor API 生成 UI，不修改对局场景。</summary>
    public static class BuffSelectPrefabBuilder
    {
        public const string FOLDER = "Assets/AssetRaw/UI/BuffSelect";
        private const string SPRITES = "Assets/AssetRaw/Actor/BuffSelect/Sprites/";
        private const string FONT_PATH = "Assets/AssetRaw/Fonts/NotoSansSC-Regular.otf";

        [MenuItem("FreshGuard/Buff Select/Build Prefabs")]
        public static void Build()
        {
            if (Application.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在编译完成的非运行模式生成预制体。");
            EnsureFolder(FOLDER);
            if (AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH) == null)
                throw new InvalidOperationException("缺少中文字体：" + FONT_PATH);
            // 单卡是三种品质共用的模板；界面内保存三个嵌套实例。
            var item = AssetDatabase.LoadAssetAtPath<GameObject>(FOLDER + "/BuffSelectItem.prefab");
            if (item == null) item = Save(BuildItem(), "BuffSelectItem");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FOLDER + "/BuffSelectUI.prefab") == null)
                Save(BuildWindow(item), "BuffSelectUI");
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var slash = path.LastIndexOf('/');
            var parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
        private static GameObject Save(GameObject root, string name)
        {
            try { return PrefabUtility.SaveAsPrefabAsset(root, FOLDER + "/" + name + ".prefab"); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static Sprite Sprite(string name)
        {
            var path = SPRITES + name + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("缺少 Sprite：" + path);
            return sprite;
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rect = go.GetComponent<RectTransform>();
            if (parent != null) rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }
        private static Image Image(RectTransform rect, Sprite sprite, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.raycastTarget = raycast;
            return image;
        }
        private static Text Label(string name, Transform parent, string content, int size,
            Vector2 rectSize, Vector2 position, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var text = Rect(name, parent, rectSize, position).gameObject.AddComponent<Text>();
            text.text = content;
            text.font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        private static GameObject BuildItem()
        {
            // 参考图 204x513，统一乘 1.5 适配工程的 1080x1920 UI 坐标。
            var root = Rect("BuffSelectItem", null, new Vector2(306, 770), Vector2.zero);
            var background = Image(root, Sprite("Rogue_goldBG"), true);
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.disabledColor = Color.white;
            button.colors = colors;
            var style = root.gameObject.AddComponent<BuffSelectItemStyle>();
            var serialized = new SerializedObject(style);
            serialized.FindProperty("_blueBackground").objectReferenceValue = Sprite("Rogue_blueBG");
            serialized.FindProperty("_purpleBackground").objectReferenceValue = Sprite("Rogue_purpleBG");
            serialized.FindProperty("_goldBackground").objectReferenceValue = Sprite("Rogue_goldBG");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var icon = Image(Rect("m_img_Icon", root, new Vector2(160, 160), new Vector2(0, 224)), null);
            icon.preserveAspect = true;
            icon.enabled = false;
            Label("m_text_IconPlaceholder", root, "强化\n图标", 36, new Vector2(160, 130), new Vector2(0, 224), Color.black);
            var level = Label("m_text_Level", root, "等级1", 28, new Vector2(240, 42), new Vector2(0, 99), Color.white);
            level.gameObject.AddComponent<Outline>().effectColor = Color.black;
            level.GetComponent<Outline>().effectDistance = new Vector2(2, -2);
            var title = Label("m_text_Name", root, "金色强化", 36, new Vector2(202, 47), new Vector2(0, 23), Color.white);
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 22;
            title.resizeTextMaxSize = 36;
            Label("m_text_Description", root, "强化介绍由配置表提供。", 28,
                new Vector2(230, 330), new Vector2(0, -199), Color.black, TextAnchor.UpperLeft);
            return root.gameObject;
        }
        private static GameObject BuildWindow(GameObject item)
        {
            var root = Rect("BuffSelectUI", null, new Vector2(1080, 1920), Vector2.zero);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0;
            root.gameObject.AddComponent<GraphicRaycaster>();
            var shade = Image(root, null, true);
            shade.color = new Color(0, 0, 0, 0.55f);
            var content = Rect("m_rect_Content", root, Vector2.zero, Vector2.zero);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = content.offsetMax = Vector2.zero;
            var headline = Rect("m_img_Headline", content, new Vector2(759, 195), new Vector2(0, -222));
            headline.anchorMin = headline.anchorMax = new Vector2(0.5f, 1);
            Image(headline, Sprite("Rogue_headline")).preserveAspect = true;
            var cards = Rect("m_rect_Cards", content, new Vector2(1000, 770), new Vector2(0, 69));
            var qualities = new[] { BuffQuality.Gold, BuffQuality.Purple, BuffQuality.Blue };
            var names = new[] { "金色强化", "紫色强化", "蓝色强化" };
            for (var i = 0; i < 3; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(item, cards);
                instance.name = "m_item_Card" + i;
                ((RectTransform)instance.transform).anchoredPosition = new Vector2((i - 1) * 348, 0);
                instance.GetComponent<Image>().sprite = instance.GetComponent<BuffSelectItemStyle>().BackgroundFor(qualities[i]);
                instance.transform.Find("m_text_Level").GetComponent<Text>().text = "等级" + (i + 1);
                instance.transform.Find("m_text_Name").GetComponent<Text>().text = names[i];
                instance.transform.Find("m_text_Description").GetComponent<Text>().text = i == 0
                    ? "强化描述强化描述强化描述强化描述强化描述强化描述" : "";
            }
            var countdown = Label("m_text_Countdown", content, "<color=#E7B722>5</color>秒后自动选择", 36,
                new Vector2(720, 64), new Vector2(0, 456), Color.white);
            countdown.rectTransform.anchorMin = countdown.rectTransform.anchorMax = new Vector2(0.5f, 0);
            countdown.supportRichText = true;
            return root.gameObject;
        }
    }
}
