using System;
using System.Linq;
using GameLogic;
using UnityEditor;
using UnityEngine;

namespace FreshGuard.Editor
{
    public static class StoreIsolationPresentationBuilder
    {
        [MenuItem("FreshGuard/Skills/Connect Food Container Presentation")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行并等待编译。");
            const string root = "Assets/AssetRaw/Actor/Prefabs/";
            var robot = AssetDatabase.LoadAssetAtPath<GameObject>(root + "Robots/RobotFoodContainer.prefab");
            if (robot == null || robot.GetComponent<RobotView>()?.ConfigId != 703)
                throw new InvalidOperationException("703机器人预制体缺失或身份不匹配。");
            var bullet = AssetDatabase.LoadAssetAtPath<GameObject>(root + "bullets/RobotBullet.prefab");
            if (bullet == null || bullet.GetComponent<RobotBulletView>() == null)
                throw new InvalidOperationException("RobotBullet缺少投射物表现组件。");
            var path = root + "bullets/ContainerBullet.prefab";
            var instance = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var flight in instance.GetComponentsInChildren<RobotBulletView>(true))
                    UnityEngine.Object.DestroyImmediate(flight);
                var visual = instance.transform.Find("Visual");
                if (visual == null) throw new InvalidOperationException("装盒表现缺少Visual。");
                var animation = visual.GetComponent<BattleSpriteAnimation>() ?? visual.gameObject.AddComponent<BattleSpriteAnimation>();
                var close = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AssetRaw/Actor/Animations/Effects/ContainerClose.anim");
                var open = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AssetRaw/Actor/Animations/Effects/ContainerOpen.anim");
                if (close == null || open == null || close.isLooping || open.isLooping)
                    throw new InvalidOperationException("装盒动画必须存在且不循环。");
                var serialized = new SerializedObject(animation);
                serialized.FindProperty("_clip").objectReferenceValue = close;
                serialized.FindProperty("_loop").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                visual.GetComponent<Animator>().enabled = false;
                var view = instance.GetComponent<StoreIsolationEffectView>() ?? instance.AddComponent<StoreIsolationEffectView>();
                serialized = new SerializedObject(view);
                serialized.FindProperty("_animation").objectReferenceValue = animation;
                serialized.FindProperty("_close").objectReferenceValue = close;
                serialized.FindProperty("_open").objectReferenceValue = open;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(instance); }
            var profile = AssetDatabase.LoadAssetAtPath<DemoBattleProfile>("Assets/AssetRaw/Configs/DemoBattleProfile.asset");
            if (profile == null) throw new InvalidOperationException("缺少DemoBattleProfile。");
            if (!profile.Robots.Any(r => r.RobotId == 703))
            {
                profile.Robots.Add(new DemoRobotReference { RobotId = 703, Unlocked = true, ReferenceLevel = 5 });
                EditorUtility.SetDirty(profile);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[StoreIsolation] 703机器人、RobotBullet、装盒开合表现和演示候选已接线。");
        }
    }
}
