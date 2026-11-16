using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FreshGuard.Editor
{
    /// <summary>仅在编辑器烘焙星芒；运行时由粒子系统播放序列图，不再重建星芒网格。</summary>
    public static class BerserkImpactAssetBuilder
    {
        private const string Folder = "Assets/AssetRaw/Actor/Sprites/PhysicsSprites";
        private const string AtlasPath = Folder + "/BerserkImpactSheet.png";
        private const string MaterialPath = Folder + "/BerserkImpactParticles.mat";
        private const string BallPath = "Assets/AssetRaw/Actor/Prefabs/PhysicsPrefabs/PhysicalCollisionBall.prefab";
        private const int TileSize = 256;
        private const int Columns = 8;
        private const int Rows = 4;
        private const float Lifetime = 0.28f;
        // 单位球半径为 1；整张粒子四边形宽 12，覆盖星芒的完整运动范围。
        private const float HalfExtent = 6f;

        [MenuItem("FreshGuard/Effects/Rebuild Berserk Particle Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先结束播放，再烘焙暴走星芒。");
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("缺少 Sprites/Default Shader。");
            var scene = EditorSceneManager.NewPreviewScene();
            var cameraRoot = new GameObject("BerserkSheetCamera");
            var meshRoot = new GameObject("BerserkSheetMesh", typeof(MeshFilter), typeof(MeshRenderer));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(meshRoot, scene);
            var mesh = new Mesh { name = "EditorBerserkBake" };
            var temporaryMaterial = new Material(shader) { mainTexture = Texture2D.whiteTexture };
            var tile = new Texture2D(TileSize, TileSize, TextureFormat.RGBA32, false);
            var atlas = new Texture2D(TileSize * Columns, TileSize * Rows, TextureFormat.RGBA32, false);
            var target = new RenderTexture(TileSize, TileSize, 24, RenderTextureFormat.ARGB32);
            var previousTarget = RenderTexture.active;
            try
            {
                var camera = cameraRoot.AddComponent<Camera>();
                camera.scene = scene;
                camera.orthographic = true;
                camera.orthographicSize = HalfExtent;
                camera.transform.position = Vector3.back * 10f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.targetTexture = target;
                meshRoot.GetComponent<MeshFilter>().sharedMesh = mesh;
                meshRoot.GetComponent<MeshRenderer>().sharedMaterial = temporaryMaterial;
                var shape = new StarShape();
                for (var frame = 0; frame < Columns * Rows; frame++)
                {
                    shape.Draw(mesh, (frame + 0.5f) / (Columns * Rows) * Lifetime);
                    camera.Render();
                    RenderTexture.active = target;
                    tile.ReadPixels(new Rect(0, 0, TileSize, TileSize), 0, 0);
                    tile.Apply();
                    var pixels = tile.GetPixels();
                    // Sprites/Default 输出预乘颜色；PNG 存直通颜色，避免运行时再次预乘变暗。
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        var color = pixels[i];
                        if (color.a > 0.0001f)
                        {
                            color.r = Mathf.Clamp01(color.r / color.a);
                            color.g = Mathf.Clamp01(color.g / color.a);
                            color.b = Mathf.Clamp01(color.b / color.a);
                        }
                        else color = Color.clear;
                        pixels[i] = color;
                    }
                    atlas.SetPixels(frame % Columns * TileSize, (Rows - 1 - frame / Columns) * TileSize,
                        TileSize, TileSize, pixels);
                }
                atlas.Apply();
                File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousTarget;
                EditorSceneManager.ClosePreviewScene(scene);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(temporaryMaterial);
                Object.DestroyImmediate(tile);
                Object.DestroyImmediate(atlas);
            }
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
                name = "Android", overridden = true, maxTextureSize = 2048,
                format = TextureImporterFormat.ETC2_RGBA8, compressionQuality = 100 });
            importer.SaveAndReimport();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "BerserkImpactParticles" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            material.color = Color.white;
            EditorUtility.SetDirty(material);
            var root = PrefabUtility.LoadPrefabContents(BallPath);
            try
            {
                var child = root.transform.Find("BerserkBallImpacts");
                if (child == null)
                {
                    child = new GameObject("BerserkBallImpacts").transform;
                    child.SetParent(root.transform, false);
                }
                child.gameObject.layer = root.layer;
                var system = child.GetComponent<ParticleSystem>();
                if (system == null) system = child.gameObject.AddComponent<ParticleSystem>();
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.duration = 1f;
                main.loop = true;
                main.prewarm = false;
                main.playOnAwake = false;
                main.startLifetime = Lifetime;
                main.startSpeed = 0f;
                main.startSize = HalfExtent * 2f;
                main.startColor = Color.white;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Shape;
                main.maxParticles = 16;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                var emission = system.emission; emission.enabled = false;
                var shape = system.shape; shape.enabled = false;
                var sheet = system.textureSheetAnimation;
                sheet.enabled = true;
                sheet.mode = ParticleSystemAnimationMode.Grid;
                sheet.numTilesX = Columns;
                sheet.numTilesY = Rows;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                sheet.startFrame = 0f;
                sheet.cycleCount = 1;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.maxParticleSize = 10f;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var source = root.GetComponent<SpriteRenderer>();
                renderer.sortingLayerID = source.sortingLayerID;
                renderer.sortingOrder = source.sortingOrder + 20;
                PrefabUtility.SaveAsPrefabAsset(root, BallPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        // 与迁移前的暴走星芒形状保持一致，只在编辑器中执行。
        private sealed class StarShape
        {
            private readonly List<Vector3> _vertices = new List<Vector3>(1572);
            private readonly List<Color32> _colors = new List<Color32>(1572);
            private readonly List<int> _triangles = new List<int>(1572);
            private static readonly Color Gold = new Color32(255, 190, 34, 255);
            private static readonly Color Hot = new Color32(255, 248, 199, 255);
            private static readonly Color Orange = new Color32(255, 127, 33, 255);
            private static readonly Color Pink = new Color32(255, 73, 149, 255);
            private static readonly Color[] Colors = { Gold, Orange, Pink, new Color32(165, 107, 255, 255), new Color32(99, 239, 246, 255) };
            private static readonly float[] Lengths = { 57, 44, 59, 42, 53, 45, 55, 39 };
            private static float EaseOut(float value) => 1f - Mathf.Pow(1f - Mathf.Clamp01(value), 3f);
            private static Color Alpha(Color color, float alpha) { color.a = Mathf.Clamp01(alpha); return color; }
            private static Vector2 Direction(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            private static Vector2 Curve(Vector2 a, Vector2 control, Vector2 b, float t)
                => (1f - t) * (1f - t) * a + 2f * (1f - t) * t * control + t * t * b;
            private void Triangle(Vector2 a, Vector2 b, Vector2 c, Color ca, Color cb, Color cc)
            {
                var index = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
                _colors.Add(ca); _colors.Add(cb); _colors.Add(cc);
                _triangles.Add(index); _triangles.Add(index + 1); _triangles.Add(index + 2);
            }
            private void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color) => Triangle(a, b, c, color, color, color);
            private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
            { Triangle(a, b, c, color); Triangle(a, c, d, color); }
            public void Draw(Mesh mesh, float age)
            {
                _vertices.Clear(); _colors.Clear(); _triangles.Clear();
                var progress = Mathf.Clamp01(age / Lifetime);
                var expansion = EaseOut(progress);
                var opacity = Mathf.Pow(1f - progress, 1.2f);
                var growth = EaseOut(age / 0.065f);
                const float unit = 1f / 18f;
                var glow = Alpha(Orange, opacity * 0.38f);
                var edge = Alpha(glow, 0f);
                for (var i = 0; i < 20; i++)
                    Triangle(Vector2.zero, Direction(i * Mathf.PI * 2f / 20f) * ((29f + expansion * 18f) * unit),
                        Direction((i + 1) * Mathf.PI * 2f / 20f) * ((29f + expansion * 18f) * unit), glow, edge, edge);
                var radius = (9f + expansion * 37f) * unit;
                var width = (3f * (1f - progress) + 0.8f) * unit;
                for (var i = 0; i < 40; i++)
                {
                    var a = Direction(i * Mathf.PI * 2f / 40f);
                    var b = Direction((i + 1) * Mathf.PI * 2f / 40f);
                    Quad(a * (radius - width * 0.5f), a * (radius + width * 0.5f),
                        b * (radius + width * 0.5f), b * (radius - width * 0.5f), Alpha(Pink, opacity * 0.65f));
                }
                for (var i = 0; i < Lengths.Length; i++)
                {
                    var angle = i * Mathf.PI / 4f + 0.12f;
                    Shard(angle, (6f + expansion * 15f) * unit, Lengths[i] * growth * (1f - progress * 0.35f) * unit,
                        (i % 2 == 0 ? 8f : 5f) * unit, Alpha(i % 2 == 0 ? Gold : Pink, opacity));
                    Shard(angle, (4f + expansion * 8f) * unit, Lengths[i] * 0.57f * growth * unit,
                        2.6f * unit, Alpha(Hot, opacity));
                }
                for (var i = 0; i < 9; i++)
                    Shard(i * 2.399f + 0.7f, (16f + expansion * (37f + i % 3 * 7f)) * unit,
                        (8f * (1f - progress) + 3f) * unit, 2.5f * unit, Alpha(Colors[i % Colors.Length], opacity * 0.9f));
                radius = 21f * growth * (1f - progress * 0.65f) * unit;
                for (var quarter = 0; quarter < 4; quarter++)
                {
                    var start = Direction(quarter * Mathf.PI * 0.5f) * radius;
                    var end = Direction((quarter + 1) * Mathf.PI * 0.5f) * radius;
                    var previous = start;
                    for (var i = 1; i <= 6; i++)
                    {
                        var next = Curve(start, (start + end) * 0.12f, end, i / 6f);
                        Triangle(Vector2.zero, previous, next, Alpha(Hot, opacity));
                        previous = next;
                    }
                }
                mesh.Clear();
                mesh.SetVertices(_vertices); mesh.SetColors(_colors); mesh.SetTriangles(_triangles, 0);
            }
            private void Shard(float angle, float distance, float length, float width, Color color)
            {
                var direction = Direction(angle);
                var side = new Vector2(-direction.y, direction.x);
                var center = direction * distance;
                var start = new Vector2(-length * 0.52f, 0f);
                var end = new Vector2(length * 0.55f, 0f);
                for (var half = -1; half <= 1; half += 2)
                {
                    var previous = start;
                    for (var i = 1; i <= 8; i++)
                    {
                        var next = Curve(start, new Vector2(-length * 0.18f, width * half), end, i / 8f);
                        Triangle(center, center + direction * previous.x + side * previous.y,
                            center + direction * next.x + side * next.y, color);
                        previous = next;
                    }
                }
            }
        }
    }
}
