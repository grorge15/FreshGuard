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
        [TestCase(2001, "SkeletonEnemy", 2, 115)]
        [TestCase(2001, "SkeletonEnemy", 10, 235)]
        [TestCase(2001, "SkeletonEnemy", 11, 250)]
        [TestCase(2001, "SkeletonEnemy", 20, 385)]
        [TestCase(2001, "SkeletonEnemy", 21, 400)]
        [TestCase(2002, "EliteSkeletonEnemy", 1, 350)]
        [TestCase(2002, "EliteSkeletonEnemy", 2, 403)]
        [TestCase(2003, "SkeletonBossEnemy", 1, 1800)]
        [TestCase(2003, "SkeletonBossEnemy", 20, 6930)]
        [TestCase(2004, "AppleEnemy", 1, 120)]
        [TestCase(2004, "AppleEnemy", 2, 138)]
        [TestCase(2004, "AppleEnemy", 20, 462)]
        public void ConfigCreatesConcretePooledEnemyWithWaveHp(int configId, string expectedType, int wave, int hp)
        {
            using (var registry = new BattleEntityRegistry())
            {
                var config = _tables.TbEnemy.Get(configId);
                var cycle = (wave - 1) / 10 + 1;
                var growth = _tables.TbGlobal.Get(53).Value;
                var id = registry.CreateEnemyFromConfig(config, BattleSide.Opponent, cycle, wave, growth);
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
                var reusedId = registry.CreateEnemyFromConfig(config, BattleSide.Player, 1, 1, growth);
                Assert.IsTrue(registry.TryGet(reusedId, out entity));
                var reused = (EnemyEntity)entity;
                Assert.AreSame(config, reused.Configuration);
                Assert.AreEqual(1, reused.Cycle);
                Assert.AreEqual(1, reused.Wave);
                Assert.AreEqual(config.Hp, reused.CurrentHp);
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
                Assert.Throws<ArgumentOutOfRangeException>(() => registry.CreateEnemyFromConfig(config, BattleSide.Player, wave: 0));
                foreach (var growth in new[] { -0.15f, float.NaN, float.PositiveInfinity, float.MaxValue })
                    Assert.Throws<ArgumentOutOfRangeException>(() => registry.CreateEnemyFromConfig(
                        config, BattleSide.Player, wave: 2, hpGrowthPerWave: growth));
                Assert.Throws<ArgumentOutOfRangeException>(() => registry.CreateEnemyFromConfig(config, (BattleSide)99));
                Assert.Throws<ArgumentException>(() => registry.CreateEnemyFromConfig(Config(9999), BattleSide.Player));
                Assert.Throws<ArgumentOutOfRangeException>(() => registry.CreateEnemyFromConfig(Config(2001, float.NaN), BattleSide.Player));
                Assert.AreEqual(1, registry.CreateEnemyFromConfig(config, BattleSide.Player));
                registry.Dispose();
                Assert.Throws<ObjectDisposedException>(() => registry.CreateEnemyFromConfig(config, BattleSide.Player));
            }
        }

        [TestCase(2001, 0f, 20, 100)]
        [TestCase(2001, 0.3f, 2, 130)]
        [TestCase(2002, 0.35f, 2, 473)]
        public void GrowthCanBeDisabledOrConfigured(int configId, float growth, int wave, int expectedHp)
        {
            using (var registry = new BattleEntityRegistry())
            {
                var id = registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(configId), BattleSide.Player,
                    wave: wave, hpGrowthPerWave: growth);
                registry.TryGet(id, out var entity);
                Assert.AreEqual(expectedHp, ((EnemyEntity)entity).MaxHp);
                Assert.AreEqual(wave, ((EnemyEntity)entity).Wave);
            }
        }

        [TestCase(2001, "Spinach", 55)]
        [TestCase(2002, "FrozenCan", 65)]
        [TestCase(2003, "GhostOnion", 72)]
        [TestCase(2004, "Apple", 65)]
        public void PrefabContainsAllOrderedFramesAndAnimationCompletionEvents(int configId, string folder, int deathFrameCount)
        {
            var config = _tables.TbEnemy.Get(configId);
            Assert.IsEmpty(config.BuffId);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/Prefabs/" + config.EnemyPrefab + ".prefab");
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
            for (var i = 0; i < names.Length; i++)
            {
                var state = controller.layers[0].stateMachine.states.Single(s => s.state.name == names[i]).state;
                var clip = state.motion as AnimationClip;
                Assert.IsNotNull(clip);
                Assert.AreEqual(i < 2, clip.isLooping);
                var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
                Assert.AreEqual("Visual", binding.path);
                Assert.AreEqual("m_Sprite", binding.propertyName);
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                Assert.GreaterOrEqual(keys.Length, 2);
                if (names[i] == "Move" || names[i] == "Death")
                    Assert.AreEqual(names[i] == "Death" ? deathFrameCount : 6,
                        keys.Select(k => k.value).Distinct().Count());
                for (var k = 0; k < keys.Length; k++)
                {
                    Assert.IsInstanceOf<Sprite>(keys[k].value);
                    var path = AssetDatabase.GetAssetPath(keys[k].value);
                    StringAssert.Contains("/" + folder + "/", path);
                    if (k > 0)
                    {
                        var previous = AssetDatabase.GetAssetPath(keys[k - 1].value);
                        Assert.LessOrEqual(FrameNumber(previous), FrameNumber(path));
                        Assert.Greater(keys[k].time, keys[k - 1].time);
                    }
                }
                Assert.AreSame(keys[keys.Length - 2].value, keys[keys.Length - 1].value);
                Assert.Greater(clip.length, keys[keys.Length - 1].time);
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
            buffer.WriteString("SkeletonEnemy"); buffer.WriteInt(100); buffer.WriteFloat(speed);
            buffer.WriteInt(1); buffer.WriteFloat(1f); buffer.WriteFloat(1f); buffer.WriteSize(0);
            buffer.WriteFloat(0.3f);
            return new Enemy(buffer);
        }

        private static MemoryPoolInfo PoolInfo(Type type)
        {
            return MemoryPool.GetAllMemoryPoolInfos().Single(info => info.Type == type);
        }
    }
}
