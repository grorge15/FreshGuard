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
    /// <summary>将三种怪物的死亡序列帧写入原片段，保留动画控制器与回收事件。</summary>
    public static class MonsterDeathEffectSetup
    {
        [Serializable]
        private sealed class ExportSettings
        {
            public MonsterSettings[] monsters;
        }

        [Serializable]
        private sealed class MonsterSettings
        {
            public string key;
            public string prefabName;
            public string animationName;
            public int frameCount;
            public float frameRate;
            public float pivotX;
            public float pivotY;
            public float pixelsPerUnit;
            public float completionTime;
        }

        [MenuItem("FreshGuard/Effects/Build Three Monster Deaths")]
        public static void Build()
        {
            BuildSelected(null);
        }

        [MenuItem("FreshGuard/Effects/Build Spinach Death")]
        public static void BuildSpinach()
        {
            BuildSelected("Spinach");
        }

        private static void BuildSelected(string key)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出播放模式后制作死亡资源。");
            var settings = JsonUtility.FromJson<ExportSettings>(File.ReadAllText("Tools/VFX/monster_death_import.json"));
            if (settings?.monsters == null || settings.monsters.Length != 3)
                throw new InvalidOperationException("请先运行 Tools/VFX/build_monster_deaths.py 生成三种怪物死亡帧。");
            foreach (var monster in settings.monsters)
                if (key == null || monster.key == key) BuildMonster(monster);
            AssetDatabase.SaveAssets();
        }

        private static void BuildMonster(MonsterSettings settings)
        {
            if (settings.frameCount < 2 || settings.frameRate <= 0f || settings.pixelsPerUnit <= 0f ||
                settings.completionTime <= (settings.frameCount - 1) / settings.frameRate)
                throw new InvalidOperationException("死亡帧参数无效：" + settings.key);
            var sprites = Enumerable.Range(0, settings.frameCount).Select(index => ImportFrame(settings, index)).ToArray();
            var clipPath = "Assets/AssetRaw/Actor/Animations/Enemy/" + settings.animationName + "/" + settings.animationName + "_Death.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null) throw new InvalidOperationException("缺少原死亡片段：" + clipPath);
            clip.ClearCurves();
            clip.frameRate = settings.frameRate;
            clip.wrapMode = WrapMode.ClampForever;
            var keys = new ObjectReferenceKeyframe[sprites.Length + 1];
            for (var i = 0; i < sprites.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / settings.frameRate, value = sprites[i] };
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

            var prefabPath = "Assets/AssetRaw/Actor/Prefabs/Prefabs/" + settings.prefabName + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var view = new SerializedObject(root.GetComponent<EnemyView>());
                view.FindProperty("_deathAnimationIncludesHitEffect").boolValue = true;
                view.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            RebuildAtlas(prefabPath, settings.animationName);
            // Remove surplus generated frames only after the clip and atlas
            // no longer reference them, including their Unity metadata.
            var frameFolder = "Assets/AssetRaw/Actor/Sprites/Enemy/Frame/" + settings.key + "/DeathImpact";
            var prefix = settings.key + "DeathImpact_";
            foreach (var path in Directory.GetFiles(frameFolder, prefix + "*.png"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(path).Substring(prefix.Length), out var index) && index >= settings.frameCount)
                    AssetDatabase.DeleteAsset(path.Replace('\\', '/'));
            Debug.Log("[MonsterDeathEffect] 已替换 " + settings.prefabName + "：" + sprites.Length + " 帧，" + settings.completionTime + " 秒后回收。");
        }

        private static Sprite ImportFrame(MonsterSettings settings, int index)
        {
            var path = "Assets/AssetRaw/Actor/Sprites/Enemy/Frame/" + settings.key + "/DeathImpact/" + settings.key + "DeathImpact_" + index.ToString("00") + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("缺少死亡帧：" + path);
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

        private static void RebuildAtlas(string prefabPath, string animationName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var sprites = prefab.GetComponent<Animator>().runtimeAnimatorController.animationClips
                .SelectMany(clip => AnimationUtility.GetObjectReferenceCurveBindings(clip)
                    .SelectMany(binding => AnimationUtility.GetObjectReferenceCurve(clip, binding)))
                .Select(key => key.value).OfType<Sprite>().Distinct().ToArray();
            var atlasPath = "Assets/AssetArt/Atlas/Frames_" + animationName + ".spriteatlasv2";
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            var asset = SpriteAtlasAsset.Load(atlasPath);
            if (atlas == null || asset == null) throw new InvalidOperationException("缺少图集：" + atlasPath);
            asset.Remove(atlas.GetPackables());
            asset.Add(sprites);
            SpriteAtlasAsset.Save(asset, atlasPath);
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
