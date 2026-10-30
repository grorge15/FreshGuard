using System;
using System.IO;
using System.Linq;
using GameConfig;
using GameLogic;
using Luban;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FreshGuard.Editor
{
    /// <summary>安装确认的等级边框与切面星星，不改占格、碰撞和商店布局。</summary>
    public static class RobotStarRatingPrefabBuilder
    {
        private const string StarPath = "Assets/AssetRaw/Actor/Robots/RobotLevelStar.png";

        [MenuItem("FreshGuard/Robots/Apply Star Ratings")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行模式并等待编译完成。");
            AssetDatabase.ImportAsset(StarPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(StarPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 512f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(0.5f, 0.5f);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            var star = AssetDatabase.LoadAssetAtPath<Sprite>(StarPath);
            if (star == null) throw new InvalidOperationException("星星图片导入失败。");

            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            foreach (var config in tables.TbRobot.DataList)
            {
                var path = "Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var portrait = root.transform.Find("RobotPortrait").GetComponent<SpriteRenderer>();
                    var icon = root.transform.Find("RobotIcon").GetComponent<SpriteRenderer>();
                    var framePath = "Assets/AssetRaw/Actor/Robots/LevelFrames/" + RobotLevelStyle.FrameForIcon(config.RobotIcon) + ".png";
                    var frameImporter = (TextureImporter)AssetImporter.GetAtPath(framePath);
                    frameImporter.textureType = TextureImporterType.Sprite;
                    frameImporter.spriteImportMode = SpriteImportMode.Single;
                    frameImporter.spritePixelsPerUnit = icon.sprite.pixelsPerUnit;
                    frameImporter.mipmapEnabled = false;
                    frameImporter.alphaIsTransparency = true;
                    frameImporter.textureCompression = TextureImporterCompression.Uncompressed;
                    frameImporter.filterMode = FilterMode.Bilinear;
                    frameImporter.wrapMode = TextureWrapMode.Clamp;
                    var frameSettings = new TextureImporterSettings();
                    frameImporter.ReadTextureSettings(frameSettings);
                    frameSettings.spriteMeshType = SpriteMeshType.FullRect;
                    frameSettings.spriteAlignment = (int)SpriteAlignment.Custom;
                    frameSettings.spritePivot = new Vector2(icon.sprite.pivot.x / icon.sprite.rect.width, icon.sprite.pivot.y / icon.sprite.rect.height);
                    frameImporter.SetTextureSettings(frameSettings);
                    frameImporter.SaveAndReimport();
                    var frameNode = root.transform.Find("RobotLevelFrame");
                    if (frameNode == null) { frameNode = new GameObject("RobotLevelFrame").transform; frameNode.SetParent(root.transform, false); }
                    frameNode.gameObject.layer = icon.gameObject.layer;
                    frameNode.localPosition = icon.transform.localPosition;
                    frameNode.localRotation = icon.transform.localRotation;
                    frameNode.localScale = icon.transform.localScale;
                    var frame = frameNode.GetComponent<SpriteRenderer>();
                    if (frame == null) frame = frameNode.gameObject.AddComponent<SpriteRenderer>();
                    frame.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(framePath);
                    frame.color = RobotLevelStyle.FrameColorForLevel(1);
                    frame.enabled = true;
                    frame.sortingLayerID = icon.sortingLayerID;
                    frame.sortingOrder = icon.sortingOrder + 1;
                    portrait.sortingOrder = icon.sortingOrder + 2;
                    icon.color = RobotLevelStyle.ColorForLevel(1);
                    var starRoot = root.transform.Find("LevelStars");
                    if (starRoot == null)
                    {
                        starRoot = new GameObject("LevelStars").transform;
                        starRoot.SetParent(root.transform, false);
                    }
                    starRoot.gameObject.layer = root.layer;
                    starRoot.localPosition = Vector3.zero;
                    starRoot.localRotation = Quaternion.identity;
                    starRoot.localScale = Vector3.one;
                    var renderers = new SpriteRenderer[RobotLevelStyle.MaxStars];
                    var anchor = RobotLevelStyle.StarAnchor(config.ShapeId_Ref.CellOffsets) * MapGridMath.REFERENCE_CELL_SIZE;
                    for (var i = 0; i < renderers.Length; i++)
                    {
                        var node = starRoot.Find("Star" + (i + 1));
                        if (node == null) { node = new GameObject("Star" + (i + 1)).transform; node.SetParent(starRoot, false); }
                        node.SetSiblingIndex(i);
                        node.gameObject.layer = root.layer;
                        node.localPosition = anchor;
                        node.localRotation = Quaternion.identity;
                        node.localScale = Vector3.one * (RobotLevelStyle.StarSpriteSize * RobotLevelStyle.StarScaleForCells(config.ShapeId_Ref.CellOffsets)
                            * MapGridMath.REFERENCE_CELL_SIZE / star.bounds.size.x);
                        var renderer = node.GetComponent<SpriteRenderer>();
                        if (renderer == null) renderer = node.gameObject.AddComponent<SpriteRenderer>();
                        renderer.sprite = star;
                        renderer.color = Color.white;
                        renderer.enabled = i == 0;
                        renderer.sortingLayerID = portrait.sortingLayerID;
                        renderer.sortingOrder = portrait.sortingOrder + 1 + i;
                        renderers[i] = renderer;
                    }
                    var view = new SerializedObject(root.GetComponent<RobotView>());
                    view.FindProperty("_levelFrameRenderer").objectReferenceValue = frame;
                    view.FindProperty("_starsRoot").objectReferenceValue = starRoot;
                    var stars = view.FindProperty("_levelStars");
                    stars.arraySize = renderers.Length;
                    for (var i = 0; i < renderers.Length; i++) stars.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
                    view.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            foreach (var name in new[] { "ShopRobotItem", "StorageRobotSlot", "RobotDragGhost", "BattleShopUI" })
            {
                var path = "Assets/AssetRaw/UI/Shop/" + name + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var icon in root.GetComponentsInChildren<Image>(true).Where(image => image.name == "m_img_RobotIcon"))
                    {
                        var frameNode = icon.transform.Find("m_img_LevelFrame") as RectTransform;
                        if (frameNode == null)
                        {
                            frameNode = new GameObject("m_img_LevelFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
                            frameNode.SetParent(icon.transform, false);
                        }
                        frameNode.gameObject.layer = icon.gameObject.layer;
                        frameNode.SetSiblingIndex(0);
                        frameNode.anchorMin = Vector2.zero;
                        frameNode.anchorMax = Vector2.one;
                        frameNode.pivot = icon.rectTransform.pivot;
                        frameNode.offsetMin = frameNode.offsetMax = Vector2.zero;
                        var frame = frameNode.GetComponent<Image>();
                        frame.color = RobotLevelStyle.FrameColorForLevel(1);
                        frame.preserveAspect = true;
                        frame.raycastTarget = false;
                        frame.enabled = false;
                        var starRoot = icon.transform.Find("m_rect_LevelStars") as RectTransform;
                        if (starRoot == null)
                        {
                            starRoot = new GameObject("m_rect_LevelStars", typeof(RectTransform)).GetComponent<RectTransform>();
                            starRoot.SetParent(icon.transform, false);
                        }
                        starRoot.gameObject.layer = icon.gameObject.layer;
                        starRoot.SetAsLastSibling();
                        starRoot.anchorMin = Vector2.zero;
                        starRoot.anchorMax = Vector2.one;
                        starRoot.pivot = icon.rectTransform.pivot;
                        starRoot.offsetMin = starRoot.offsetMax = Vector2.zero;
                        for (var i = 0; i < RobotLevelStyle.MaxStars; i++)
                        {
                            var child = starRoot.Find("m_img_Star" + (i + 1)) as RectTransform;
                            if (child == null)
                            {
                                child = new GameObject("m_img_Star" + (i + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
                                child.SetParent(starRoot, false);
                            }
                            child.SetSiblingIndex(i);
                            child.gameObject.layer = icon.gameObject.layer;
                            child.anchorMin = child.anchorMax = child.pivot = new Vector2(0.5f, 0.5f);
                            var image = child.GetComponent<Image>();
                            image.sprite = star;
                            image.color = Color.white;
                            image.preserveAspect = true;
                            image.raycastTarget = false;
                            image.enabled = false;
                        }
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Robot Stars] 已安装 8 种机器人及商店、暂存、拖拽的 1–5 星显示，统一五星间距与底部居中位置。");
        }
    }
}
