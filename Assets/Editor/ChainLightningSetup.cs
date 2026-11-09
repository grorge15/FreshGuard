using System;
using System.IO;
using System.Linq;
using GameLogic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FreshGuard.Editor
{
    /// <summary>由 Editor 显式重建序列帧动画与闪电预制体，保留配置中的资源地址。</summary>
    public static class ChainLightningSetup
    {
        private const string Folder = "Assets/AssetRaw/Actor/Prefabs/bullets";
        private const string AnimationFolder = "Assets/AssetRaw/Actor/Animations/Effects";
        private const string SpriteFolder = "Assets/AssetRaw/Actor/Sprites/Bullets/闪电";
        private const string BoltPath = Folder + "/InductionBolt.prefab";
        private const string EffectPath = Folder + "/LightningChainEffect.prefab";
        private const float FrameRate = 24f;

        [MenuItem("FreshGuard/Skills/Build Chain Lightning Assets")]
        public static void Build()
        {
            RequireEditMode();
            if (!AssetDatabase.IsValidFolder(AnimationFolder))
                AssetDatabase.CreateFolder("Assets/AssetRaw/Actor/Animations", "Effects");
            var spark = BuildClip("LightningSpark", "闪电火花", 10, new Vector2(0.5f, 0.5f));
            // 帧画布为512x128，左侧电光起点在x=56。各帧保持同一支点，增长时不漂移。
            var chain = BuildClip("LightningArc", "闪电链", 5, new Vector2(56f / 512f, 0.5f));
            BuildBolt(chain);
            BuildEffect(chain, spark);
            AssetDatabase.DeleteAsset(Folder + "/LightningChain.mat");
            AssetDatabase.SaveAssets();
            Debug.Log("[ChainLightningSetup] 已制作两段24fps序列帧动画，并重建闪电预制体、清除旧线条材质。");
        }

        private static AnimationClip BuildClip(string name, string source, int expectedCount, Vector2 pivot)
        {
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { SpriteFolder + "/" + source })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
            if (paths.Length != expectedCount) throw new InvalidOperationException("序列帧数量不匹配：" + source);
            foreach (var path in paths)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = pivot;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            var sprites = paths.Select(AssetDatabase.LoadAssetAtPath<Sprite>).ToArray();
            if (sprites.Any(sprite => sprite == null)) throw new InvalidOperationException("序列帧未导入为Sprite：" + source);
            var assetPath = AnimationFolder + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                clip = new AnimationClip { name = name };
                AssetDatabase.CreateAsset(clip, assetPath);
            }
            clip.ClearCurves();
            clip.frameRate = FrameRate;
            clip.wrapMode = WrapMode.ClampForever;
            // Unity 的 Sprite 曲线会为末帧自动保留一个帧间隔。
            var keys = new ObjectReferenceKeyframe[sprites.Length];
            for (var i = 0; i < sprites.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / FrameRate, value = sprites[i] };
            AnimationUtility.SetObjectReferenceCurve(clip,
                EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite"), keys);
            var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static void BuildBolt(AnimationClip clip)
        {
            // 从空对象重建，旧ElectricArc、Sprite和材质引用均不会残留。
            var root = new GameObject("InductionBolt");
            try
            {
                root.transform.localScale = Vector3.one * 0.08f;
                var sequence = AddSequence(root, clip, true, 21);
                root.AddComponent<RobotBulletView>();
                sequence.Sample(0f);
                Save(root, BoltPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildEffect(AnimationClip chainClip, AnimationClip sparkClip)
        {
            var root = new GameObject("LightningChainEffect");
            try
            {
                var chainRoot = new GameObject("ChainSegments").transform;
                chainRoot.SetParent(root.transform, false);
                var sparkRoot = new GameObject("HitSparks").transform;
                sparkRoot.SetParent(root.transform, false);
                var chain = new GameObject("ChainTemplate");
                chain.transform.SetParent(chainRoot, false);
                var chainSequence = AddSequence(chain, chainClip, false, 24);
                var spark = new GameObject("SparkTemplate");
                spark.transform.SetParent(sparkRoot, false);
                spark.transform.localScale = Vector3.one * 0.22f;
                var sparkSequence = AddSequence(spark, sparkClip, false, 25);
                var view = root.AddComponent<LightningChainEffectView>();
                var serialized = new SerializedObject(view);
                serialized.FindProperty("_chainTemplate").objectReferenceValue = chainSequence;
                serialized.FindProperty("_sparkTemplate").objectReferenceValue = sparkSequence;
                serialized.FindProperty("Lifetime").floatValue = sparkClip.length;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                view.Clear();
                Save(root, EffectPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static BattleSpriteAnimation AddSequence(GameObject root, AnimationClip clip, bool loop, int order)
        {
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.color = Color.white;
            renderer.sortingOrder = order;
            var player = root.AddComponent<BattleSpriteAnimation>();
            // SampleAnimation 需要 Animator 绑定；关闭自动推进，时间由战斗驱动。
            root.GetComponent<Animator>().enabled = false;
            var serialized = new SerializedObject(player);
            serialized.FindProperty("_clip").objectReferenceValue = clip;
            serialized.FindProperty("_loop").boolValue = loop;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            clip.SampleAnimation(root, 0f);
            return player;
        }

        private static void Save(GameObject root, string path)
        {
            if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                throw new InvalidOperationException("预制体保存失败：" + path);
        }

        [MenuItem("FreshGuard/Skills/Verify Chain Lightning Effect Lifecycle")]
        public static void VerifyEffectLifecycle()
        {
            RequireEditMode();
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EffectPath));
            try
            {
                var view = root.GetComponent<LightningChainEffectView>();
                view.Play(new[] { Vector3.zero, Vector3.right });
                if (root.GetComponentsInChildren<LineRenderer>(true).Length != 0 || view.Advance(0f) ||
                    view.Advance(view.Lifetime * 0.5f) || !view.Advance(view.Lifetime))
                    throw new InvalidOperationException("序列帧播放、暂停或回收验证失败。");
                view.Play(new[] { Vector3.one });
                if (root.GetComponentsInChildren<SpriteRenderer>().Count(r => r.enabled) != 1)
                    throw new InvalidOperationException("单目标必须只显示火花。");
                view.Clear();
                Debug.Log("[ChainLightningSetup] 序列帧生命周期验证通过。");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行并等待编译完成。");
        }
    }
}
