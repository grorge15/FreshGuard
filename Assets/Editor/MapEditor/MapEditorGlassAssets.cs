using System;
using System.Collections.Generic;
using System.IO;
using GameConfig;
using GameConfig.glass;
using GameLogic;
using UnityEditor;
using UnityEngine;

namespace FreshGuard.Editor
{
    /// <summary>与GlassPrefabBuilder、GlassView共用既有目录和配置映射，不依赖运行时ConfigSystem。</summary>
    internal sealed class MapEditorGlassAssets
    {
        private const string GLASS_ROOT = "Assets/AssetRaw/Actor/Glass/";
        private readonly Dictionary<TerrainType, GameObject> _prefabs = new Dictionary<TerrainType, GameObject>();
        private readonly List<string> _errors = new List<string>();

        public IReadOnlyList<string> Errors => _errors;

        public GameObject GetPrefab(TerrainType terrain)
        {
            return _prefabs.TryGetValue(terrain, out var prefab) ? prefab : null;
        }

        public void Reload(MapData map)
        {
            _prefabs.Clear();
            _errors.Clear();
            try
            {
                ValidatePlacementSlot();
                var tables = new Tables(name => new Luban.ByteBuf(File.ReadAllBytes(
                    Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes/" + name + ".bytes"))));
                ValidateGlass(tables.TbGlass.GetOrDefault(map.NormalGlassId), map.NormalGlassId, 0, TerrainType.GlassNormal);
                ValidateGlass(tables.TbGlass.GetOrDefault(map.ColoredGlassId), map.ColoredGlassId, 1, TerrainType.GlassColored);
            }
            catch (Exception exception)
            {
                _errors.Add("无法读取Luban玻璃配置：" + exception.Message);
            }
        }

        private void ValidatePlacementSlot()
        {
            const string path = "Assets/AssetRaw/Physics/Prefabs/PlacementSlot.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var renderer = prefab == null ? null : prefab.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null || renderer.sprite.bounds.size.x <= 0f || renderer.sprite.bounds.size.y <= 0f)
                _errors.Add("PlacementSlot缺少有效的SpriteRenderer或Sprite：" + path);
            if (prefab == null) return;
            if (prefab.GetComponentInChildren<Collider2D>(true) != null || prefab.GetComponentInChildren<Rigidbody2D>(true) != null)
                _errors.Add("PlacementSlot底图不能包含碰撞体或刚体：" + path);
            _prefabs[TerrainType.Track] = prefab;
            _prefabs[TerrainType.Empty] = prefab;
        }

        private void ValidateGlass(Glass config, int id, int kind, TerrainType terrain)
        {
            var label = (kind == 0 ? "普通" : "彩色") + "玻璃 ID=" + id + "：";
            if (config == null) { _errors.Add(label + "TbGlass中不存在此ID。"); return; }
            if (config.Kind != kind) _errors.Add(label + "Kind应为" + kind + "，当前为" + config.Kind + "。");
            if (config.MaxDurability <= 0 || config.HitDurabilityLoss <= 0)
                _errors.Add(label + "耐久和每次命中损失必须大于0。");

            var prefabPath = GLASS_ROOT + "Prefabs/" + config.PrefabLocation + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) _errors.Add(label + "缺少Prefab：" + prefabPath);
            else _prefabs[terrain] = prefab;

            var stages = config.StageSprites;
            var ratios = config.StageUpperRatios;
            if (stages == null || stages.Length == 0 || ratios == null || stages.Length != ratios.Length)
            {
                _errors.Add(label + "阶段图片与耐久上限必须非空且数量一致。");
                return;
            }

            var view = prefab == null ? null : prefab.GetComponent<GlassView>();
            SerializedProperty references = null;
            SerializedObject serialized = null;
            if (prefab != null)
            {
                if (prefab.GetComponent<BoxCollider2D>() == null) _errors.Add(label + "Prefab缺少BoxCollider2D。");
                if (view == null) _errors.Add(label + "Prefab根节点缺少GlassView。");
                else
                {
                    serialized = new SerializedObject(view);
                    references = serialized.FindProperty("_stageSprites");
                    if (references == null || references.arraySize != stages.Length)
                        _errors.Add(label + "Prefab阶段Sprite引用数量与配置不匹配。");
                    var renderer = serialized.FindProperty("_renderer");
                    if (renderer == null || !(renderer.objectReferenceValue is SpriteRenderer spriteRenderer) ||
                        (spriteRenderer.transform != prefab.transform && !spriteRenderer.transform.IsChildOf(prefab.transform)))
                        _errors.Add(label + "GlassView的SpriteRenderer引用缺失或不属于该Prefab。");
                }
            }
            try
            {
                for (var index = 0; index < stages.Length; index++)
                {
                    var ratio = ratios[index];
                    if (float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 0f || ratio > 1f ||
                        (index > 0 && ratio <= ratios[index - 1]))
                        _errors.Add(label + "阶段" + index + "耐久比上限必须在(0,1]内递增。");
                    var spritePath = GLASS_ROOT + "Sprites/" + stages[index] + ".png";
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                    if (sprite == null) _errors.Add(label + "缺少Sprite：" + spritePath);
                    if (references != null && index < references.arraySize &&
                        references.GetArrayElementAtIndex(index).objectReferenceValue != sprite)
                        _errors.Add(label + "Prefab阶段" + index + "引用与配置Sprite不一致：" + stages[index]);
                }
                if (ratios[ratios.Length - 1] != 1f) _errors.Add(label + "最后一阶段耐久比上限必须为1。");
            }
            finally
            {
                serialized?.Dispose();
            }
        }
    }
}
