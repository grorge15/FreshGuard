using System;
using GameLogic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FreshGuard.Editor
{
    public static class BattleInfoPrefabBuilder
    {
        public const string PATH = "Assets/AssetRaw/UI/BattleInfo/BattleInfoUI.prefab";
        private const string SPRITES = "Assets/AssetRaw/Actor/GeneralSprites/";
        private static readonly Color Gold = new Color(1f, 0.76f, 0.18f);
        private static readonly Color Ink = new Color(0.14f, 0.08f, 0.04f);

        [MenuItem("FreshGuard/Battle Info/Create Missing Prefab")]
        public static void Build()
        {
            if (Application.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在编译完成的非运行模式生成战斗信息预制体。");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PATH) != null) return;
            if (!AssetDatabase.IsValidFolder("Assets/AssetRaw/UI/BattleInfo"))
                AssetDatabase.CreateFolder("Assets/AssetRaw/UI", "BattleInfo");
            var root = Rect("BattleInfoUI", null, Vector2.zero, Vector2.zero);
            try
            {
                Stretch(root);
                var canvas = root.gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                root.gameObject.AddComponent<GraphicRaycaster>();
                var scaler = root.gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0;
                var group = root.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
                var view = root.gameObject.AddComponent<BattleInfoView>();
                var refs = new SerializedObject(view);

                var panel = Rect("m_rect_BattleInfo", root, new Vector2(1040, 156), Vector2.zero);
                SetPanelLayout(panel);
                Bind(refs, "_panel", panel);
                Image(panel, Sprite("Battle_middleInfoBG"));
                Side(panel, refs, true);
                Side(panel, refs, false);
                var battle = Rect("m_rect_Battle", panel, new Vector2(196, 136), Vector2.zero);
                Image(battle, Sprite("Battle_battleInfoBG"));
                Bind(refs, "_wave", Label("m_text_Wave", battle, "波次 1", 26,
                    new Vector2(160, 38), new Vector2(0, 30), Ink));
                var elapsed = Label("m_text_ElapsedTime", battle, "00:00", 43,
                    new Vector2(166, 58), new Vector2(0, -12), Ink);
                elapsed.resizeTextForBestFit = true;
                elapsed.resizeTextMinSize = 24;
                elapsed.resizeTextMaxSize = 43;
                Bind(refs, "_elapsedTime", elapsed);
                refs.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root.gameObject, PATH);
            }
            finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
        }

        /// <summary>原位迁移现有 Prefab；不重建头像、名字、奖杯及横排子节点，可重复调用。</summary>
        [MenuItem("FreshGuard/Battle Info/Update Existing Layout")]
        public static void UpdateExistingLayout()
        {
            if (Application.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在编译完成的非运行模式更新战斗信息预制体。");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PATH) == null)
                throw new InvalidOperationException("战斗信息预制体不存在，请先调用 Build()。");

            var root = PrefabUtility.LoadPrefabContents(PATH);
            try
            {
                var panel = root.transform.Find("m_rect_BattleInfo") as RectTransform;
                var view = root.GetComponent<BattleInfoView>();
                var elapsed = panel != null ? panel.Find("m_rect_Battle/m_text_ElapsedTime") : null;
                var elapsedText = elapsed != null ? elapsed.GetComponent<Text>() : null;
                if (panel == null || view == null || elapsedText == null)
                    throw new InvalidOperationException("战斗信息预制体缺少面板、展示组件或计时文字，停止更新。");

                SetPanelLayout(panel);
                var refs = new SerializedObject(view);
                Bind(refs, "_panel", panel);
                refs.ApplyModifiedPropertiesWithoutUndo();
                elapsedText.text = "00:00";
                var group = root.GetComponent<CanvasGroup>();
                if (group == null) group = root.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
                foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                    graphic.raycastTarget = false;
                if (PrefabUtility.SaveAsPrefabAsset(root, PATH) == null)
                    throw new InvalidOperationException("战斗信息预制体保存失败。");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetPanelLayout(RectTransform panel)
        {
            panel.anchorMin = panel.anchorMax = Vector2.one * 0.5f;
            panel.pivot = Vector2.one * 0.5f;
            panel.sizeDelta = new Vector2(1040, 156);
            panel.anchoredPosition = Vector2.zero;
            panel.localScale = Vector3.one;
        }

        private static void Side(Transform parent, SerializedObject refs, bool player)
        {
            var side = player ? "Player" : "Enemy";
            var prefix = player ? "_player" : "_enemy";
            var container = Rect("m_rect_" + side, parent, new Vector2(378, 138),
                new Vector2(player ? -290 : 290, 0));
            var info = Rect("m_img_InfoBackground", container, new Vector2(252, 106),
                new Vector2(player ? 62 : -62, 0));
            Image(info, Sprite("Battle_playerInfoBG"));
            var name = Label("m_text_Name", info, player ? "美味大厨" : "料理守护者", 26,
                new Vector2(188, 36), new Vector2(player ? 16 : -16, 24), Color.white);
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 18;
            name.resizeTextMaxSize = 26;
            Bind(refs, prefix + "Name", name);
            var trophy = Rect("m_img_Trophy", info, new Vector2(38, 38), new Vector2(-53, -23));
            var graphic = trophy.gameObject.AddComponent<BattleInfoTrophyGraphic>();
            graphic.color = Gold;
            graphic.raycastTarget = false;
            Bind(refs, prefix + "Trophies", Label("m_text_Trophies", info, "2500", 29,
                new Vector2(118, 42), new Vector2(28, -23), player ? Color.white : Gold));

            var avatar = Rect("m_rect_Avatar", container, new Vector2(130, 130),
                new Vector2(player ? -64 : 64, 0));
            var circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Image(avatar, circle).color = new Color(0.76f, 0.49f, 0.19f);
            Image(Rect("GoldRim", avatar, new Vector2(123, 123), Vector2.zero), circle).color = new Color(1f, 0.86f, 0.47f);
            var inner = Rect("m_rect_AvatarMask", avatar, new Vector2(110, 110), Vector2.zero);
            Image(inner, circle).color = new Color(1f, 0.97f, 0.87f);
            inner.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var icon = Image(Rect("m_img_Avatar", inner, new Vector2(104, 104), Vector2.zero), null);
            icon.preserveAspect = true;
            icon.enabled = false;
            Bind(refs, prefix + "Avatar", icon);
            Bind(refs, prefix + "AvatarPlaceholder", Label("m_text_AvatarPlaceholder", inner,
                player ? "我" : "敌", 52, new Vector2(100, 100), Vector2.zero,
                player ? Ink : new Color(0.1f, 0.29f, 0.42f)));
        }
        private static void Bind(SerializedObject refs, string name, UnityEngine.Object value)
        { refs.FindProperty(name).objectReferenceValue = value; }
        private static Sprite Sprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SPRITES + name + ".png");
            if (sprite == null) throw new InvalidOperationException("缺少 Sprite：" + name);
            return sprite;
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rect = go.GetComponent<RectTransform>();
            if (parent != null) rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private static Image Image(RectTransform rect, Sprite sprite)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }
        private static Text Label(string name, Transform parent, string content, int size,
            Vector2 rectSize, Vector2 position, Color color)
        {
            var text = Rect(name, parent, rectSize, position).gameObject.AddComponent<Text>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = false;
            text.raycastTarget = false;
            ModernUIFontTheme.Apply(text, true);
            return text;
        }
    }
}
