using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameConfig;
using GameLogic;
using Luban;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FreshGuard.Editor
{
    /// <summary>在独立预览场景渲染真实游戏组件，便于调整撞击反馈，不修改当前场景。</summary>
    public static class BallImpactPreviewCapture
    {
        [MenuItem("FreshGuard/Effects/Capture Ball Impact Preview")]
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请在编辑模式生成预览。");
            var scene = EditorSceneManager.NewPreviewScene();
            var registry = new BattleEntityRegistry();
            var temporaryAssets = new List<Object>();
            var previousTarget = RenderTexture.active;
            var folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs/BallImpactPreview");
            Directory.CreateDirectory(folder);
            RenderTexture target = null;
            Texture2D image = null;
            try
            {
                var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,
                    "AssetRaw/Configs/bytes", name + ".bytes"))));
                var cameraRoot = NewObject(scene, "ImpactPreviewCamera");
                var camera = cameraRoot.AddComponent<Camera>();
                camera.scene = scene;
                camera.transform.position = new Vector3(0f, 0.28f, -10f);
                camera.orthographic = true;
                camera.orthographicSize = 1.05f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(193, 234, 243, 255);
                target = new RenderTexture(1500, 900, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                image = new Texture2D(1500, 900, TextureFormat.RGB24, false);
                var glassSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/AssetRaw/Actor/Glass/Sprites/GlassNormalIntact.png");
                for (var x = -6; x <= 6; x++)
                    for (var y = -4; y <= 5; y++)
                        Sprite(scene, "BlueBoardCell", glassSprite, new Vector2(x * 0.35f, y * 0.35f), Vector2.one * 0.336f, -5000);

                var config = tables.TbRobot.Get(502);
                var robotRoot = Instantiate(scene, "Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab");
                var robot = robotRoot.GetComponent<RobotView>();
                var entity = registry.CreateRobotFromConfig(config, BattleSide.Player);
                Invoke(entity, "TrySetLocation", RobotLocation.Board, new BoardCoordinate(1, 1));
                robot.Bind(registry, entity.InstanceId);
                var collider = robot.GetPhysicalCollider();
                Physics2D.SyncTransforms();
                robot.transform.position += new Vector3(-1.15f - collider.bounds.center.x, 0.15f - collider.bounds.max.y, 0f);

                var glassRoot = Instantiate(scene, "Assets/AssetRaw/Actor/Glass/Prefabs/" + tables.TbGlass.Get(1).PrefabLocation + ".prefab");
                var glass = glassRoot.GetComponent<GlassView>();
                var owner = NewObject(scene, "PreviewGlassOwner").AddComponent<GlassBoardController>();
                var glassEntity = registry.CreateGlassFromConfig(tables.TbGlass.Get(1), BattleSide.Player, new BoardCoordinate(2, 2), 0.25f);
                glass.Bind(owner, glassEntity, tables.TbGlass.Get(1), 0.35f);
                glass.transform.position = new Vector3(0f, -0.025f, 0f);

                var white = SpriteFromWhite(temporaryAssets);
                Sprite(scene, "Wall", white, new Vector2(1.15f, 0.025f), new Vector2(1.02f, 0.25f), -1,
                    new Color32(80, 111, 131, 255));
                Sprite(scene, "WallEdge", white, new Vector2(1.15f, 0.143f), new Vector2(1.02f, 0.014f), 0,
                    new Color32(164, 192, 205, 255));
                Physics2D.SyncTransforms();
                var contacts = new[] { new Vector2(collider.bounds.min.x + 0.15f, 0.15f), new Vector2(0f, 0.15f), new Vector2(1.15f, 0.15f) };
                var balls = new PhysicalCollisionBall[3];
                var feedbacks = new BallImpactFeedback[3];
                for (var i = 0; i < balls.Length; i++)
                {
                    var root = Instantiate(scene, "Assets/AssetRaw/Physics/Prefabs/PhysicalCollisionBall.prefab");
                    balls[i] = root.GetComponent<PhysicalCollisionBall>();
                    if (root.GetComponent<BallImpactFeedback>() == null) Invoke(balls[i], "Awake");
                    feedbacks[i] = root.GetComponent<BallImpactFeedback>();
                    balls[i].GlobalValueProvider = key => 0f;
                    balls[i].BindBattle(1, BattleSide.Player, () => true);
                    balls[i].LaunchAt(Position(contacts[i], balls[i].WorldRadius, -0.48f), 45f);
                    balls[i].GetComponent<Rigidbody2D>().simulated = false;
                }
                for (var frame = 0; frame < 76; frame++)
                {
                    var t = -0.48f + frame * 0.02f;
                    for (var i = 0; i < balls.Length; i++)
                    {
                        var position = Position(contacts[i], balls[i].WorldRadius, t);
                        balls[i].transform.position = position;
                        balls[i].GetComponent<Rigidbody2D>().position = position;
                        Physics2D.SyncTransforms();
                        if (frame == 24)
                        {
                            Invoke(feedbacks[i], "RecordCorner", contacts[i] + Vector2.up * balls[i].WorldRadius);
                            if (i == 0) robot.PlayCollisionFeedback();
                            else Invoke(feedbacks[i], i == 1 ? "PlayGlass" : "PlayWall", contacts[i], Vector2.up);
                        }
                        Invoke(feedbacks[i], "Advance", 0.02f);
                    }
                    if (frame >= 24)
                    {
                        Invoke(robot, "AdvanceCollisionFeedback", 0.02f);
                        Invoke(robotRoot.GetComponent<RobotImpactOutline>(), "Advance", 0.02f);
                        glass.ShowDurability(1, false);
                    }
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 1500, 900), 0, 0);
                    image.Apply();
                    File.WriteAllBytes(Path.Combine(folder, "frame-" + frame.ToString("D3") + ".png"), image.EncodeToPNG());
                }
                Debug.Log("Ball impact preview captured: " + folder);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                registry.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (image != null) Object.DestroyImmediate(image);
                foreach (var asset in temporaryAssets) if (asset != null) Object.DestroyImmediate(asset);
            }
        }

        private static Vector2 Position(Vector2 contact, float radius, float t) =>
            contact + new Vector2(1.2f * t, radius + 1.75f * Mathf.Abs(t));

        private static GameObject NewObject(Scene scene, string name)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static GameObject Instantiate(Scene scene, string path)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static void Sprite(Scene scene, string name, Sprite sprite, Vector2 position, Vector2 size, int order, Color? color = null)
        {
            var renderer = NewObject(scene, name).AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color ?? Color.white;
            renderer.sortingOrder = order;
            renderer.transform.position = position;
            renderer.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f);
        }

        private static Sprite SpriteFromWhite(List<Object> assets)
        {
            var sprite = UnityEngine.Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), Vector2.one * 0.5f, 1f);
            assets.Add(sprite);
            return sprite;
        }

        private static void Invoke(object target, string method, params object[] arguments) => target.GetType()
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, arguments);
    }
}
