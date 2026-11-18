using System;
using System.IO;
using GameLogic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FreshGuard.Editor
{
    /// <summary>在现有战斗信息栏原位接入 A 版分件，保留主窗口、按钮与屏幕适配绑定。</summary>
    public static class BattleInfoPresentationSetup
    {
        public const string PrefabPath = "Assets/AssetRaw/Actor/Prefabs/UIPrefabs/BattleInfo/BattleInfoUI.prefab";
        public const string SpriteFolder = "Assets/AssetRaw/UI/UISprites/BattleInfoA";
        // 保留原 1040×156 信息区，按统一比例容纳 1080×168 原画，避免改变双方棋盘的布局。
        private const float ArtScale = 156f / 168f;

        public static void ImportSprites(string sourceFolder)
        {
            Directory.CreateDirectory(SpriteFolder);
            foreach (var source in new[] {
                "Backgrounds/BattleInfo_MainPanel.png", "Backgrounds/BattleInfo_Nameplate.png",
                "Backgrounds/BattleInfo_TimerPlate.png", "Backgrounds/BattleInfo_FriendlyIdentityBadge.png",
                "Backgrounds/BattleInfo_EnemyIdentityBadge.png", "Backgrounds/BattleInfo_FriendlyBuffTray6.png",
                "Backgrounds/BattleInfo_EnemyBuffTray6.png", "Buttons/BattleInfo_ExitButton.png",
                "Buttons/BattleInfo_SettingsButton.png", "Buttons/BattleInfo_ButtonBase.png",
                "Icons/BattleInfo_ExitIcon.png", "Icons/BattleInfo_SettingsIcon.png", "Icons/BattleInfo_TrophyIcon.png" })
            {
                var destination = SpriteFolder + "/" + Path.GetFileName(source);
                WriteTrimmedSprite(Path.Combine(sourceFolder, source), destination);
                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(destination);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        [MenuItem("FreshGuard/Battle Info/Apply A Presentation")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非运行且编译完成时更新战斗信息栏。");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var panel = (RectTransform)root.transform.Find("m_rect_BattleInfo");
                var view = root.GetComponent<BattleInfoView>();
                if (panel == null || view == null) throw new InvalidOperationException("缺少战斗信息面板或展示组件。");
                var oldBackground = panel.GetComponent<Image>();
                if (oldBackground != null) oldBackground.enabled = false;
                var background = Rect(panel, "m_img_MainPanel", new Vector2(848, 135) * ArtScale, new Vector2(0, .5f) * ArtScale);
                Graphic(background, "BattleInfo_MainPanel");
                background.SetAsFirstSibling();
                var refs = new SerializedObject(view);
                Side(panel, refs, true);
                Side(panel, refs, false);
                var battle = (RectTransform)panel.Find("m_rect_Battle");
                Layout(battle, new Vector2(168, 115), new Vector2(0, 1.5f) * ArtScale);
                battle.localScale = Vector3.one * ArtScale;
                Graphic(battle, "BattleInfo_TimerPlate");
                Style(battle.Find("m_text_Wave").GetComponent<Text>(), new Vector2(142, 38), new Vector2(0, 32), 24, new Color32(49, 77, 93, 255));
                var elapsed = battle.Find("m_text_ElapsedTime").GetComponent<Text>();
                Style(elapsed, new Vector2(144, 52), new Vector2(0, -4), 34, new Color32(49, 77, 93, 255));
                elapsed.resizeTextMinSize = 20;
                elapsed.text = "00:00";
                Button(panel, "m_btn_Leave", "BattleInfo_ExitIcon", -482);
                Button(panel, "m_btn_Settings", "BattleInfo_SettingsIcon", 482);
                BindQualitySprites(refs);
                refs.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                    throw new InvalidOperationException("战斗信息栏保存失败。");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("FreshGuard/Battle Info/Apply Shop Buff Appearance")]
        public static void ApplyBuffAppearance()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非运行且编译完成时更新助力图标。");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var panel = root.transform.Find("m_rect_BattleInfo");
                var refs = new SerializedObject(root.GetComponent<BattleInfoView>());
                Buffs((RectTransform)panel.Find("m_rect_Player"), refs, "_player");
                Buffs((RectTransform)panel.Find("m_rect_Enemy"), refs, "_enemy");
                BindQualitySprites(refs);
                refs.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                    throw new InvalidOperationException("战斗助力图标保存失败。");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void BindQualitySprites(SerializedObject refs)
        {
            var sprites = refs.FindProperty("_buffQualityFrames");
            sprites.arraySize = 4;
            for (var i = 0; i < sprites.arraySize; i++)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(MysteriousStoreBuffFrameSetup.SpriteFolder +
                    "Shop_BuffFrame" + (MysteriousStoreQuality)i + ".png");
                if (sprite == null) throw new InvalidOperationException("缺少神秘商店品质底框：" + (MysteriousStoreQuality)i);
                sprites.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
            }
        }

        private static void Side(RectTransform panel, SerializedObject refs, bool player)
        {
            var side = (RectTransform)panel.Find(player ? "m_rect_Player" : "m_rect_Enemy");
            var prefix = player ? "_player" : "_enemy";
            Layout(side, new Vector2(316, 168), new Vector2(player ? -252 : 252, 0) * ArtScale);
            side.localScale = Vector3.one * ArtScale;
            var info = (RectTransform)side.Find("m_img_InfoBackground");
            Layout(info, new Vector2(316, 54), new Vector2(0, 35));
            Graphic(info, "BattleInfo_Nameplate");
            var name = info.Find("m_text_Name").GetComponent<Text>();
            Style(name, new Vector2(117, 40), new Vector2(player ? -32.5f : -87.5f, 0), 22, Color.white);
            name.resizeTextMinSize = 14;
            var trophy = (RectTransform)info.Find("m_img_Trophy");
            Layout(trophy, new Vector2(23, 25), new Vector2(player ? 42 : -13, 0));
            var oldTrophy = trophy.GetComponent<BattleInfoTrophyGraphic>();
            if (oldTrophy != null) UnityEngine.Object.DestroyImmediate(oldTrophy);
            Graphic(trophy, "BattleInfo_TrophyIcon").preserveAspect = true;
            Style(info.Find("m_text_Trophies").GetComponent<Text>(), new Vector2(78, 40), new Vector2(player ? 95 : 40, 0), 24, Color.white);
            side.Find("m_rect_Avatar").gameObject.SetActive(false);
            var badge = Rect(side, "m_img_IdentityBadge", new Vector2(66, 66), new Vector2(player ? -127 : 127, 37));
            Graphic(badge, player ? "BattleInfo_FriendlyIdentityBadge" : "BattleInfo_EnemyIdentityBadge").preserveAspect = true;
            badge.SetAsLastSibling();

            Buffs(side, refs, prefix);
        }

        private static void Buffs(RectTransform side, SerializedObject refs, string prefix)
        {
            var tray = Rect(side, "m_rect_BuffSlots", new Vector2(316, 61), new Vector2(0, -28.5f));
            var oldTray = tray.GetComponent<Image>();
            if (oldTray != null) oldTray.enabled = false;
            var trayBackground = tray.Find("m_img_TrayBackground");
            if (trayBackground != null) trayBackground.gameObject.SetActive(false);
            var frames = refs.FindProperty(prefix + "BuffFrames");
            var icons = refs.FindProperty(prefix + "BuffIcons");
            var levels = refs.FindProperty(prefix + "BuffLevels");
            frames.arraySize = icons.arraySize = levels.arraySize = BattleInfoView.BUFF_CAPACITY;
            for (var i = 0; i < BattleInfoView.BUFF_CAPACITY; i++)
            {
                var slot = Rect(tray, "m_rect_Buff" + i, new Vector2(48, 48), new Vector2(-122.5f + 49f * i, 0));
                var frameRect = Rect(slot, "m_img_QualityFrame", new Vector2(48, 48), Vector2.zero);
                var frame = frameRect.GetComponent<Image>() ?? frameRect.gameObject.AddComponent<Image>();
                frame.sprite = null;
                frame.enabled = false;
                frame.color = Color.white;
                frame.preserveAspect = true;
                frame.raycastTarget = false;
                frameRect.SetAsFirstSibling();
                // 与神秘商店一致：前景图标在 512 方形品质框内每边内缩 30 像素。
                var icon = Rect(slot, "m_img_Icon", Vector2.one * (48f * (1f - 60f / 512f)), Vector2.zero).GetComponent<Image>();
                if (icon == null) icon = slot.Find("m_img_Icon").gameObject.AddComponent<Image>();
                icon.sprite = null;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = false;
                var levelRect = Rect(slot, "m_text_Level", new Vector2(38, 24), Vector2.zero);
                var level = levelRect.GetComponent<Text>() ?? levelRect.gameObject.AddComponent<Text>();
                Style(level, new Vector2(38, 24), new Vector2(-3, 3), 15, Color.white);
                levelRect.anchorMin = levelRect.anchorMax = levelRect.pivot = new Vector2(1, 0);
                level.alignment = TextAnchor.LowerRight;
                level.alignByGeometry = true;
                level.resizeTextMinSize = 10;
                var outline = level.gameObject.AddComponent<Outline>();
                outline.effectColor = Color.black;
                outline.effectDistance = new Vector2(.8f, -.8f);
                level.text = string.Empty;
                level.gameObject.SetActive(false);
                frames.GetArrayElementAtIndex(i).objectReferenceValue = frame;
                icons.GetArrayElementAtIndex(i).objectReferenceValue = icon;
                levels.GetArrayElementAtIndex(i).objectReferenceValue = level;
            }
        }

        [MenuItem("FreshGuard/Battle Info/Apply Separate Button Icons")]
        public static void ApplyButtonIcons()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非运行且编译完成时更新战斗信息栏按钮。");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var panel = (RectTransform)root.transform.Find("m_rect_BattleInfo");
                Button(panel, "m_btn_Leave", "BattleInfo_ExitIcon", -482);
                Button(panel, "m_btn_Settings", "BattleInfo_SettingsIcon", 482);
                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                    throw new InvalidOperationException("战斗信息栏按钮保存失败。");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void Button(RectTransform panel, string name, string iconSprite, float x)
        {
            var button = (RectTransform)panel.Find(name);
            Layout(button, new Vector2(116, 130), new Vector2(x, 0) * ArtScale);
            button.localScale = Vector3.one * ArtScale;
            foreach (Transform child in button) child.gameObject.SetActive(false);
            var graphic = Graphic(button, "BattleInfo_ButtonBase");
            graphic.raycastTarget = true;
            // 背景与点击区域保留原尺寸，两个前景图标使用相同边长并各自保持宽高比。
            var icon = Rect(button, "m_img_Icon", new Vector2(84, 84), Vector2.zero);
            icon.gameObject.SetActive(true);
            Graphic(icon, iconSprite).preserveAspect = true;
            icon.SetAsLastSibling();
            button.GetComponent<UnityEngine.UI.Button>().targetGraphic = graphic;
            var group = button.GetComponent<CanvasGroup>() ?? button.gameObject.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = group.ignoreParentGroups = true;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 size, Vector2 position)
        {
            var rect = parent.Find(name) as RectTransform;
            if (rect == null)
            {
                rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                rect.gameObject.layer = parent.gameObject.layer;
                rect.SetParent(parent, false);
            }
            Layout(rect, size, position);
            return rect;
        }

        private static void Layout(RectTransform rect, Vector2 size, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localScale = Vector3.one;
        }

        private static Image Graphic(RectTransform rect, string sprite)
        {
            var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFolder + "/" + sprite + ".png");
            if (image.sprite == null) throw new InvalidOperationException("缺少战斗信息栏分件：" + sprite);
            image.enabled = true;
            image.color = Color.white;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
            return image;
        }

        private static void Style(Text text, Vector2 size, Vector2 position, int fontSize, Color color)
        {
            foreach (var shadow in text.GetComponents<Shadow>()) UnityEngine.Object.DestroyImmediate(shadow);
            Layout(text.rectTransform, size, position);
            text.font = ModernUIFontTheme.LoadFont(true);
            text.fontStyle = FontStyle.Normal;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = fontSize - 4;
            text.resizeTextMaxSize = fontSize;
            text.raycastTarget = false;
        }

        private static void WriteTrimmedSprite(string source, string destination)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D trimmed = null;
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(source))) throw new InvalidOperationException("无法读取分件：" + source);
                var pixels = texture.GetPixels32();
                var minX = texture.width;
                var minY = texture.height;
                var maxX = -1;
                var maxY = -1;
                for (var y = 0; y < texture.height; y++)
                    for (var x = 0; x < texture.width; x++)
                        if (pixels[y * texture.width + x].a != 0)
                        {
                            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                        }
                if (maxX < minX) throw new InvalidOperationException("分件没有可见像素：" + source);
                var width = maxX - minX + 1;
                var height = maxY - minY + 1;
                var cropped = new Color32[width * height];
                for (var y = 0; y < height; y++) Array.Copy(pixels, (minY + y) * texture.width + minX, cropped, y * width, width);
                trimmed = new Texture2D(width, height, TextureFormat.RGBA32, false);
                trimmed.SetPixels32(cropped);
                trimmed.Apply();
                File.WriteAllBytes(destination, trimmed.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
                if (trimmed != null) UnityEngine.Object.DestroyImmediate(trimmed);
            }
        }
    }
}
