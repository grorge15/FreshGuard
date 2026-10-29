using System;
using System.IO;
using GameLogic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FreshGuard.Editor
{
    /// <summary>以 720P 示意图坐标制作，整体换算为主菜单的 1080P 设计尺寸。</summary>
    public static class MysteriousStorePrefabBuilder
    {
        public const string PrefabPath = "Assets/AssetRaw/UI/MysteriousStoreWidget.prefab";
        private const string Sprites = "Assets/AssetRaw/Actor/UISprites/MysteriousStore/";
        private const string CoinPath = "Assets/AssetRaw/Actor/UISprites/CheckOutScreen/Sprites/zhuliCoin.png";
        private const string FontPath = "Assets/AssetRaw/Fonts/NotoSansSC-Regular.otf";
        private const float Scale = 1.5f;

        [MenuItem("FreshGuard/Mysterious Store/Build Prefab")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非运行且编译完成时制作神秘商店。");
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = CreateRoot();
                SceneManager.MoveGameObjectToScene(root, scene);
                var widget = new MysteriousStoreWidget();
                widget.Create(new UIBase(), root);
                widget.Bind(MysteriousStorePreviewSession.CreateDefault(), null);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[MysteriousStore] Prefab 已保存：" + PrefabPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static GameObject CreateRoot()
        {
            var root = Rect("MysteriousStoreWidget", null, 720, 1280, 0, 0);
            Stretch(root);
            var shade = Picture("m_img_InputBlocker", root, null, 720, 1280, 0, 0);
            Stretch(shade.rectTransform);
            shade.color = new Color(0, 0, 0, 0.56f);
            shade.raycastTarget = true;
            var content = Rect("m_rect_Content", root, 720, 1280, 0, 0);
            AtImage("m_img_Background", content, "Shop_BG", 573, 779, 360, 617.5f);
            AtText("m_text_Title", content, "神秘商店", 58, 490, 90, 360, 217, new Color(1, 0.72f, 0.06f), 3);
            AtText("QualityGreen", content, "绿", 27, 65, 40, 222, 287);
            AtText("QualityBlue", content, "蓝", 27, 65, 40, 315, 287);
            AtText("QualityPurple", content, "紫", 27, 65, 40, 408, 287);
            AtText("QualityOrange", content, "黄", 27, 65, 40, 501, 287);
            AtButton("m_btn_Close", content, "MysteryShop_closeButton", 80, 80, 623, 234);
            // 余额置于标题上方，不挤占商品的描述和价格区域。
            var wallet = AtImage("Wallet", content, null, 206, 43, 360, 145);
            wallet.color = new Color(0.19f, 0.12f, 0.08f, 0.9f);
            var coin = Picture("WalletCoin", wallet.transform, null, 29, 29, -76, 0);
            coin.sprite = RequiredSprite(CoinPath);
            AtText("m_text_Balance", content, "助力币  120", 22, 170, 38, 380, 145);
            for (var i = 0; i < 3; i++) Offer(content, i, 395 + i * 156);
            AtText("CurrentTitle", content, "当前助力", 29, 400, 45, 360, 816);
            for (var i = 0; i < MysteriousStorePreviewSession.SlotCount; i++) Held(content, i);
            AtText("m_text_Feedback", content, "", 19, 530, 67, 360, 974, new Color(1, 0.9f, 0.52f), 1);
            AtText("ResetHint", content, "跨日后在结算、主动退出战斗或重新启动游戏时\n重置助力，助力币保留。", 20, 640, 70, 360, 1055);
            var template = Picture("m_img_SoldOutTemplate", content, "Shop_soldOutButton", 1, 1, 0, 0);
            template.gameObject.SetActive(false);
            Confirmation(root);
            return root.gameObject;
        }

        private static void Offer(Transform parent, int index, float y)
        {
            var row = At("m_item_Offer" + index, parent, 510, 142, 360, y);
            Icon(Rect("m_rect_Icon", row, 120, 120, -180, 0));
            Label("m_text_Name", row, "示例助力", 25, 191, 38, -17, -39, Color.white, 1.7f, TextAnchor.MiddleLeft);
            Label("m_text_Level", row, "", 14, 191, 26, -17, -13, new Color(1, 0.94f, 0.65f), 0.7f, TextAnchor.MiddleLeft);
            Label("m_text_Description", row, "", 16, 191, 74, -17, 31, Color.black, 0, TextAnchor.MiddleLeft);
            Picture("m_img_Upgrade", row, "Shop_upgradeArrow", 30, 43, -130, -43);
            var button = Button("m_btn_Buy", row, "Shop_buyButtonBG", 158, 90, 160, 0);
            var coin = Picture("m_img_Coin", button.transform, null, 43, 43, -35, 0);
            coin.sprite = RequiredSprite(CoinPath);
            Label("m_text_Price", button.transform, "60", 29, 87, 55, 23, 0, Color.white, 1.5f);
            Label("m_text_SoldOut", button.transform, "已售罄", 30, 145, 65, 0, 0, Color.white, 1.7f).gameObject.SetActive(false);
        }

        private static void Held(Transform parent, int index)
        {
            var root = At("m_item_Held" + index, parent, 72, 76, 161 + index * 80, 895);
            var content = Rect("m_go_Content", root, 72, 76, 0, 0);
            Icon(Rect("m_rect_Icon", content, 68, 68, 0, 0));
            Label("m_text_Level", content, "1级", 18, 65, 28, 4, 24, Color.white, 1.3f, TextAnchor.MiddleRight);
            Button("m_btn_Remove", content, "Shop_removeIcon", 28, 28, 28, -28);
        }

        private static void Confirmation(Transform parent)
        {
            var root = Rect("m_go_RemoveConfirmation", parent, 720, 1280, 0, 0);
            Stretch(root);
            var outside = Button("m_btn_Outside", root, null, 720, 1280, 0, 0);
            Stretch((RectTransform)outside.transform);
            outside.GetComponent<Image>().color = new Color(0, 0, 0, 0.64f);
            var content = Rect("m_rect_Content", root, 720, 1280, 0, 0);
            var panel = AtImage("Panel", content, "Shop_doubleCheckBG", 450, 414, 360, 645);
            panel.raycastTarget = true;
            AtText("m_text_Name", content, "移除示例助力", 34, 405, 72, 360, 512);
            var icon = At("m_rect_Icon", content, 120, 120, 360, 635);
            Icon(icon);
            Label("m_text_Level", icon, "2级", 24, 90, 38, 23, 49, Color.white, 1.5f, TextAnchor.MiddleRight);
            var remove = AtButton("m_btn_Remove", content, "Shop_removeButtonBG", 158, 95, 360, 774);
            Label("Title", remove.transform, "移除", 43, 143, 80, 0, -1, Color.white, 2.6f);
            AtButton("m_btn_Close", content, "MysteryShop_closeButton", 64, 64, 569, 442);
            AtText("OutsideHint", content, "点击空白处退出", 29, 450, 60, 360, 935);
            root.gameObject.SetActive(false);
        }

        private static void Icon(RectTransform root)
        {
            var files = new[] { "Shop_greenBuffIcon", "Shop_blueBuffIcon", "Shop_purpleBuffIcon", "Shop_orangeBuffIcon" };
            for (var i = 0; i < files.Length; i++)
            {
                var icon = Picture("m_img_" + (MysteriousStoreQuality)i, root, "skillIcon/" + files[i],
                    root.sizeDelta.x / Scale, root.sizeDelta.y / Scale, 0, 0);
                icon.gameObject.SetActive(i == 2);
            }
        }

        private static RectTransform Rect(string name, Transform parent, float w, float h, float x, float y)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            if (parent != null) rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(w, h) * Scale;
            rect.anchoredPosition = new Vector2(x, -y) * Scale;
            return rect;
        }
        private static RectTransform At(string n, Transform p, float w, float h, float x, float y) => Rect(n, p, w, h, x - 360, y - 640);
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private static Image Picture(string n, Transform p, string sprite, float w, float h, float x, float y)
        {
            var image = Rect(n, p, w, h, x, y).gameObject.AddComponent<Image>();
            image.sprite = sprite == null ? null : RequiredSprite(Sprites + sprite + ".png");
            image.preserveAspect = sprite != null;
            image.raycastTarget = false;
            return image;
        }
        private static Image AtImage(string n, Transform p, string s, float w, float h, float x, float y) => Picture(n, p, s, w, h, x - 360, y - 640);
        private static Sprite RequiredSprite(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("缺少神秘商店图片：" + path);
            return sprite;
        }
        private static Button Button(string n, Transform p, string s, float w, float h, float x, float y)
        {
            var image = Picture(n, p, s, w, h, x, y);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;
            return button;
        }
        private static Button AtButton(string n, Transform p, string s, float w, float h, float x, float y) => Button(n, p, s, w, h, x - 360, y - 640);
        private static Text Label(string n, Transform p, string value, int size, float w, float h, float x, float y,
            Color color, float outline, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var label = Rect(n, p, w, h, x, y).gameObject.AddComponent<Text>();
            label.font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (label.font == null) throw new InvalidOperationException("缺少中文字体：" + FontPath);
            label.fontSize = Mathf.RoundToInt(size * Scale);
            label.fontStyle = FontStyle.Bold;
            label.text = value;
            label.color = color;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            if (outline > 0)
            {
                var effect = label.gameObject.AddComponent<Outline>();
                effect.effectColor = Color.black;
                effect.effectDistance = new Vector2(outline, -outline) * Scale;
            }
            return label;
        }
        private static Text AtText(string n, Transform p, string value, int size, float w, float h, float x, float y,
            Color? color = null, float outline = 1.5f) => Label(n, p, value, size, w, h, x - 360, y - 640, color ?? Color.white, outline);

        /// <summary>在独立预览场景渲染，不保存或污染当前 Main 场景。</summary>
        public static void Capture(string path, int width = 720, int height = 1280, bool removal = false)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            Texture2D capture = null;
            var previous = RenderTexture.active;
            try
            {
                var canvasObject = new GameObject("StorePreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasObject.layer = 5;
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
                var cameraObject = new GameObject("StorePreviewCamera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = 960;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.65f, 0.65f, 0.65f);
                camera.cullingMask = 1 << 5;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 1;
                var scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0.5f;
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), canvasObject.transform, false);
                var widget = new MysteriousStoreWidget();
                widget.Create(new UIBase(), root);
                widget.Bind(MysteriousStorePreviewSession.CreateDefault(), null);
                if (removal) root.transform.Find("m_rect_Content/m_item_Held1/m_go_Content/m_btn_Remove").GetComponent<Button>().onClick.Invoke();
                target = new RenderTexture(width, height, 24);
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                // RenderTexture 会改变预览 Canvas 的实际尺寸；模拟运行时的下一帧适配。
                widget.RefreshView();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                capture = new Texture2D(width, height, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                capture.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, capture.EncodeToPNG());
                camera.targetTexture = null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (capture != null) Object.DestroyImmediate(capture);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
