using System;
using System.IO;
using GameConfig;
using Luban;
using GameLogic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FreshGuard.Editor
{
    /// <summary>仅增补既有资源；保留场景布局和已有 Prefab 内容。</summary>
    public static class RobotMergePrefabBuilder
    {
        [MenuItem("FreshGuard/Robots/Upgrade Merge Presentation")]
        public static void Upgrade()
        {
            if (Application.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("请退出运行并等待编译。");
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            foreach (var config in tables.TbRobot.DataList)
            {
                var path = "Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponent<RobotBoardPointerInput>() == null) root.AddComponent<RobotBoardPointerInput>();
                    var label = root.transform.Find("LevelLabel");
                    if (label == null)
                    {
                        label = new GameObject("LevelLabel", typeof(TextMesh)).transform;
                        label.SetParent(root.transform, false);
                    }
                    label.localPosition = new Vector3(0f, -0.20f, -0.01f);
                    var text = label.GetComponent<TextMesh>();
                    if (text == null) text = label.gameObject.AddComponent<TextMesh>();
                    text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
                    text.GetComponent<MeshRenderer>().sortingOrder = 15;
                    text.text = "Lv1";
                    text.fontSize = 32;
                    text.characterSize = 0.035f;
                    text.anchor = TextAnchor.MiddleCenter;
                    text.color = Color.white;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            // 独立幽灵资源与窗口嵌套实例均补齐，避免既有窗口覆盖隐藏新节点。
            foreach (var path in new[] { "Assets/AssetRaw/UI/Shop/RobotDragGhost.prefab", "Assets/AssetRaw/UI/Shop/BattleShopUI.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var ghost = path.EndsWith("RobotDragGhost.prefab", StringComparison.Ordinal) ? root.transform : root.transform.Find("m_item_DragGhost");
                    UpgradeGhost(ghost);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var controllers = UnityEngine.Object.FindObjectsOfType<PlacementController>();
            foreach (var controller in controllers)
            {
                var camera = controller.SceneCamera;
                if (camera != null && camera.GetComponent<Physics2DRaycaster>() == null) camera.gameObject.AddComponent<Physics2DRaycaster>();
                EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
                EditorSceneManager.SaveScene(controller.gameObject.scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Robot Merge] 机器人输入、等级标签与拖拽等级背景装配完成；重复执行不增加对象。");
        }

        internal static void UpgradeGhost(Transform ghost)
        {
            if (ghost == null) throw new InvalidOperationException("缺少拖拽图标节点。");
            var background = ghost.Find("m_img_LevelBackground");
            if (background != null) UnityEngine.Object.DestroyImmediate(background.gameObject);
            var level = ghost.Find("m_text_Level");
            if (level != null) UnityEngine.Object.DestroyImmediate(level.gameObject);
            var icon = EnsureRect(ghost, "m_img_RobotIcon");
            icon.sizeDelta = new Vector2(100, 100);
            var iconImage = icon.GetComponent<Image>() ?? icon.gameObject.AddComponent<Image>();
            iconImage.color = Color.white;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            var rootImage = ghost.GetComponent<Image>();
            if (rootImage != null) UnityEngine.Object.DestroyImmediate(rootImage);
        }

        private static RectTransform EnsureRect(Transform parent, string name)
        {
            var node = parent.Find(name);
            if (node != null) return node.GetComponent<RectTransform>();
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = parent.gameObject.layer;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            return rect;
        }
    }
}
