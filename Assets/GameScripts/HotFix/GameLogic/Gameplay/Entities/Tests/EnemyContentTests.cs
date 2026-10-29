using System;
using System.IO;
using System.Linq;
using GameConfig;
using GameConfig.enemy;
using Luban;
using NUnit.Framework;
using TEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class EnemyContentTests
    {
        private Tables _tables;

        [SetUp]
        public void SetUp()
        {
            _tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
        }

        [TestCase(2001, "SkeletonEnemy", 1, 100)]
        [TestCase(2001, "SkeletonEnemy", 2, 220)]
        [TestCase(2002, "EliteSkeletonEnemy", 1, 350)]
        [TestCase(2002, "EliteSkeletonEnemy", 2, 770)]
        [TestCase(2003, "SkeletonBossEnemy", 1, 1800)]
        [TestCase(2003, "SkeletonBossEnemy", 2, 3960)]
        public void ConfigCreatesConcretePooledEnemyWithCycleHp(int configId, string expectedType, int cycle, int hp)
        {
            using (var registry = new BattleEntityRegistry())
            {
                var config = _tables.TbEnemy.Get(configId);
                var id = registry.CreateEnemyFromConfig(config, BattleSide.Opponent, cycle);
                Entity entity;
                Assert.IsTrue(registry.TryGet(id, out entity));
                var enemy = (EnemyEntity)entity;
                Assert.AreEqual(expectedType, enemy.GetType().Name);
                Assert.AreEqual(hp, enemy.CurrentHp);
                Assert.AreEqual(hp, enemy.MaxHp);
                Assert.AreEqual(config.EnemySpeed, enemy.BaseSpeed);
                Assert.AreEqual(configId, enemy.ConfigId);
                Assert.AreEqual(BattleSide.Opponent, enemy.Side);
                var poolType = enemy.GetType();
                var usingCount = PoolInfo(poolType).UsingMemoryCount;
                EntityRemovalSnapshot snapshot;
                Assert.AreEqual(DamageResult.Damaged, registry.ApplyDamage(id, hp - 1, out snapshot));
                Assert.AreEqual(DamageResult.Killed, registry.ApplyDamage(id, 1, out snapshot));
                Assert.AreEqual(0, snapshot.EnemyHp);
                Assert.AreEqual(configId, snapshot.ConfigId);
                Assert.AreEqual(EntityRemovalReason.Killed, snapshot.RemovalReason);
                Assert.AreEqual(usingCount - 1, PoolInfo(poolType).UsingMemoryCount);
                Assert.IsFalse(registry.TryGet(id, out entity));
                Assert.IsFalse(registry.Remove(id, EntityRemovalReason.Manual, out snapshot));
                var reusedId = registry.CreateEnemyFromConfig(config, BattleSide.Player, cycle == 1 ? 2 : 1);
                Assert.IsTrue(registry.TryGet(reusedId, out entity));
                var reused = (EnemyEntity)entity;
                Assert.AreSame(config, reused.Configuration);
                Assert.AreEqual(cycle == 1 ? 2 : 1, reused.Cycle);
                Assert.AreEqual(reused.Cycle == 1 ? config.HpC1 : config.HpC2, reused.CurrentHp);
                Assert.AreEqual(0f, reused.RouteProgress);
            }
        }

        [Test]
        public void InvalidConfigurationDoesNotAcquireOrConsumeIdentity()
        {
            using (var registry = new BattleEntityRegistry())
            {
                var config = _tables.TbEnemy.Get(2001);
                Assert.Throws<ArgumentNullException>(() => registry.CreateEnemyFromConfig(null, BattleSide.Player));
                Assert.Throws<ArgumentOutOfRangeException>(() => registry.CreateEnemyFromConfig(config, BattleSide.Player, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => registry.CreateEnemyFromConfig(config, BattleSide.Player, 3));
                Assert.Throws<ArgumentOutOfRangeException>(() => registry.CreateEnemyFromConfig(config, (BattleSide)99));
                Assert.Throws<ArgumentException>(() => registry.CreateEnemyFromConfig(Config(9999), BattleSide.Player));
                Assert.Throws<ArgumentOutOfRangeException>(() => registry.CreateEnemyFromConfig(Config(2001, float.NaN), BattleSide.Player));
                Assert.AreEqual(1, registry.CreateEnemyFromConfig(config, BattleSide.Player));
                registry.Dispose();
                Assert.Throws<ObjectDisposedException>(() => registry.CreateEnemyFromConfig(config, BattleSide.Player));
            }
        }

        [TestCase(2001, "骷髅兵", 8, 7, 8, 10)]
        [TestCase(2002, "二级骷髅怪", 6, 8, 6, 6)]
        [TestCase(2003, "骷髅BOSS", 12, 8, 12, 16)]
        public void PrefabContainsAllOrderedFramesAndAnimationCompletionEvents(int configId, string folder, int idle, int move, int attack, int death)
        {
            var config = _tables.TbEnemy.Get(configId);
            Assert.IsEmpty(config.BuffId);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Enemy/Prefabs/" + config.EnemyPrefab + ".prefab");
            Assert.IsNotNull(prefab);
            var view = prefab.GetComponent("EnemyView");
            Assert.IsNotNull(view);
            Assert.AreEqual(configId, new SerializedObject(view).FindProperty("_configId").intValue);
            var animator = prefab.GetComponent<Animator>();
            Assert.IsNotNull(animator);
            var controller = animator.runtimeAnimatorController as AnimatorController;
            Assert.IsNotNull(controller);
            Assert.AreEqual("Idle", controller.layers[0].stateMachine.defaultState.name);
            var names = new[] { "Idle", "Move", "Attack", "Death" };
            var actions = new[] { "待机", "移动", "攻击", "死亡" };
            var counts = new[] { idle, move, attack, death };
            for (var i = 0; i < names.Length; i++)
            {
                var state = controller.layers[0].stateMachine.states.Single(s => s.state.name == names[i]).state;
                var clip = state.motion as AnimationClip;
                Assert.IsNotNull(clip);
                Assert.AreEqual(i < 2, clip.isLooping);
                Assert.AreEqual(10f, clip.frameRate);
                var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
                Assert.AreEqual("Visual", binding.path);
                Assert.AreEqual("m_Sprite", binding.propertyName);
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                // 最后额外保持一帧，确保末帧具有完整的显示时长。
                Assert.AreEqual(counts[i] + 1, keys.Length);
                for (var k = 0; k < counts[i]; k++)
                {
                    Assert.IsInstanceOf<Sprite>(keys[k].value);
                    var path = AssetDatabase.GetAssetPath(keys[k].value);
                    StringAssert.Contains("/" + folder + "/", path);
                    StringAssert.Contains(actions[i], path);
                    Assert.AreEqual(k / 10f, keys[k].time, 0.0001f);
                    if (k > 0)
                    {
                        var previous = AssetDatabase.GetAssetPath(keys[k - 1].value);
                        Assert.Less(FrameNumber(previous), FrameNumber(path));
                    }
                }
                Assert.AreSame(keys[counts[i] - 1].value, keys[counts[i]].value);
                Assert.AreEqual(counts[i] / 10f, keys[counts[i]].time, 0.0001f);
                if (i >= 2)
                    Assert.AreEqual(i == 2 ? "OnAttackAnimationComplete" : "OnDeathAnimationComplete",
                        AnimationUtility.GetAnimationEvents(clip).Single().functionName);
            }
            var renderer = prefab.transform.Find("Visual").GetComponent<SpriteRenderer>();
            Assert.IsNotNull(renderer.sprite);
            Assert.AreEqual(Color.white, renderer.color);
        }

        private static int FrameNumber(string path)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(Path.GetFileNameWithoutExtension(path), "[0-9]+");
            return int.Parse(matches[matches.Count - 1].Value);
        }

        private static Enemy Config(int id, float speed = 0.6f)
        {
            var buffer = new ByteBuf();
            buffer.WriteInt(id); buffer.WriteString("Test"); buffer.WriteInt(1); buffer.WriteInt(1);
            buffer.WriteString("SkeletonEnemy"); buffer.WriteInt(100); buffer.WriteInt(220); buffer.WriteFloat(speed);
            buffer.WriteInt(1); buffer.WriteFloat(1f); buffer.WriteFloat(1f); buffer.WriteSize(0);
            return new Enemy(buffer);
        }

        private static MemoryPoolInfo PoolInfo(Type type)
        {
            return MemoryPool.GetAllMemoryPoolInfos().Single(info => info.Type == type);
        }
    }
}
