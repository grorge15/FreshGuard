using System;
using System.IO;
using GameLogic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FreshGuard.Editor
{
    /// <summary>在既有商店资源上装配老虎机，保留绑定、拖拽幽灵和暴走条。</summary>
    public static class SlotMachineShopPrefabBuilder
    {
        private const string SHOP = "Assets/AssetRaw/UI/Shop/BattleShopUI.prefab";
        private const string COMBAT = "Assets/AssetRaw/UI/MainCombat/MainCombatUI.prefab";
        private const string ART = "Assets/AssetRaw/Actor/UISprites/Battle/";
        private static readonly Vector2 Design = new Vector2(1056, 324);
        private static readonly Vector2 Machine = new Vector2(600, 289.49f);
        private static readonly Vector2 MachinePosition = new Vector2((Design.x - Machine.x) * 0.5f, -(Design.y - Machine.y) * 0.5f);
        private static readonly Vector2 LeverSize = new Vector2(253.44f, 174.7f);
        private const float LeverScale = 0.75f;
        private static readonly Color Cream = new Color32(255, 246, 222, 255);
        private static readonly Color Ink = new Color32(36, 73, 92, 255);

        [MenuItem("FreshGuard/Shop/Build Slot Machine Shop")]
        public static void Build()
        {
            if (Application.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行并等待编译完成。");
            ImportSprite(ART + "laohuji.png");
            ImportSprite(ART + "yaogan.png");
            var root = PrefabUtility.LoadPrefabContents(SHOP);
            try
            {
                var panel = (RectTransform)root.transform.Find("m_rect_ShopPanel");
                Stretch(panel);
                panel.GetComponent<Image>().color = Color.clear;
                var rim = panel.Find("TopRim");
                if (rim != null) rim.gameObject.SetActive(false);
                var oldMachine = panel.Find("SlotMachine");
                if (oldMachine != null) UnityEngine.Object.DestroyImmediate(oldMachine.gameObject);
                var machine = Rect("SlotMachine", panel, Machine, MachinePosition, new Vector2(0, 1), new Vector2(0, 1));
                machine.SetAsFirstSibling();
                Picture(machine, Sprite(ART + "laohuji.png"));
                var palette = Rect("ReelPalette", machine, Vector2.zero, Vector2.zero);
                foreach (var stem in new[] { "Cannon", "Frost", "Impact", "Piercing", "Poison", "RapidFire", "Scatter", "Tractor" })
                {
                    var icon = "Robot" + stem + "Icon";
                    var card = Rect(icon, palette, Vector2.one * 180, Vector2.zero);
                    var baseImage = Picture(card, Sprite("Assets/AssetRaw/Actor/Robots/Icons/" + icon + ".png"));
                    baseImage.color = RobotLevelStyle.ColorForLevel(1);
                    var frame = Rect("m_img_LevelFrame", card, Vector2.zero, Vector2.zero);
                    Stretch(frame);
                    Picture(frame, Sprite("Assets/AssetRaw/Actor/Robots/LevelFrames/Robot" + stem + "LevelFrame.png")).color = RobotLevelStyle.FrameColorForLevel(1);
                    var portrait = Rect("m_img_RobotPortrait", card, Vector2.zero, Vector2.zero);
                    Stretch(portrait);
                    Picture(portrait, Sprite("Assets/AssetRaw/Actor/Robots/Portraits/Robot" + stem + "Portrait.png"));
                }
                palette.gameObject.SetActive(false);
                for (var i = 0; i < ShopRules.OfferBatchCount; i++)
                {
                    var window = Rect("Reel_" + i, machine, new Vector2(Machine.x * 0.24f, Machine.y * 0.465f),
                        new Vector2(Machine.x * (0.101f + i * 0.277f), -Machine.y * 0.284f), new Vector2(0, 1), new Vector2(0, 1));
                    window.gameObject.AddComponent<RectMask2D>();
                    Rect("Strip", window, window.sizeDelta, Vector2.zero, new Vector2(0.5f, 1), new Vector2(0.5f, 1));
                    window.gameObject.SetActive(false);
                }
                var offers = (RectTransform)panel.Find("m_tf_Offers");
                Place(offers, Machine, MachinePosition, new Vector2(0, 1), new Vector2(0, 1));
                var layout = offers.GetComponent<HorizontalLayoutGroup>();
                if (layout != null) UnityEngine.Object.DestroyImmediate(layout);
                var template = (RectTransform)offers.Find("m_item_Template");
                if (template.GetComponent<CanvasGroup>() == null) template.gameObject.AddComponent<CanvasGroup>();
                Place(template, new Vector2(Machine.x * 0.24f, Machine.y * 0.568f),
                    new Vector2(Machine.x * 0.101f, -Machine.y * 0.284f), new Vector2(0, 1), new Vector2(0, 1));
                var iconRect = (RectTransform)template.Find("m_img_RobotIcon");
                Place(iconRect, new Vector2(Machine.x * 0.24f, Machine.y * 0.465f) * 0.91f,
                    new Vector2(0, -Machine.y * 0.465f * 0.5f), new Vector2(0.5f, 1), Vector2.one * 0.5f);
                var priceY = -Machine.y * (0.465f + 0.063f);
                var price = template.Find("m_text_Price").GetComponent<Text>();
                Place(price.rectTransform, new Vector2(72, 42), new Vector2(17, priceY), new Vector2(0.5f, 1), Vector2.one * 0.5f);
                Style(price, 32);
                var priceCoin = template.Find("m_img_PriceCoin").GetComponent<Image>();
                Place(priceCoin.rectTransform, Vector2.one * 30, new Vector2(-24, priceY), new Vector2(0.5f, 1), Vector2.one * 0.5f);
                priceCoin.sprite = Sprite(ART + "energyCoin.png");
                priceCoin.color = Color.white;
                priceCoin.preserveAspect = true;

                var lever = (RectTransform)panel.Find("m_btn_Refresh");
                ConfigureLeverLayout(lever);
                var leverImage = lever.GetComponent<Image>();
                leverImage.sprite = Sprite(ART + "yaogan.png");
                leverImage.type = Image.Type.Simple;
                leverImage.color = Color.white;
                leverImage.preserveAspect = true;
                leverImage.raycastTarget = true;
                var button = lever.GetComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                var knobAnchor = new Vector2(0.68f, 0.56f);
                var title = lever.Find("Title").GetComponent<Text>();
                Place(title.rectTransform, new Vector2(110, 42), new Vector2(0, -28), knobAnchor, Vector2.one * 0.5f);
                Style(title, 29);
                title.text = "刷新";
                var refreshPrice = lever.Find("m_text_Price").GetComponent<Text>();
                Place(refreshPrice.rectTransform, new Vector2(70, 63), new Vector2(16, 18), knobAnchor, Vector2.one * 0.5f);
                Style(refreshPrice, 56);
                var refreshCoin = lever.Find("RefreshCoin") as RectTransform;
                if (refreshCoin == null) refreshCoin = Rect("RefreshCoin", lever, Vector2.one * 36, Vector2.zero);
                Place(refreshCoin, Vector2.one * 36, new Vector2(-35, 18), knobAnchor, Vector2.one * 0.5f);
                Picture(refreshCoin, Sprite(ART + "energyCoin.png"));
                panel.Find("m_text_Message").gameObject.SetActive(false);
                ConfigureReadability(root);
                PrefabUtility.SaveAsPrefabAsset(root, SHOP);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            ConfigureCombatLayout();
            AssetDatabase.SaveAssets();
            Debug.Log("[Slot Shop] 已装配老虎机、摇杆和连续滚动窗口，保留现有能源币及机器人等级底板。");
        }

        private static void ConfigureCombatLayout()
        {
            var combat = PrefabUtility.LoadPrefabContents(COMBAT);
            try
            {
                var shop = (RectTransform)combat.transform.Find("BattleShopUI");
                foreach (var path in new[] { "m_rect_ShopPanel/SlotMachine", "m_rect_ShopPanel/m_tf_Offers" })
                {
                    var rect = new SerializedObject(shop.Find(path));
                    PrefabUtility.RevertPropertyOverride(rect.FindProperty("m_AnchoredPosition"), InteractionMode.AutomatedAction);
                }
                Place(shop, Design, new Vector2(0, 12), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
                Stretch((RectTransform)shop.Find("m_rect_ShopPanel"));
                PrefabUtility.RecordPrefabInstancePropertyModifications(shop);
                PrefabUtility.RecordPrefabInstancePropertyModifications(shop.Find("m_rect_ShopPanel"));
                PrefabUtility.SaveAsPrefabAsset(combat, COMBAT);
            }
            finally { PrefabUtility.UnloadPrefabContents(combat); }
        }

        [MenuItem("FreshGuard/Shop/Apply Compact Slot Shop Layout")]
        public static void ApplyCompactLayout()
        {
            ImproveReadability();
            ConfigureCombatLayout();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("FreshGuard/Shop/Improve Slot Shop Readability")]
        public static void ImproveReadability()
        {
            if (Application.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行并等待编译完成。");
            var root = PrefabUtility.LoadPrefabContents(SHOP);
            try
            {
                ConfigureReadability(root);
                PrefabUtility.SaveAsPrefabAsset(root, SHOP);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureReadability(GameObject root)
        {
            var panel = (RectTransform)root.transform.Find("m_rect_ShopPanel");
            ConfigureMachineLayout(panel);
            Panel(panel, new Color32(164, 132, 95, 255), true);
            var backdrop = EnsureRect("ShopBackdrop", panel);
            Stretch(backdrop);
            backdrop.offsetMin = Vector2.one * 5;
            backdrop.offsetMax = Vector2.one * -5;
            Panel(backdrop, new Color32(237, 219, 187, 255));
            backdrop.SetAsFirstSibling();
            var edge = EnsureRect("TopHighlight", backdrop);
            Place(edge, new Vector2(-24, 4), new Vector2(0, -4), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
            edge.anchorMin = new Vector2(0, 1);
            edge.anchorMax = Vector2.one;
            Panel(edge, new Color32(255, 243, 216, 255));

            var balance = (RectTransform)panel.Find("EnergyBalance");
            Place(balance, new Vector2(158, 56), new Vector2(-32, MachinePosition.y - 4), Vector2.one, Vector2.one);
            balance.GetComponent<Image>().color = new Color32(180, 151, 111, 255);
            var balanceIcon = (RectTransform)balance.Find("m_img_EnergyCoin");
            Place(balanceIcon, Vector2.one * 42, new Vector2(-50, 0));
            balanceIcon.GetComponent<Image>().sprite = Sprite(ART + "energyCoin.png");
            var coins = panel.Find("m_text_EnergyCoins").GetComponent<Text>();
            Place(coins.rectTransform, new Vector2(100, 52), new Vector2(-36, MachinePosition.y - 6), Vector2.one, Vector2.one);
            Style(coins, 36);

            var template = (RectTransform)panel.Find("m_tf_Offers/m_item_Template");
            var priceY = -Machine.y * (0.465f + 0.063f);
            var badge = template.Find("PriceBadge");
            if (badge != null) UnityEngine.Object.DestroyImmediate(badge.gameObject);
            var price = template.Find("m_text_Price").GetComponent<Text>();
            Place(price.rectTransform, new Vector2(90, 60), new Vector2(20, priceY), new Vector2(0.5f, 1), Vector2.one * 0.5f);
            Style(price, 52);
            var outline = price.GetComponent<Outline>();
            outline.enabled = true;
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(3, -3);
            var coin = (RectTransform)template.Find("m_img_PriceCoin");
            Place(coin, Vector2.one * 46, new Vector2(-43, priceY), new Vector2(0.5f, 1), Vector2.one * 0.5f);
        }

        private static void ConfigureMachineLayout(RectTransform panel)
        {
            var topLeft = new Vector2(0, 1);
            var machine = (RectTransform)panel.Find("SlotMachine");
            Place(machine, Machine, MachinePosition, topLeft, topLeft);
            var offers = (RectTransform)panel.Find("m_tf_Offers");
            Place(offers, Machine, MachinePosition, topLeft, topLeft);
            var template = (RectTransform)offers.Find("m_item_Template");
            Place(template, new Vector2(Machine.x * 0.24f, Machine.y * 0.568f),
                new Vector2(Machine.x * 0.101f, -Machine.y * 0.284f), topLeft, topLeft);
            var windowSize = new Vector2(Machine.x * 0.24f, Machine.y * 0.465f);
            Place((RectTransform)template.Find("m_img_RobotIcon"), windowSize * 0.91f,
                new Vector2(0, -windowSize.y * 0.5f), new Vector2(0.5f, 1), Vector2.one * 0.5f);
            for (var i = 0; i < ShopRules.OfferBatchCount; i++)
            {
                var window = (RectTransform)machine.Find("Reel_" + i);
                Place(window, windowSize, new Vector2(Machine.x * (0.101f + i * 0.277f), -Machine.y * 0.284f), topLeft, topLeft);
                Place((RectTransform)window.Find("Strip"), windowSize, Vector2.zero, new Vector2(0.5f, 1), new Vector2(0.5f, 1));
            }
            ConfigureLeverLayout((RectTransform)panel.Find("m_btn_Refresh"));
        }

        private static void ConfigureLeverLayout(RectTransform lever)
        {
            // 整体缩放使旋钮、文字和货币图标保持比例，旋钮中心对齐右侧货币栏。
            var knobCenter = new Vector2(Design.x - 32 - 158 * 0.5f, MachinePosition.y - Machine.y * 0.54f);
            var knobOffset = new Vector2(LeverSize.x * 0.6f, LeverSize.y * 0.45f) * LeverScale;
            Place(lever, LeverSize, knobCenter - knobOffset, new Vector2(0, 1), new Vector2(0.08f, 0.11f));
            lever.localScale = Vector3.one * LeverScale;
        }

        private static RectTransform EnsureRect(string name, Transform parent)
        {
            var existing = parent.Find(name) as RectTransform;
            return existing != null ? existing : Rect(name, parent, Vector2.zero, Vector2.zero);
        }

        private static void Panel(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.GetComponent<Image>();
            if (image == null) image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            image.color = color;
            image.raycastTarget = raycast;
        }

        private static void ImportSprite(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("缺少老虎机素材", path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static Sprite Sprite(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("图片不是可用的 Sprite：" + path);
            return sprite;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position,
            Vector2? anchor = null, Vector2? pivot = null)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            Place(rect, size, position, anchor, pivot);
            return rect;
        }

        private static void Place(RectTransform rect, Vector2 size, Vector2 position, Vector2? anchor = null, Vector2? pivot = null)
        {
            rect.anchorMin = rect.anchorMax = anchor ?? Vector2.one * 0.5f;
            rect.pivot = pivot ?? Vector2.one * 0.5f;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one * 0.5f;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static Image Picture(RectTransform rect, Sprite sprite)
        {
            var image = rect.GetComponent<Image>();
            if (image == null) image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;
            return image;
        }

        private static void Style(Text text, int fontSize)
        {
            text.fontSize = fontSize;
            text.color = Cream;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = text.GetComponent<Outline>();
            if (outline == null) outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = Ink;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            text.raycastTarget = false;
        }
    }
}
