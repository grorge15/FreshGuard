#if UNITY_EDITOR
using System.IO;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class EnemyPlaybackTests
    {
        [TestCase(2001)]
        [TestCase(2002)]
        [TestCase(2003)]
        public void RuntimeLifecycleAndAnimationCompletionEvents(int configId)
        {
            Assert.IsTrue(Application.isPlaying, "此测试须使用 PlayMode 执行");
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            var config = tables.TbEnemy.Get(configId);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Enemy/Prefabs/" + config.EnemyPrefab + ".prefab"));
            using (var registry = new BattleEntityRegistry())
            {
                try
                {
                    var view = root.GetComponent<EnemyView>();
                    var id = registry.CreateEnemyFromConfig(config, BattleSide.Player);
                    view.Bind(registry, id);
                    var animator = root.GetComponent<Animator>();
                    animator.Update(0.3f);
                    var idleTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    view.SetMoving(false);
                    animator.Update(0f);
                    Assert.GreaterOrEqual(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, idleTime,
                        "连续 Idle 请求不能从第 0 帧重播");
                    view.SetMoving(true);
                    view.PlayAttack();
                    animator.Update(0f);
                    var attackClip = System.Array.Find(animator.runtimeAnimatorController.animationClips,
                        clip => clip.name.EndsWith("_Attack"));
                    animator.Update(attackClip.length + 0.01f);
                    Assert.AreEqual("Move", view.AnimationState, "真实动画事件应恢复移动");
                    EntityRemovalSnapshot removal;
                    registry.ApplyDamage(id, config.HpC1, out removal);
                    Assert.IsTrue(view.PlayRemoval(removal));
                    var completions = 0;
                    view.RemovalAnimationCompleted += _ => completions++;
                    animator.Update(0f);
                    var deathClip = System.Array.Find(animator.runtimeAnimatorController.animationClips,
                        clip => clip.name.EndsWith("_Death"));
                    animator.Update(deathClip.length + 0.01f);
                    Assert.AreEqual(1, completions, "真实死亡动画事件应完成回收通知");
                    animator.Update(0.1f);
                    Assert.AreEqual(1, completions);
                    var nextId = registry.CreateEnemyFromConfig(config, BattleSide.Player);
                    view.Bind(registry, nextId);
                    view.SetMoving(true);
                    root.SetActive(false);
                    root.SetActive(true);
                    Assert.AreEqual(0, view.InstanceId);
                    Assert.AreEqual("Idle", view.AnimationState);
                }
                finally { Object.DestroyImmediate(root); }
            }
        }
    }
}
#endif
