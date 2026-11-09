using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FreshGuard.Editor
{
    /// <summary>仅由 Editor 菜单装配资源，不在导入或域重载时自动写入。</summary>
    public static class ChainLightningSetup
    {
        private const string Folder = "Assets/AssetRaw/Actor/Prefabs/bullets";
        private const string SourcePath = Folder + "/RobotBullet.prefab";
        private const string BoltPath = Folder + "/InductionBolt.prefab";
        private const string EffectPath = Folder + "/LightningChainEffect.prefab";
        private const string MaterialPath = Folder + "/LightningChain.mat";
        private const string ProfilePath = "Assets/AssetRaw/Configs/DemoBattleProfile.asset";

        [MenuItem("FreshGuard/Skills/Build Chain Lightning Assets")]
        public static void Build()
        {
            RequireEditMode();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            var bulletType = RequireType("GameLogic.RobotBulletView");
            var effectType = RequireType("GameLogic.LightningChainEffectView");
            if (source == null || source.GetComponent(bulletType) == null)
                throw new InvalidOperationException("RobotBullet 预制体缺失或没有 RobotBulletView：" + SourcePath);
            var profile = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ProfilePath);
            if (profile == null || profile.GetType().FullName != "GameLogic.DemoBattleProfile")
                throw new InvalidOperationException("演示配置不存在或类型错误：" + ProfilePath);
            var robots = new SerializedObject(profile).FindProperty("Robots");
            if (robots == null || !robots.isArray)
                throw new InvalidOperationException("DemoBattleProfile 缺少 Robots 列表。");
            var material = LoadMaterial();
            BuildBolt(material, bulletType);
            BuildEffect(material, effectType);
            AppendRobot(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("[ChainLightningSetup] 已保存 InductionBolt、LightningChainEffect 和 701 演示配置。");
        }

        // 聚焦表现生命周期检查，不依赖战斗实体、资源加载器或场景。
        [MenuItem("FreshGuard/Skills/Verify Chain Lightning Effect Lifecycle")]
        public static void VerifyEffectLifecycle()
        {
            RequireEditMode();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EffectPath);
            if (prefab == null) throw new InvalidOperationException("请先执行 Build Chain Lightning Assets。");
            var root = Object.Instantiate(prefab);
            root.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var type = RequireType("GameLogic.LightningChainEffectView");
                var view = root.GetComponent(type);
                Require(view != null, "Prefab 缺少 LightningChainEffectView。");
                var play = type.GetMethod("Play");
                var advance = type.GetMethod("Advance");
                var clear = type.GetMethod("Clear");
                type.GetField("Lifetime").SetValue(view, 0.2f);
                var line = root.transform.Find("ChainLine").GetComponent<LineRenderer>();
                var flash = root.transform.Find("HitFlash").GetComponent<LineRenderer>();
                var points = new List<Vector3> { new Vector3(1f, 2f, 0f), new Vector3(3f, 4f, 0f) };
                play.Invoke(view, new object[] { points });
                Require(line.enabled && flash.enabled, "链线和首名闪光应同时显示。");
                Require(line.GetPosition(0) == points[0] &&
                    line.GetPosition(line.positionCount - 1) == points[1], "连线端点必须是命中快照位置。");
                points[0] = Vector3.zero;
                Require(line.GetPosition(0) == new Vector3(1f, 2f, 0f), "Play 后不得追踪输入列表变化。");
                Require(!(bool)advance.Invoke(view, new object[] { 0f }), "零战斗时间不得到期。");
                Require(!(bool)advance.Invoke(view, new object[] { 0.1f }), "寿命中途不得到期。");
                Require((bool)advance.Invoke(view, new object[] { 0.11f }), "超过寿命必须到期。");
                Require(!line.enabled && !flash.enabled && line.positionCount == 0 && flash.positionCount == 0,
                    "到期必须清除表现几何。");
                play.Invoke(view, new object[] { new[] { Vector3.one } });
                Require(!line.enabled && flash.enabled, "单目标命中也必须显示闪光。");
                clear.Invoke(view, null);
                Require((bool)advance.Invoke(view, new object[] { 0f }) && !flash.enabled,
                    "Clear 后必须处于可回收状态。");
                play.Invoke(view, new object[] { Array.Empty<Vector3>() });
                Require((bool)advance.Invoke(view, new object[] { 0f }), "空位置不得残留上次表现。");
                Debug.Log("[ChainLightningSetup] 表现生命周期验证通过：位置快照、零时间暂停、到期清理、单目标闪光、重复使用。");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildBolt(Material material, Type bulletType)
        {
            var exists = AssetDatabase.LoadAssetAtPath<GameObject>(BoltPath) != null;
            var root = PrefabUtility.LoadPrefabContents(exists ? BoltPath : SourcePath);
            try
            {
                root.name = "InductionBolt";
                if (root.GetComponent(bulletType) == null) root.AddComponent(bulletType);
                root.transform.localScale = Vector3.one * 0.2f;
                var sprite = root.GetComponent<SpriteRenderer>();
                if (sprite != null)
                {
                    sprite.color = new Color(0.48f, 0.92f, 1f, 1f);
                    sprite.sharedMaterial = material;
                }
                var arc = ConfigureLine(root, "ElectricArc", material, 0.12f, 21, false);
                arc.startColor = arc.endColor = new Color(0.12f, 0.6f, 1f, 1f);
                arc.positionCount = 5;
                arc.SetPositions(new[]
                {
                    new Vector3(-0.75f, 0f, 0f), new Vector3(-0.25f, 0.25f, 0f),
                    Vector3.zero, new Vector3(0.25f, -0.25f, 0f), new Vector3(0.75f, 0f, 0f)
                });
                arc.enabled = true;
                if (PrefabUtility.SaveAsPrefabAsset(root, BoltPath) == null)
                    throw new InvalidOperationException("保存电光弹失败：" + BoltPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void BuildEffect(Material material, Type effectType)
        {
            var exists = AssetDatabase.LoadAssetAtPath<GameObject>(EffectPath) != null;
            var root = exists ? PrefabUtility.LoadPrefabContents(EffectPath) : new GameObject("LightningChainEffect");
            try
            {
                root.name = "LightningChainEffect";
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                var line = ConfigureLine(root, "ChainLine", material, 0.035f, 24, true);
                var flash = ConfigureLine(root, "HitFlash", material, 0.025f, 25, true);
                line.startColor = line.endColor = new Color(0.12f, 0.65f, 1f, 1f);
                flash.startColor = flash.endColor = new Color(0.8f, 0.97f, 1f, 1f);
                var view = root.GetComponent(effectType);
                if (view == null) view = root.AddComponent(effectType);
                var serialized = new SerializedObject(view);
                serialized.FindProperty("_chainLine").objectReferenceValue = line;
                serialized.FindProperty("_hitFlash").objectReferenceValue = flash;
                serialized.FindProperty("Lifetime").floatValue = 0.2f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.SaveAsPrefabAsset(root, EffectPath) == null)
                    throw new InvalidOperationException("保存闪电链表现失败：" + EffectPath);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        private static LineRenderer ConfigureLine(GameObject root, string name, Material material,
            float width, int order, bool worldSpace)
        {
            var child = root.transform.Find(name);
            if (child == null)
            {
                child = new GameObject(name).transform;
                child.SetParent(root.transform, false);
            }
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            child.gameObject.layer = root.layer;
            var line = child.GetComponent<LineRenderer>();
            if (line == null) line = child.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = worldSpace;
            line.alignment = LineAlignment.TransformZ;
            line.widthMultiplier = width;
            line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.loop = false;
            line.sortingOrder = order;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.positionCount = 0;
            line.enabled = false;
            return line;
        }

        private static Material LoadMaterial()
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("找不到 Sprites/Default 着色器。");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "LightningChain" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.color = Color.white;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AppendRobot(ScriptableObject profile)
        {
            var serialized = new SerializedObject(profile);
            var robots = serialized.FindProperty("Robots");
            SerializedProperty robot = null;
            for (var i = 0; i < robots.arraySize; i++)
            {
                var entry = robots.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("RobotId").intValue == 701) { robot = entry; break; }
            }
            if (robot == null)
            {
                var index = robots.arraySize;
                robots.InsertArrayElementAtIndex(index);
                robot = robots.GetArrayElementAtIndex(index);
            }
            robot.FindPropertyRelative("RobotId").intValue = 701;
            robot.FindPropertyRelative("Unlocked").boolValue = true;
            robot.FindPropertyRelative("ReferenceLevel").intValue = 5;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }

        private static Type RequireType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            throw new InvalidOperationException("类型尚未编译或不存在：" + fullName);
        }

        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行并等待编译完成后执行闪电链菜单。");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
