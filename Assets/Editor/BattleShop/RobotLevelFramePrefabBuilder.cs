using System;
using System.IO;
using System.Linq;
using GameConfig;
using GameLogic;
using Luban;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FreshGuard.Editor
{
    /// <summary>安装机器人等级边框，不改占格、碰撞和商店布局。</summary>
    public static class RobotLevelFramePrefabBuilder
    {
        [MenuItem("FreshGuard/Robots/Apply Level Frames")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行模式并等待编译完成。");
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
                    var view = new SerializedObject(root.GetComponent<RobotView>());
                    view.FindProperty("_levelFrameRenderer").objectReferenceValue = frame;
                    view.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            foreach (var name in new[] { "ShopRobotItem", "RobotDragGhost", "BattleShopUI" })
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
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Robot Frames] 已更新机器人及商店、拖拽图标的等级边框。");
        }
    }
}
