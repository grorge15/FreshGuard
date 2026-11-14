#if UNITY_EDITOR
using System.IO;
using System.Linq;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class EnemyPlaybackTests
    {
        [TestCase(2001, 1.1f)]
        [TestCase(2002, 1.3f)]
        [TestCase(2003, 1.44f)]
        [TestCase(2004, 1.3f)]
        public void BakedDeathPausesCompletesOnceAndRestoresIdleOnReuse(int configId, float completionTime)
        {
            Assert.IsTrue(Application.isPlaying);
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            var config = tables.TbEnemy.Get(configId);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/Prefabs/" + config.EnemyPrefab + ".prefab"));
            using (var registry = new BattleEntityRegistry())
            {
                try
                {
                    var view = root.GetComponent<EnemyView>();
                    var animator = root.GetComponent<Animator>();
                    var id = registry.CreateEnemyFromConfig(config, BattleSide.Player);
                    view.Bind(registry, id);
                    animator.Update(0f);
                    var renderer = root.transform.Find("Visual").GetComponent<SpriteRenderer>();
                    var idle = renderer.sprite;
                    var completedId = 0;
                    var completionCount = 0;
                    view.RemovalAnimationCompleted += value => { completedId = value; completionCount++; };
                    Assert.IsTrue(view.PlayHitFeedback());
                    Assert.AreEqual(DamageResult.Killed, registry.ApplyDamage(id, int.MaxValue, out var removal));
                    Assert.IsTrue(view.PlayRemoval(removal));
                    animator.Update(0f);
                    Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Death"),
                        "致命命中后应直接进入死亡状态，不能等待移动或攻击动画播完。");
                    Assert.AreNotSame(idle, renderer.sprite, "致命命中后的首帧应显示死亡序列。");
                    animator.Update(.4f);
                    Assert.AreEqual(0, completionCount);
                    var pausedSprite = renderer.sprite;
                    animator.speed = 0f;
                    animator.Update(3f);
                    Assert.AreSame(pausedSprite, renderer.sprite);
                    Assert.AreEqual(0, completionCount);
                    animator.speed = 1f;
                    animator.Update(completionTime - .42f);
                    Assert.AreEqual(0, completionCount, "死亡尾段播完前不能提前回收。");
                    animator.Update(.04f);
                    Assert.AreEqual(1, completionCount, "真实 Animator 应通过末帧事件完成回收。");
                    Assert.AreEqual(id, completedId);
                    Assert.AreEqual(0, view.InstanceId);
                    animator.Update(1f);
                    view.OnDeathAnimationComplete();
                    Assert.AreEqual(1, completionCount);
                    var nextId = registry.CreateEnemyFromConfig(config, BattleSide.Player);
                    view.Bind(registry, nextId);
                    animator.Update(0f);
                    Assert.AreEqual("Idle", view.AnimationState);
                    Assert.AreSame(idle, renderer.sprite, "复用后不能保留透明死亡尾帧。");
                    Assert.IsTrue(view.PlayHitFeedback());
                    Assert.IsTrue(root.GetComponentInChildren<EnemyHitEffect>(true).IsPlaying);
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [TestCase(2001, 1, 100)]
        [TestCase(2002, 1, 350)]
        [TestCase(2003, 1, 1800)]
        [TestCase(2001, 11, 250)]
        [TestCase(2002, 2, 403)]
        [TestCase(2003, 20, 6930)]
        [TestCase(2004, 1, 120)]
        [TestCase(2004, 2, 138)]
        public void RuntimeLifecycleAndAnimationCompletionEvents(int configId, int wave, int expectedHp)
        {
            Assert.IsTrue(Application.isPlaying, "此测试须使用 PlayMode 执行");
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            var config = tables.TbEnemy.Get(configId);
            var prefabPath = AssetDatabase.FindAssets(config.EnemyPrefab + " t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Single(path => Path.GetFileNameWithoutExtension(path) == config.EnemyPrefab);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            using (var registry = new BattleEntityRegistry())
            {
                try
                {
                    var view = root.GetComponent<EnemyView>();
                    var id = registry.CreateEnemyFromConfig(config, BattleSide.Player, (wave - 1) / 10 + 1,
                        wave, tables.TbGlobal.Get(53).Value);
                    registry.TryGet(id, out var entity);
                    Assert.AreEqual(expectedHp, ((EnemyEntity)entity).CurrentHp);
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
                    registry.ApplyDamage(id, expectedHp, out removal);
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
