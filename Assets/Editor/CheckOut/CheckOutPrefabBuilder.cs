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
    /// <summary>仅通过 Editor API 制作结算 Prefab，重复执行替换同一资源。</summary>
    public static class CheckOutPrefabBuilder
    {
        public const string PrefabPath = "Assets/AssetRaw/UI/CheckOutUI.prefab";
        private const string Sprites = "Assets/AssetRaw/Actor/CheckOutScreen/Sprites/";
        private const string CommonSprites = "Assets/AssetRaw/Actor/UISprites/General/";
        private static readonly Vector2 DesignSize = new Vector2(750, 1334);

        [MenuItem("FreshGuard/CheckOut/Build Prefab")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非运行且编译完成时制作结算 Prefab。");
            var root = CreateWindow();
            try { PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[CheckOut] 结算 Prefab 已保存：" + PrefabPath);
        }

        private static GameObject CreateWindow()
        {
            var root = Rect("CheckOutUI", null, DesignSize, Vector2.zero);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.sizeDelta = Vector2.zero;
            root.gameObject.AddComponent<Canvas>().overrideSorting = true;
            root.gameObject.AddComponent<GraphicRaycaster>();
            var blocker = Image("m_img_InputBlocker", root, null, DesignSize, Vector2.zero);
            Stretch(blocker.rectTransform);
            blocker.color = Color.clear;
            blocker.raycastTarget = true;
            Theme(root, "m_go_Victory", true);
            Theme(root, "m_go_Defeat", false);
            var content = Rect("m_rect_Content", root, DesignSize, Vector2.zero);
            Role(content, "m_rect_Player", "Res_playerBG", "玩家", new Vector2(-210, 72));
            Role(content, "m_rect_Opponent", "Res_enemyBG", "敌方", new Vector2(210, 72));
            CommonImage("m_img_VictoryVS", content, "U_winVS", new Vector2(170, 144), new Vector2(0, 60));
            Image("m_img_DefeatVS", content, "Res_loseVS", new Vector2(190, 160), new Vector2(0, 60)).gameObject.SetActive(false);
            var rewards = Rect("m_rect_Rewards", content, new Vector2(650, 215), new Vector2(0, -292));
            Image("m_img_VictoryBG", rewards, "Res_winResourceBGSingle", new Vector2(650, 215), Vector2.zero);
            Image("m_img_DefeatBG", rewards, "Res_loseResourceBGSingle", new Vector2(650, 215), Vector2.zero).gameObject.SetActive(false);
            var ink = new Color(0.39f, 0.17f, 0.04f);
            Label("m_text_Title", rewards, "获得奖励", 28, new Vector2(560, 45), new Vector2(0, 68), ink);
            var reward = Rect("m_item_Reward", rewards, new Vector2(270, 106), new Vector2(0, -18));
            Image("m_img_Icon", reward, "zhuliCoin", new Vector2(72, 72), new Vector2(-78, 0));
            Label("m_text_Name", reward, "助力币", 24, new Vector2(155, 40), new Vector2(43, 20), ink);
            Label("m_text_Amount", reward, "—", 30, new Vector2(155, 40), new Vector2(43, -22), ink);
            BusinessButton(content, "m_btn_DoubleReward", "双倍奖励", "U_yellowButton", new Vector2(-166, -487), ink);
            BusinessButton(content, "m_btn_ReturnMain", "返回主界面", "U_blueButton", new Vector2(166, -487), Color.white);
            return root.gameObject;
        }

        private static void Theme(Transform root, string name, bool victory)
        {
            var theme = Rect(name, root, DesignSize, Vector2.zero);
            Stretch(theme);
            var bg = Image("m_img_Background", theme, victory ? "Res_winBG" : "Res_loseBG", DesignSize, Vector2.zero);
            Stretch(bg.rectTransform);
            bg.preserveAspect = false;
            Image("m_img_Title", theme, victory ? "Res_win" : "Res_lose", new Vector2(610, 300), new Vector2(0, 412));
            theme.gameObject.SetActive(victory);
        }

        private static void Role(Transform parent, string name, string sprite, string displayName, Vector2 position)
        {
            var role = Rect(name, parent, new Vector2(245, 340), position);
            Image("m_img_Background", role, sprite, new Vector2(245, 340), Vector2.zero);
            var portrait = Image("m_img_Avatar", role, null, new Vector2(130, 130), new Vector2(0, 65));
            portrait.color = new Color(1, 1, 1, 0.22f);
            Label("AvatarMark", portrait.transform, displayName == "玩家" ? "P" : "E", 56, new Vector2(120, 120), Vector2.zero, Color.white);
            Label("m_text_Name", role, displayName, 30, new Vector2(220, 50), new Vector2(0, -30), Color.white);
            Label("m_text_Cups", role, "—", 30, new Vector2(210, 60), new Vector2(0, -111), Color.white);
            var crown = Image("m_img_Crown", role, "Res_winCrown", new Vector2(90, 83), new Vector2(-88, 150));
            Label("Title", crown.transform, "胜利", 20, new Vector2(76, 32), new Vector2(0, -18), new Color(0.51f, 0.24f, 0.02f));
            crown.gameObject.SetActive(displayName == "玩家");
        }

        private static void BusinessButton(Transform parent, string name, string title, string sprite, Vector2 position, Color textColor)
        {
            var image = CommonImage(name, parent, sprite, new Vector2(300, 120), position);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.interactable = false;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.disabledColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            button.colors = colors;
            Label("m_text_Title", image.transform, title, 31, new Vector2(280, 90), new Vector2(0, 7), textColor);
        }

        private static Image CommonImage(string name, Transform parent, string sprite, Vector2 size, Vector2 position)
        {
            var image = Rect(name, parent, size, position).gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CommonSprites + sprite + ".png");
            if (image.sprite == null) throw new InvalidOperationException("通用UI图片缺失：" + sprite);
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            if (parent != null) rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
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

        private static Image Image(string name, Transform parent, string sprite, Vector2 size, Vector2 position)
        {
            var image = Rect(name, parent, size, position).gameObject.AddComponent<Image>();
            image.sprite = sprite == null ? AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd") :
                AssetDatabase.LoadAssetAtPath<Sprite>(Sprites + sprite + ".png");
            if (sprite != null && image.sprite == null) throw new InvalidOperationException("结算图片缺失：" + sprite);
            image.type = sprite == null ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = sprite != null;
            image.raycastTarget = false;
            return image;
        }

        private static Text Label(string name, Transform parent, string value, int fontSize, Vector2 size, Vector2 position, Color color)
        {
            var text = Rect(name, parent, size, position).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        [MenuItem("FreshGuard/CheckOut/Preview Victory")]
        public static void PreviewVictory() { RenderPreview(true, Path.Combine(Path.GetTempPath(), "freshguard-checkout-victory.png")); }

        [MenuItem("FreshGuard/CheckOut/Preview Defeat")]
        public static void PreviewDefeat() { RenderPreview(false, Path.Combine(Path.GetTempPath(), "freshguard-checkout-defeat.png")); }

        public static void RenderPreview(bool victory, string path)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            Camera camera = null;
            RenderTexture target = null;
            Texture2D capture = null;
            Font font = null;
            var previous = RenderTexture.active;
            try
            {
                root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                SceneManager.MoveGameObjectToScene(root, scene);
                var cameraObject = new GameObject("CheckOutPreviewCamera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = DesignSize.y / 2f;
                camera.transform.position = new Vector3(0, 0, -10);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << 5;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                target = new RenderTexture(750, 1334, 24);
                camera.targetTexture = target;
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                // 预览只使用 Prefab 节点，不启动业务或全局 UI 模块。
                SetTheme(root.transform, victory);
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 30);
                foreach (var text in root.GetComponentsInChildren<Text>(true)) text.font = font;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                capture = new Texture2D(750, 1334, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, 750, 1334), 0, 0);
                capture.Apply();
                File.WriteAllBytes(path, capture.EncodeToPNG());
                Debug.Log("[CheckOut] 预览已保存：" + path);
            }
            finally
            {
                RenderTexture.active = previous;
                if (camera != null) camera.targetTexture = null;
                if (capture != null) Object.DestroyImmediate(capture);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (font != null) Object.DestroyImmediate(font);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void SetTheme(Transform root, bool victory)
        {
            root.Find("m_go_Victory").gameObject.SetActive(victory);
            root.Find("m_go_Defeat").gameObject.SetActive(!victory);
            var content = root.Find("m_rect_Content");
            content.Find("m_rect_Player/m_img_Crown").gameObject.SetActive(victory);
            content.Find("m_rect_Opponent/m_img_Crown").gameObject.SetActive(!victory);
            content.Find("m_img_VictoryVS").gameObject.SetActive(victory);
            content.Find("m_img_DefeatVS").gameObject.SetActive(!victory);
            content.Find("m_rect_Rewards/m_img_VictoryBG").gameObject.SetActive(victory);
            content.Find("m_rect_Rewards/m_img_DefeatBG").gameObject.SetActive(!victory);
            content.Find("m_rect_Rewards/m_text_Title").GetComponent<Text>().color = victory ? new Color(0.39f, 0.17f, 0.04f) : new Color(0.19f, 0.12f, 0.36f);
        }
    }
}
