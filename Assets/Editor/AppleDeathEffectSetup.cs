using System;
using System.IO;
using System.Linq;
using GameLogic;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace FreshGuard.Editor
{
    /// <summary>用透明果块死亡帧替换苹果原死亡片段，保留控制器引用和回收动画事件。</summary>
    public static class AppleDeathEffectSetup
    {
        private const string FRAME_FOLDER = "Assets/AssetRaw/Actor/Sprites/Enemy/Frame/Apple/DeathBurst";
        private const string CLIP_PATH = "Assets/AssetRaw/Actor/Animations/Enemy/AppleEnemy/AppleEnemy_Death.anim";
        private const string PREFAB_PATH = "Assets/AssetRaw/Actor/Prefabs/Prefabs/AppleEnemy.prefab";
        private const string ATLAS_PATH = "Assets/AssetArt/Atlas/Frames_AppleEnemy.spriteatlasv2";

        [Serializable]
        private sealed class BakeSettings
        {
            public int frameCount;
            public float frameRate;
            public float pivotX;
            public float pivotY;
            public float pixelsPerUnit;
            public float completionTime;
        }

        [MenuItem("FreshGuard/Effects/Build Apple Burst Death")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出播放模式后制作死亡资源。");
            var settings = JsonUtility.FromJson<BakeSettings>(File.ReadAllText("Tools/VFX/apple_death_import.json"));
            if (settings == null || settings.frameCount < 2 || settings.frameRate <= 0f || settings.pixelsPerUnit <= 0f)
                throw new InvalidOperationException("苹果死亡帧导入参数无效，请先运行 Tools/VFX/build_apple_death.py。");
            var sprites = Enumerable.Range(0, settings.frameCount).Select(index => ImportFrame(index, settings)).ToArray();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CLIP_PATH);
            if (clip == null) throw new InvalidOperationException("缺少原苹果死亡片段。");
            clip.ClearCurves();
            clip.frameRate = settings.frameRate;
            clip.wrapMode = WrapMode.ClampForever;
            var keys = new ObjectReferenceKeyframe[sprites.Length + 1];
            for (var i = 0; i < sprites.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / settings.frameRate, value = sprites[i] };
            // 末尾保留透明帧，动画事件触发回收前不会残留尸体或碎片。
            keys[sprites.Length] = new ObjectReferenceKeyframe { time = settings.completionTime, value = sprites[sprites.Length - 1] };
            AnimationUtility.SetObjectReferenceCurve(clip,
                EditorCurveBinding.PPtrCurve("Visual", typeof(SpriteRenderer), "m_Sprite"), keys);
            var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = false;
            clipSettings.startTime = 0f;
            clipSettings.stopTime = settings.completionTime + 1f / settings.frameRate;
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { time = settings.completionTime, functionName = "OnDeathAnimationComplete" }
            });
            EditorUtility.SetDirty(clip);

            var root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            try
            {
                var view = new SerializedObject(root.GetComponent<EnemyView>());
                view.FindProperty("_deathAnimationIncludesHitEffect").boolValue = true;
                view.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            RebuildAppleAtlas();
            Debug.Log("[AppleDeathEffect] 已替换苹果死亡动画：" + sprites.Length + "帧，回收事件位于 " + settings.completionTime + " 秒。");
        }

        private static Sprite ImportFrame(int index, BakeSettings settings)
        {
            var path = FRAME_FOLDER + "/AppleDeathBurst_" + index.ToString("00") + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("缺少苹果死亡帧：" + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = settings.pixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteAlignment = (int)SpriteAlignment.Custom;
            textureSettings.spritePivot = new Vector2(settings.pivotX, settings.pivotY);
            textureSettings.spriteMeshType = SpriteMeshType.FullRect;
            textureSettings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(textureSettings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void RebuildAppleAtlas()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            var sprites = prefab.GetComponent<Animator>().runtimeAnimatorController.animationClips
                .SelectMany(clip => AnimationUtility.GetObjectReferenceCurveBindings(clip)
                    .SelectMany(binding => AnimationUtility.GetObjectReferenceCurve(clip, binding)))
                .Select(key => key.value).OfType<Sprite>().Distinct().ToArray();
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(ATLAS_PATH);
            var asset = SpriteAtlasAsset.Load(ATLAS_PATH);
            if (atlas == null || asset == null) throw new InvalidOperationException("缺少苹果图集。");
            asset.Remove(atlas.GetPackables());
            asset.Add(sprites);
            SpriteAtlasAsset.Save(asset, ATLAS_PATH);
            AssetDatabase.ImportAsset(ATLAS_PATH, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
