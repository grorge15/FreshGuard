using System;
using System.IO;
using System.Linq;
using GameConfig;
using GameLogic;
using Luban;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FreshGuard.Editor
{
    /// <summary>按现有配置制作702至704的立绘、等级底板和整体碰撞预制体。</summary>
    public static class KitchenRobotPrefabBuilder
    {
        private const string SpriteFolder = "Assets/AssetRaw/Actor/Sprites/Robots/";
        private const string PrefabFolder = "Assets/AssetRaw/Actor/Prefabs/Robots/";
        private const float PixelsPerUnit = 800f;

        [MenuItem("FreshGuard/Robots/Build Kitchen Robots 702-704")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行并等待编译完成。");
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            var names = new[] { "擀面杖机器人_3x1_840x280", "保鲜盒机器人_2x1_560x280", "榨汁机机器人704" };
            for (var index = 0; index < names.Length; index++)
            {
                var config = tables.TbRobot.Get(702 + index);
                var offsets = config.ShapeId_Ref.CellOffsets;
                var columns = offsets.Max(cell => cell[0]) + 1;
                var rows = offsets.Max(cell => cell[1]) + 1;
                if (offsets.Length != columns * rows || offsets.Any(cell => cell[0] < 0 || cell[1] < 0))
                    throw new InvalidOperationException("本构建器仅用于从原点开始的矩形占格。");
                var width = columns * 280 + 40;
                var height = rows * 280 + 40;
                var template = rows > 1 ? "RobotRapidFire" : "RobotCannon";
                var source = SpriteFolder + "Portraits/" + names[index] + ".png";
                var icon = SpriteFolder + "Robots/" + config.RobotIcon + ".png";
                var frame = SpriteFolder + "LevelFrames/" + RobotLevelStyle.FrameForIcon(config.RobotIcon) + ".png";
                var portrait = SpriteFolder + "Portraits/" + RobotLevelStyle.PortraitForIcon(config.RobotIcon) + ".png";
                ExtendMiddle(SpriteFolder + "Robots/" + template + "Icon.png", icon, width, height);
                ExtendMiddle(SpriteFolder + "LevelFrames/" + template + "LevelFrame.png", frame, width, height);
                BakePortrait(source, portrait, width, height);
                ImportSprite(source, new Vector2(0.5f, 0.5f));
                var pivot = new Vector2(160f / width, 160f / height);
                ImportSprite(icon, pivot);
                ImportSprite(frame, pivot);
                ImportSprite(portrait, pivot);
                BuildPrefab(config, source, icon, frame, columns, rows);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Kitchen Robots] 702、703、704的预制体和商店图标资源已保存。");
        }

        private static void BuildPrefab(GameConfig.robot.Robot config, string source, string icon, string frame, int columns, int rows)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject(config.RobotPrefab);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            try
            {
                var cellSize = MapGridMath.REFERENCE_CELL_SIZE;
                var body = root.AddComponent<PolygonCollider2D>();
                body.SetPath(0, new[] { new Vector2(-cellSize / 2, -cellSize / 2),
                    new Vector2((columns - 0.5f) * cellSize, -cellSize / 2),
                    new Vector2((columns - 0.5f) * cellSize, (rows - 0.5f) * cellSize),
                    new Vector2(-cellSize / 2, (rows - 0.5f) * cellSize) });
                var view = root.AddComponent<RobotView>();
                root.AddComponent<RobotBoardPointerInput>();
                var cellSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/AssetRaw/Actor/Sprites/PhysicsSprites/PlacementSlotSquare.png");
                foreach (var offset in config.ShapeId_Ref.CellOffsets)
                {
                    var cell = AddRenderer(root.transform, "Cell_" + offset[0] + "_" + offset[1], cellSprite, 0);
                    cell.transform.localPosition = new Vector3(offset[0] * cellSize, offset[1] * cellSize, 0);
                    cell.transform.localScale = new Vector3(0.329f / cellSprite.bounds.size.x, 0.329f / cellSprite.bounds.size.y, 1);
                    cell.enabled = false;
                }
                var iconRenderer = AddRenderer(root.transform, "RobotIcon", AssetDatabase.LoadAssetAtPath<Sprite>(icon), 1);
                iconRenderer.color = RobotLevelStyle.ColorForLevel(1);
                var frameRenderer = AddRenderer(root.transform, "RobotLevelFrame", AssetDatabase.LoadAssetAtPath<Sprite>(frame), 2);
                frameRenderer.color = RobotLevelStyle.FrameColorForLevel(1);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(source);
                var portraitRenderer = AddRenderer(root.transform, "RobotPortrait", sprite, 3);
                var texture = ReadTexture(source);
                try
                {
                    var bounds = OpaqueBounds(texture);
                    var factor = Mathf.Min((columns * 280 - 48f) / bounds.width, (rows * 280 - 48f) / bounds.height);
                    var center = new Vector2((columns - 1) * cellSize / 2, (rows - 1) * cellSize / 2);
                    portraitRenderer.transform.localScale = new Vector3(factor, factor, 1);
                    portraitRenderer.transform.localPosition = center - (bounds.center - sprite.pivot) * (factor / PixelsPerUnit);
                }
                finally { Object.DestroyImmediate(texture); }
                var serialized = new SerializedObject(view);
                serialized.FindProperty("_configId").intValue = config.Id;
                serialized.FindProperty("_iconRenderer").objectReferenceValue = iconRenderer;
                serialized.FindProperty("_portraitRenderer").objectReferenceValue = portraitRenderer;
                serialized.FindProperty("_levelFrameRenderer").objectReferenceValue = frameRenderer;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + config.RobotPrefab + ".prefab") == null)
                    throw new InvalidOperationException("预制体保存失败：" + config.RobotPrefab);
            }
            finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static SpriteRenderer AddRenderer(Transform parent, string name, Sprite sprite, int order)
        {
            if (sprite == null) throw new InvalidOperationException("精灵资源不存在：" + name);
            var node = new GameObject(name);
            node.transform.SetParent(parent, false);
            var renderer = node.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static void ExtendMiddle(string source, string target, int width, int height)
        {
            var input = ReadTexture(source);
            var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                if (height != input.height || width < input.width)
                    throw new InvalidOperationException("底板模板尺寸不兼容：" + source);
                var original = input.GetPixels32();
                var pixels = new Color32[width * height];
                var extra = width - input.width;
                var middle = input.width / 2;
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var sx = x < middle ? x : x >= middle + extra ? x - extra : middle;
                        pixels[y * width + x] = original[y * input.width + sx];
                    }
                output.SetPixels32(pixels);
                File.WriteAllBytes(target, output.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(input); Object.DestroyImmediate(output); }
        }

        private static void BakePortrait(string source, string target, int width, int height)
        {
            var input = ReadTexture(source);
            var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                var bounds = OpaqueBounds(input);
                var factor = Mathf.Min((width - 88f) / bounds.width, (height - 88f) / bounds.height);
                var pixels = new Color[width * height];
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var point = (new Vector2(x + 0.5f - width / 2f, y + 0.5f - height / 2f) / factor) + bounds.center;
                        if (point.x >= 0 && point.y >= 0 && point.x < input.width && point.y < input.height)
                            pixels[y * width + x] = input.GetPixelBilinear(point.x / input.width, point.y / input.height);
                    }
                output.SetPixels(pixels);
                File.WriteAllBytes(target, output.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(input); Object.DestroyImmediate(output); }
        }

        private static Texture2D ReadTexture(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(File.ReadAllBytes(path))) return texture;
            Object.DestroyImmediate(texture);
            throw new InvalidOperationException("无法读取图片：" + path);
        }

        private static Rect OpaqueBounds(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            var minX = texture.width; var minY = texture.height; var maxX = -1; var maxY = -1;
            for (var y = 0; y < texture.height; y++)
                for (var x = 0; x < texture.width; x++)
                {
                    if (pixels[y * texture.width + x].a <= 16) continue;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
            if (maxX < minX) throw new InvalidOperationException("立绘没有可见内容。");
            return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static void ImportSprite(string path, Vector2 pivot)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
    }
}
