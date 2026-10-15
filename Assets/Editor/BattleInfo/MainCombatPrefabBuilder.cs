using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.EditorTools
{
    public static class MainCombatPrefabBuilder
    {
        public const string PATH = "Assets/AssetRaw/UI/MainCombat/MainCombatUI.prefab";

        [MenuItem("FreshGuard/UI/Create MainCombatUI If Missing")]
        public static void CreateIfMissing()
        {
            // 已有预制体是布局的唯一来源；重复执行不能覆盖手工调整。
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PATH) != null) return;
            var info = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/BattleInfo/BattleInfoUI.prefab");
            var shop = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/Shop/BattleShopUI.prefab");
            if (info == null || shop == null) throw new InvalidOperationException("缺少信息栏或商店源预制体。");
            if (!AssetDatabase.IsValidFolder("Assets/AssetRaw/UI/MainCombat"))
                AssetDatabase.CreateFolder("Assets/AssetRaw/UI", "MainCombat");
            // 在临时 Canvas 下保存，避免独立根 Canvas 驱动 RectTransform 并序列化为零尺寸/零缩放。
            var staging = new GameObject("MainCombatPrefabStaging", typeof(RectTransform), typeof(Canvas));
            var root = new GameObject("MainCombatUI", typeof(RectTransform));
            root.transform.SetParent(staging.transform, false);
            try
            {
                root.layer = 5;
                var rect = (RectTransform)root.transform;
                Fill(rect);
                root.AddComponent<Canvas>();
                root.AddComponent<GraphicRaycaster>();
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0;
                AddRegion(info, root.transform, "BattleInfoUI", new Vector2(1040, 156),
                    Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero, "m_rect_BattleInfo");
                AddRegion(shop, root.transform, "BattleShopUI", new Vector2(1056, 218),
                    new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 12), "m_rect_ShopPanel");
                PrefabUtility.SaveAsPrefabAsset(root, PATH);
                AssetDatabase.SaveAssets();
            }
            finally { UnityEngine.Object.DestroyImmediate(staging); }
        }

        private static void AddRegion(GameObject source, Transform parent, string name, Vector2 size,
            Vector2 anchor, Vector2 pivot, Vector2 position, string panelPath)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.name = name;
            // 主窗口统一管理画布与射线检测，保留信息区域自身的穿透 CanvasGroup。
            foreach (var scaler in instance.GetComponents<CanvasScaler>()) UnityEngine.Object.DestroyImmediate(scaler);
            foreach (var raycaster in instance.GetComponents<GraphicRaycaster>()) UnityEngine.Object.DestroyImmediate(raycaster);
            foreach (var canvas in instance.GetComponents<Canvas>()) UnityEngine.Object.DestroyImmediate(canvas);
            var rect = (RectTransform)instance.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition3D = new Vector3(position.x, position.y, 0);
            rect.localScale = Vector3.one;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            var panel = (RectTransform)rect.Find(panelPath);
            Fill(panel);
            PrefabUtility.RecordPrefabInstancePropertyModifications(panel);
        }

        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one * 0.5f;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }
}
