using System;
using System.IO;
using System.Linq;
using GameLogic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FreshGuard.Editor
{
    /// <summary>只迁移现有商店的图标区域，保留已编辑的商品、文字和按钮布局。</summary>
    public static class MysteriousStoreBuffFrameSetup
    {
        public const string SpriteFolder = "Assets/AssetRaw/UI/UISprites/MysteriousStore/shopIcon/";

        [MenuItem("FreshGuard/Mysterious Store/Apply Quality Frames")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出播放模式后更新神秘商店。");
            foreach (MysteriousStoreQuality quality in Enum.GetValues(typeof(MysteriousStoreQuality)))
                ImportSprite(SpriteFolder + "Shop_BuffFrame" + quality + ".png");
            ImportSprite(SpriteFolder + "Shop_BuffLevelBadge.png");
            var root = PrefabUtility.LoadPrefabContents(MysteriousStorePrefabBuilder.PrefabPath);
            try
            {
                ConfigureLayout(root);
                PrefabUtility.SaveAsPrefabAsset(root, MysteriousStorePrefabBuilder.PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[MysteriousStore] 商品、六个助力槽及移除弹窗的品质边框已更新。");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void ConfigureLayout(GameObject root)
        {
            foreach (var icon in root.GetComponentsInChildren<RectTransform>(true).Where(t => t.name == "m_rect_Icon").ToArray())
                ConfigureIcon(icon);
            for (var i = 0; i < 3; i++)
                root.transform.Find("m_rect_Content/m_item_Offer" + i + "/m_rect_Icon")
                    .GetComponent<RectTransform>().sizeDelta = new Vector2(204, 204);
            for (var i = 0; i < MysteriousStorePreviewSession.SlotCount; i++)
            {
                var content = root.transform.Find("m_rect_Content/m_item_Held" + i + "/m_go_Content");
                content.Find("m_rect_Icon").GetComponent<RectTransform>().sizeDelta = new Vector2(114, 114);
                ConfigureLevel(content, new Vector2(25, -36), new Vector2(52, 30), 22);
                content.Find("m_btn_Remove").GetComponent<RectTransform>().sizeDelta = new Vector2(33, 33);
                content.gameObject.SetActive(false);
                foreach (MysteriousStoreQuality quality in Enum.GetValues(typeof(MysteriousStoreQuality)))
                    content.Find("m_rect_Icon/m_img_" + quality).gameObject.SetActive(false);
            }
            ConfigureLevel(root.transform.Find("m_go_RemoveConfirmation/m_rect_Content/m_rect_Icon"),
                new Vector2(43, -70), new Vector2(84, 42), 30);
        }

        public static void ConfigureIcon(RectTransform root)
        {
            foreach (MysteriousStoreQuality quality in Enum.GetValues(typeof(MysteriousStoreQuality)))
            {
                var name = "m_img_" + quality;
                var existing = root.Find(name);
                var frame = existing != null ? existing.GetComponent<Image>() : NewImage(name, root);
                frame.sprite = RequireSprite("Shop_BuffFrame" + quality);
                frame.color = Color.white;
                frame.enabled = true;
                frame.preserveAspect = true;
                frame.raycastTarget = false;
                Fill(frame.rectTransform, 0f);
                var foreground = frame.transform.Find("m_img_Icon");
                var image = foreground != null ? foreground.GetComponent<Image>() : NewImage("m_img_Icon", frame.transform);
                Fill(image.rectTransform, 30f / 512f);
                image.sprite = null;
                image.enabled = false;
                image.color = Color.white;
                image.preserveAspect = true;
                image.raycastTarget = false;
                if (existing == null) frame.gameObject.SetActive(false);
            }
        }

        private static void ConfigureLevel(Transform parent, Vector2 position, Vector2 size, int fontSize)
        {
            var level = parent.Find("m_text_Level").GetComponent<Text>();
            var existing = parent.Find("m_img_LevelBadge");
            var badge = existing != null ? existing.GetComponent<Image>() : NewImage("m_img_LevelBadge", parent);
            badge.sprite = RequireSprite("Shop_BuffLevelBadge");
            badge.raycastTarget = false;
            badge.rectTransform.sizeDelta = size;
            badge.rectTransform.anchoredPosition = position;
            badge.transform.SetSiblingIndex(level.transform.GetSiblingIndex());
            level.rectTransform.anchoredPosition = position;
            level.rectTransform.sizeDelta = new Vector2(size.x, Mathf.Max(size.y, Mathf.Ceil(fontSize * 1.4f) + 2f));
            level.fontSize = fontSize;
            level.alignment = TextAnchor.MiddleCenter;
            level.color = Color.white;
        }

        private static void Fill(RectTransform rect, float inset)
        {
            rect.anchorMin = new Vector2(inset, inset);
            rect.anchorMax = new Vector2(1f - inset, 1f - inset);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static Image NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return go.GetComponent<Image>();
        }

        private static Sprite RequireSprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFolder + name + ".png");
            if (sprite == null) throw new InvalidOperationException("缺少商店图标资源：" + name);
            return sprite;
        }

        private static void ImportSprite(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 512;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        public static void ApplyAndCapture()
        {
            Apply();
            var tables = new GameConfig.Tables(name => new Luban.ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            var catalog = AssistConfigAdapter.FromTables(tables);
            MysteriousStoreOfferData Copy(MysteriousStoreOfferData data, MysteriousStoreQuality quality) =>
                new MysteriousStoreOfferData(data.GoodsId, data.BuffId, data.Name, string.Empty,
                    Enumerable.Range(1, data.MaxLevel).Select(data.DescriptionAt).ToArray(), quality,
                    data.PriceAt(1), data.HasLevel, data.MaxLevel, data.IsInstantReward);
            var offers = new[]
            {
                Copy(catalog.Single(o => o.IsInstantReward), MysteriousStoreQuality.Green),
                Copy(catalog.Single(o => o.IconLocation == "Shop_BerserkExpansionBuffIcon"), MysteriousStoreQuality.Orange),
                Copy(catalog.Single(o => o.IconLocation == "Shop_EnergyStarterBuffIcon"), MysteriousStoreQuality.Blue)
            };
            var held = catalog.Where(o => !o.IsInstantReward).Select((o, slot) =>
                new MysteriousStoreHeldBuffData(Copy(o, (MysteriousStoreQuality)(slot % 4)), Math.Min(1 + slot % 3, o.MaxLevel), slot)).ToArray();
            var player = new MainMenuPlayerState(30, TimeSpan.FromMinutes(5), 30, 11560, 2500, DateTimeOffset.UtcNow);
            var session = new MysteriousStorePreviewSession(player, offers, held);
            Action<GameObject> decorate = root =>
            {
                void SetIcon(Transform icon, MysteriousStoreOfferData data)
                {
                    var source = catalog.Single(o => o.GoodsId == data.GoodsId);
                    var image = icon.Find("m_img_" + data.Quality + "/m_img_Icon").GetComponent<Image>();
                    image.sprite = RequireSprite(source.IconLocation);
                    image.enabled = true;
                }
                for (var i = 0; i < offers.Length; i++) SetIcon(root.transform.Find("m_rect_Content/m_item_Offer" + i + "/m_rect_Icon"), offers[i]);
                foreach (var item in held) SetIcon(root.transform.Find("m_rect_Content/m_item_Held" + item.Slot + "/m_go_Content/m_rect_Icon"), item.Buff);
                if (root.transform.Find("m_go_RemoveConfirmation").gameObject.activeSelf)
                    SetIcon(root.transform.Find("m_go_RemoveConfirmation/m_rect_Content/m_rect_Icon"), held[1].Buff);
            };
            var output = Environment.GetEnvironmentVariable("FRESHGUARD_SHOP_PREVIEW");
            if (string.IsNullOrEmpty(output)) output = Path.GetFullPath("Captures/ShopQualityFrames");
            MysteriousStorePrefabBuilder.Capture(Path.Combine(output, "ShopQualityFrames.png"), session: session, prepare: decorate);
            MysteriousStorePrefabBuilder.Capture(Path.Combine(output, "ShopRemoveQualityFrame.png"), removal: true, session: session, prepare: decorate);
            Debug.Log("[MysteriousStore] 品质边框预览已导出：" + output);
        }
    }
}
