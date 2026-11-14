using System;
using System.Linq;
using GameLogic;
using UnityEditor;
using UnityEngine;

namespace FreshGuard.Editor
{
    /// <summary>导入通用受击帧，并为所有 EnemyView 预制体绑定依赖，运行时无需额外加载或实例化。</summary>
    public static class EnemyHitEffectSetup
    {
        private const string FRAME_FOLDER = "Assets/AssetRaw/Effects/EnemyHit";
        private const string PREFAB_FOLDER = "Assets/AssetRaw/Actor/Prefabs";

        [MenuItem("FreshGuard/Effects/Build All Enemy Hit Effects")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出播放模式后制作受击资源。");
            var sprites = Enumerable.Range(0, 16).Select(ImportFrame).ToArray();
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { PREFAB_FOLDER })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<EnemyView>() != null).ToArray();
            if (paths.Length == 0) throw new InvalidOperationException("未找到怪物预制体。");
            foreach (var path in paths) BindPrefab(path, sprites);
            AssetDatabase.SaveAssets();
            Debug.Log("[EnemyHitEffect] 已为 " + paths.Length + " 个怪物预制体绑定16帧、32fps通用受击特效。");
        }

        private static void BindPrefab(string path, Sprite[] sprites)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var effect = root.GetComponentInChildren<EnemyHitEffect>(true);
                if (effect == null)
                {
                    var child = new GameObject("HitEffect");
                    child.layer = root.layer;
                    child.transform.SetParent(root.transform, false);
                    effect = child.AddComponent<EnemyHitEffect>();
                }
                var serializedEffect = new SerializedObject(effect);
                var frames = serializedEffect.FindProperty("_frames");
                frames.arraySize = sprites.Length;
                for (var i = 0; i < sprites.Length; i++) frames.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
                serializedEffect.FindProperty("_framesPerSecond").floatValue = 32f;
                serializedEffect.FindProperty("_visualSize").floatValue = 1.65f;
                serializedEffect.ApplyModifiedPropertiesWithoutUndo();
                var renderer = effect.GetComponent<SpriteRenderer>();
                var body = root.transform.Find("Visual").GetComponent<SpriteRenderer>();
                renderer.sharedMaterial = body.sharedMaterial;
                effect.SetSorting(body.sortingLayerID, body.sortingOrder);
                renderer.color = Color.white;
                renderer.flipX = false;
                effect.Clear();
                var serializedView = new SerializedObject(root.GetComponent<EnemyView>());
                serializedView.FindProperty("_hitEffect").objectReferenceValue = effect;
                serializedView.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Sprite ImportFrame(int index)
        {
            var path = FRAME_FOLDER + "/EnemyHit_" + index.ToString("00") + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("缺少受击帧：" + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 256f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(.5f, .5f);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("受击帧未导入为 Sprite：" + path);
            return sprite;
        }
    }
}
