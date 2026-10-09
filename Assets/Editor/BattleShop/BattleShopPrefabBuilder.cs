using System;
using GameLogic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace FreshGuard.Editor
{
    /// <summary>通过 Editor API 装配资源；不生成或修补 Unity YAML。</summary>
    public static class BattleShopPrefabBuilder
    {
        private const string FOLDER = "Assets/AssetRaw/UI/Shop";
        private static readonly Color Ink = new Color(0.25f, 0.17f, 0.12f);

        [MenuItem("FreshGuard/Shop/Build Prefabs And Attach Main")]
        public static void Build()
        {
            if (Application.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("必须在编译完成的非运行模式制作商店资源。");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/AssetRaw/Scenes/Main.unity")
                throw new InvalidOperationException("请先打开本工程业务 Main 场景。");
            EnsureFolder(FOLDER);
            var item = Save(BuildItem(), "ShopRobotItem");
            var storage = Save(BuildStorage(), "StorageRobotSlot");
            var ghost = Save(BuildGhost(), "RobotDragGhost");
            Save(BuildWindow(item, storage, ghost), "BattleShopUI");
            AttachMain();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[BattleShop] 4 个 UI Prefab 已保存，Main 已装配；重复执行不会增加场景对象。");
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
        private static void Pin(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
        }
        private static Image Image(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }
        private static Text Label(string name, Transform parent, string content, int size, Vector2 bounds, Vector2 position, Color color)
        {
            var text = Rect(name, parent, bounds, position).gameObject.AddComponent<Text>();
            text.font = UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        private static GameObject BuildItem()
        {
            var root = Rect("ShopRobotItem", null, new Vector2(112, 132), Vector2.zero);
            Image(root, new Color(1f, 0.98f, 0.91f, 0.72f), true);
            root.gameObject.AddComponent<ShopPointerInput>();
            var element = root.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 112;
            element.preferredHeight = 132;
            element.flexibleWidth = 0;
            var icon = Image(Rect("m_img_RobotIcon", root, new Vector2(94, 88), new Vector2(0, 7)), Color.white);
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/AssetRaw/Actor/Robots/Icons/RobotIIcon.png");
            icon.type = UnityEngine.UI.Image.Type.Simple;
            icon.preserveAspect = true;
            Image(Rect("m_img_Quality", root, new Vector2(100, 4), new Vector2(0, -40)), new Color(0.37f, 0.67f, 0.42f));
            Label("m_text_Level", root, "Lv1", 16, new Vector2(42, 24), new Vector2(30, 51), Ink);
            Label("m_text_Price", root, "6", 20, new Vector2(104, 24), new Vector2(0, -54), Ink);
            return root.gameObject;
        }
        private static GameObject BuildStorage()
        {
            var root = Rect("StorageRobotSlot", null, new Vector2(92, 118), Vector2.zero);
            Image(root, new Color(0.76f, 0.68f, 0.58f), true);
            root.gameObject.AddComponent<ShopPointerInput>();
            var icon = Image(Rect("m_img_RobotIcon", root, new Vector2(80, 79), new Vector2(0, 10)), Color.white);
            icon.type = UnityEngine.UI.Image.Type.Simple;
            icon.sprite = null;
            icon.preserveAspect = true;
            icon.enabled = false;
            Label("m_text_Storage", root, "暂存位", 17, new Vector2(90, 28), new Vector2(0, -43), Ink);
            return root.gameObject;
        }
        private static GameObject BuildGhost()
        {
            var root = Rect("RobotDragGhost", null, new Vector2(100, 100), Vector2.zero);
            var image = Image(root, Color.white);
            image.sprite = null;
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = true;
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            return root.gameObject;
        }
        private static GameObject BuildWindow(GameObject item, GameObject storage, GameObject ghost)
        {
            var root = Rect("BattleShopUI", null, Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            root.gameObject.AddComponent<GraphicRaycaster>();
            root.gameObject.AddComponent<RobotDragController>();
            var panel = Rect("m_rect_ShopPanel", root, new Vector2(-24, 218), new Vector2(0, 12));
            panel.anchorMin = new Vector2(0, 0);
            panel.anchorMax = new Vector2(1, 0);
            panel.pivot = new Vector2(0.5f, 0);
            Image(panel, new Color(0.94f, 0.85f, 0.73f), true);
            var rim = Rect("TopRim", panel, new Vector2(-8, 5), Vector2.zero);
            rim.anchorMin = new Vector2(0, 1);
            rim.anchorMax = new Vector2(1, 1);
            rim.anchoredPosition = new Vector2(0, -3);
            Image(rim, new Color(0.72f, 0.47f, 0.21f));
            var balance = Rect("EnergyBalance", panel, new Vector2(146, 36), Vector2.zero);
            Pin(balance, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -12));
            Image(balance, new Color(0.25f, 0.15f, 0.12f));
            var coin = Rect("m_img_EnergyCoin", balance, new Vector2(34, 34), new Vector2(-55, 0));
            Image(coin, new Color(0.74f, 0.8f, 0.81f));
            Label("CoinMark", coin, "E", 22, new Vector2(28, 28), Vector2.zero, new Color(0.28f, 0.34f, 0.36f));
            var coinText = Label("m_text_EnergyCoins", panel, "20", 25, new Vector2(83, 36), Vector2.zero, Color.white);
            Pin(coinText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(67, -12));
            var storageInstance = (GameObject)PrefabUtility.InstantiatePrefab(storage, panel);
            storageInstance.name = "m_item_Storage";
            Pin((RectTransform)storageInstance.transform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(65, -5));
            var offers = Rect("m_tf_Offers", panel, new Vector2(-246, 142), new Vector2(-1, -4));
            offers.anchorMin = new Vector2(0, 0.5f);
            offers.anchorMax = new Vector2(1, 0.5f);
            var layout = offers.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            for (var i = 0; i < 3; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(item, offers);
                instance.name = "Slot_" + i;
            }
            var refresh = Rect("m_btn_Refresh", panel, new Vector2(100, 95), Vector2.zero);
            Pin(refresh, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-64, -3));
            var refreshImage = Image(refresh, new Color(1f, 0.77f, 0.16f), true);
            var button = refresh.gameObject.AddComponent<Button>();
            button.targetGraphic = refreshImage;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Label("Title", refresh, "刷新", 24, new Vector2(96, 30), new Vector2(0, 20), Ink);
            Label("m_text_Price", refresh, "5", 23, new Vector2(92, 30), new Vector2(0, -16), Ink);
            var hint = Label("RefreshWarning", panel, "刷新清除商品区内已购买机器人，不退款", 16,
                new Vector2(-20, 26), Vector2.zero, new Color(0.45f, 0.33f, 0.24f));
            hint.rectTransform.anchorMin = new Vector2(0, 0);
            hint.rectTransform.anchorMax = new Vector2(1, 0);
            hint.rectTransform.anchoredPosition = new Vector2(0, 17);
            var message = Label("m_text_Message", panel, "", 20, new Vector2(-20, 30), Vector2.zero,
                new Color(0.8f, 0.15f, 0.1f));
            message.rectTransform.anchorMin = new Vector2(0, 1);
            message.rectTransform.anchorMax = new Vector2(1, 1);
            message.rectTransform.anchoredPosition = new Vector2(0, 24);
            var ghostInstance = (GameObject)PrefabUtility.InstantiatePrefab(ghost, root);
            ghostInstance.name = "m_item_DragGhost";
            ghostInstance.SetActive(false);
            return root.gameObject;
        }
        private static void AttachMain()
        {
            var placementRoot = GameObject.Find("PlacementBoards");
            var player = GameObject.Find("PlayerBoardWorldRoot");
            var opponent = GameObject.Find("EnemyBoardWorldRoot");
            if (placementRoot == null || player == null || opponent == null)
                throw new InvalidOperationException("Main 现有盘面对象不完整，禁止重复创建盘面。");
            var placement = placementRoot.GetComponent<PlacementController>();
            var playerBoard = player.GetComponentInChildren<PlacementBoardView>(true);
            var opponentBoard = opponent.GetComponentInChildren<PlacementBoardView>(true);
            if (placement == null || playerBoard == null || opponentBoard == null)
                throw new InvalidOperationException("Main 现有盘面组件不完整。");
            var host = GameObject.Find("BattleShop");
            if (host == null) host = new GameObject("BattleShop");
            var controller = host.GetComponent<BattleShopSceneController>();
            if (controller == null) controller = host.AddComponent<BattleShopSceneController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("_placement").objectReferenceValue = placement;
            serialized.FindProperty("_playerBoard").objectReferenceValue = playerBoard;
            serialized.FindProperty("_opponentBoard").objectReferenceValue = opponentBoard;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            // Startup UIRoot owns the persistent EventSystem.
            var sceneEvents = GameObject.Find("EventSystem");
            if (sceneEvents != null) sceneEvents.SetActive(false);
        }
    }
}

