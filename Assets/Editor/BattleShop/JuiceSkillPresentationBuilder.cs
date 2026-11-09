using System;
using System.Linq;
using GameLogic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FreshGuard.Editor
{
    public static class JuiceSkillPresentationBuilder
    {
        [MenuItem("FreshGuard/Skills/Connect Juicer Presentation")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行并等待编译。");
            const string root = "Assets/AssetRaw/Actor/Prefabs/";
            var robot = AssetDatabase.LoadAssetAtPath<GameObject>(root + "Robots/RobotJuicer.prefab");
            if (robot == null || robot.GetComponent<RobotView>() == null || robot.GetComponent<RobotView>().ConfigId != 704)
                throw new InvalidOperationException("704机器人预制体缺失或身份不匹配。");
            var bullet = AssetDatabase.LoadAssetAtPath<GameObject>(root + "bullets/JuiceBullet.prefab");
            if (bullet == null || bullet.GetComponent<RobotBulletView>() == null)
                throw new InvalidOperationException("JuiceBullet缺少RobotBulletView。");
            var path = root + "bullets/JuiceBulletLandingEffect.prefab";
            var effect = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var visual = effect.transform.Find("Visual");
                if (visual == null) throw new InvalidOperationException("落地表现缺少Visual。");
                var animation = visual.GetComponent<BattleSpriteAnimation>() ?? visual.gameObject.AddComponent<BattleSpriteAnimation>();
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AssetRaw/Actor/Animations/Effects/JuiceBulletLanding.anim");
                if (clip == null || clip.isLooping) throw new InvalidOperationException("落地动画缺失或循环播放。");
                var serialized = new SerializedObject(animation);
                serialized.FindProperty("_clip").objectReferenceValue = clip;
                serialized.FindProperty("_loop").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                visual.GetComponent<Animator>().enabled = false;
                var view = effect.GetComponent<JuiceLandingEffectView>() ?? effect.AddComponent<JuiceLandingEffectView>();
                serialized = new SerializedObject(view);
                serialized.FindProperty("_animation").objectReferenceValue = animation;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(effect, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(effect); }
            var profile = AssetDatabase.LoadAssetAtPath<DemoBattleProfile>("Assets/AssetRaw/Configs/DemoBattleProfile.asset");
            if (profile == null) throw new InvalidOperationException("缺少演示入场配置。");
            if (!profile.Robots.Any(r => r.RobotId == 704))
                profile.Robots.Add(new DemoRobotReference { RobotId = 704, Unlocked = true, ReferenceLevel = 5 });
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("[JuiceSkill] 704与果汁弹检查完成，落地效果和演示候选已接线。");
        }
    }
}
