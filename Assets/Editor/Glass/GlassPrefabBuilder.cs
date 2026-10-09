using System;
using System.IO;
using GameConfig;
using GameLogic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FreshGuard.Editor
{
    /// <summary>所有PNG、Prefab和场景写入由Unity Editor执行；可重复装配。</summary>
    public static class GlassPrefabBuilder
    {
        public const string Folder = "Assets/AssetRaw/Actor/Glass";

        public static Tables ReadTables() => new Tables(name =>
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/AssetRaw/Configs/bytes/" + name + ".bytes");
            if (asset == null) throw new InvalidOperationException("配置二进制缺失：" + name);
            return new Luban.ByteBuf(asset.bytes);
        });

        [MenuItem("FreshGuard/Glass/Build Prefabs And Attach Main")]
        public static void Build()
        {
            if (Application.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("玻璃装配需要编译完成的非运行模式。");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/AssetRaw/Scenes/Main.unity")
                throw new InvalidOperationException("请先打开业务Main场景。");
            var host = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
            var bootstrap = UnityEngine.Object.FindObjectOfType<PlacementBoardsBootstrap>();
            if (host == null || bootstrap == null) throw new InvalidOperationException("Main缺少商店或棋盘启动器。");
            EnsureFolder(Folder + "/Sprites");
            EnsureFolder(Folder + "/Prefabs");
            var tables = ReadTables();
            var layout = new BoardLayoutRules(tables.TbBoardLayout.Get(1));
            foreach (var id in new[] { layout.NormalGlassId, layout.ColoredGlassId })
            {
                var config = tables.TbGlass.Get(id);
                var sprites = new Sprite[config.StageSprites.Length];
                for (var i = 0; i < sprites.Length; i++)
                    sprites[i] = CreateSprite(config.StageSprites[i], config.Kind == 1, sprites.Length - 1 - i);
                BuildPrefab(config.PrefabLocation, sprites, layout.CellSize);
            }
            AttachMain(host, bootstrap);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Glass] 两个Prefab、六张阶段图和双方主球已装配并保存。");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        private static Sprite CreateSprite(string name, bool colored, int cracks)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var edge = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y));
                        var color = colored ? Color.HSVToRGB((x + y * 0.45f) / 92f, 0.58f, 0.98f) : new Color(0.45f, 0.86f, 0.96f);
                        color.a = 0.74f;
                        if (edge < 2) { color = Color.Lerp(color, Color.white, 0.7f); color.a = 0.97f; }
                        else if (edge < 5) { color = Color.Lerp(color, Color.white, 0.28f); color.a = 0.84f; }
                        if (Math.Abs(x - y - 15) < 2) { color = Color.Lerp(color, Color.white, 0.45f); color.a = 0.9f; }
                        texture.SetPixel(x, y, color);
                    }
                if (cracks >= 1)
                {
                    Line(texture, 7, 55, 29, 36); Line(texture, 29, 36, 26, 19); Line(texture, 26, 19, 39, 3);
                    Line(texture, 29, 36, 46, 44);
                }
                if (cracks >= 2)
                {
                    Line(texture, 3, 19, 26, 19); Line(texture, 26, 19, 46, 28); Line(texture, 46, 28, 59, 15);
                    Line(texture, 46, 28, 53, 58); Line(texture, 29, 36, 31, 61);
                }
                texture.Apply();
                var path = Folder + "/Sprites/" + name + ".png";
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = size;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static void Line(Texture2D texture, int ax, int ay, int bx, int by)
        {
            var steps = Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay));
            for (var i = 0; i <= steps; i++)
            {
                var x = Mathf.RoundToInt(Mathf.Lerp(ax, bx, (float)i / steps));
                var y = Mathf.RoundToInt(Mathf.Lerp(ay, by, (float)i / steps));
                texture.SetPixel(x, y, new Color(0.08f, 0.17f, 0.23f, 0.95f));
                if (x + 1 < texture.width) texture.SetPixel(x + 1, y, new Color(0.95f, 1f, 1f, 0.88f));
            }
        }

        private static void BuildPrefab(string name, Sprite[] sprites, float cellSize)
        {
            var root = new GameObject(name);
            try
            {
                var collider = root.AddComponent<BoxCollider2D>();
                collider.size = Vector2.one * cellSize;
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                visual.transform.localScale = Vector3.one * (cellSize * 0.94f);
                var renderer = visual.AddComponent<SpriteRenderer>();
                renderer.sprite = sprites[sprites.Length - 1];
                renderer.sortingOrder = 0;
                var view = root.AddComponent<GlassView>();
                var serialized = new SerializedObject(view);
                serialized.FindProperty("_renderer").objectReferenceValue = renderer;
                var stages = serialized.FindProperty("_stageSprites");
                stages.arraySize = sprites.Length;
                for (var i = 0; i < sprites.Length; i++) stages.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Prefabs/" + name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void AttachMain(BattleShopSceneController host, PlacementBoardsBootstrap bootstrap)
        {
            var bootstrapSettings = new SerializedObject(bootstrap);
            var player = bootstrapSettings.FindProperty("_ball").objectReferenceValue as PhysicalCollisionBall;
            if (player == null) throw new InvalidOperationException("现有玩家球引用缺失。");
            var opponent = bootstrapSettings.FindProperty("_opponentBall").objectReferenceValue as PhysicalCollisionBall;
            if (opponent == null)
            {
                var existing = GameObject.Find("OpponentPhysicalCollisionBall");
                if (existing != null) opponent = existing.GetComponent<PhysicalCollisionBall>();
                else
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Physics/Prefabs/PhysicalCollisionBall.prefab");
                    if (prefab == null) throw new InvalidOperationException("能源球Prefab缺失。");
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform.parent);
                    root.name = "OpponentPhysicalCollisionBall";
                    root.transform.localScale = player.transform.localScale;
                    opponent = root.GetComponent<PhysicalCollisionBall>();
                }
            }
            if (opponent == null || opponent == player) throw new InvalidOperationException("对手球绑定无效。");
            bootstrapSettings.FindProperty("_opponentBall").objectReferenceValue = opponent;
            bootstrapSettings.ApplyModifiedPropertiesWithoutUndo();
            var hostSettings = new SerializedObject(host);
            hostSettings.FindProperty("_bootstrap").objectReferenceValue = bootstrap;
            hostSettings.FindProperty("_layoutConfigId").intValue = 1;
            hostSettings.ApplyModifiedPropertiesWithoutUndo();
            var opponentBoard = hostSettings.FindProperty("_opponentBoard").objectReferenceValue as PlacementBoardView;
            opponent.transform.position = opponentBoard.transform.position;
            foreach (var ball in new[] { player, opponent })
            {
                var settings = new SerializedObject(ball);
                settings.FindProperty("launchOnEnable").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(ball);
            }
        }
    }
}
