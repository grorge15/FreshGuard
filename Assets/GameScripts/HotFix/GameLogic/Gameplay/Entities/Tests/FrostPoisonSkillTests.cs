using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class FrostPoisonSkillTests
    {
        private EnemyBattleControllerTests.Fixture _f;
        private RobotCombatController _combat;
        private RobotEntity _robot;
        [TearDown] public void Clear() { _combat?.Dispose(); _combat = null; _f?.Dispose(); _f = null; }
        private static Tables Tables() => new Tables(name => new ByteBuf(File.ReadAllBytes(
            Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));

        [Test]
        public void PoisonLevelsBindNamedFrozenHitEffectsAndFrostKeepsItsConfiguration()
        {
            var tables = Tables(); var catalog = SkillConfigAdapter.FromTables(tables);
            Assert.IsTrue(catalog.TryGet(1507, out var skill)); Assert.AreEqual(SkillType.Robot, skill.Type);
            for (var i = 1; i <= 5; i++)
            {
                catalog.TryGetLevel(1507, i, out var level);
                Assert.AreEqual(new[] { 20, 30, 40, 50, 60 }[i - 1], level.Parameters["damage"]);
                Assert.AreEqual(new[] { 5, 7, 10, 14, 20 }[i - 1], level.Parameters["poisonDamage"]);
                var effect = level.CaptureHitBuffs(level.Parameters).Single();
                Assert.AreEqual(2507, effect.BuffId); Assert.AreEqual(5, effect.Duration);
                Assert.AreEqual(1, effect.Parameters["poisonInterval"]); Assert.AreEqual(3, effect.Parameters["poisonMaxLayers"]);
                catalog.TryGetLevel(1505, i, out var frost);
                Assert.AreEqual(new[] { 20, 28, 40, 55, 77 }[i - 1], frost.Parameters["damage"]);
                Assert.AreEqual(i <= 2 ? 0.3f : 0.4f, frost.Parameters["slow"]); Assert.AreEqual(3, frost.Parameters["duration"]);
            }
        }

        [TestCase("PoisonInterval", 0f)] [TestCase("PoisonDuration", -1f)]
        [TestCase("PoisonDamage", -1)] [TestCase("PoisonMaxLayers", 0)]
        public void InvalidPoisonConfigurationFailsBeforeBattle(string field, object value)
        {
            var tables = Tables();
            typeof(GameConfig.skill.SkillLevel).GetField(field).SetValue(tables.TbSkillLevel.Get(150701), value);
            Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
        }

        [Test]
        public void FrostWithPositiveSlowAndZeroDurationFailsBeforeBattle()
        {
            var tables = Tables();
            typeof(GameConfig.skill.SkillLevel).GetField("Duration").SetValue(tables.TbSkillLevel.Get(150501), 0f);
            Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
        }

        [Test]
        public void FrostRefreshesSameSourceAndStrongerSourceExpiresIntoWeakerSlow()
        {
            _f = new EnemyBattleControllerTests.Fixture();
            var config = _f.Tables.TbEnemy.Get(2001);
            typeof(GameConfig.enemy.Enemy).GetField("ControlResist").SetValue(config, 0.5f);
            var id = _f.Context.Registry.CreateEnemyFromConfig(config, BattleSide.Player);
            _f.Context.Registry.TryGet(id, out var entity); var enemy = (EnemyEntity)entity;
            var slow = new BuffModel(2505, "slow"); var source = _f.Context.Battle.Player.OwnerRef;
            _f.Context.Battle.AddBuff(enemy, slow, source, 3, new Dictionary<string, float> { { "slow", 0.8f } });
            Assert.That(enemy.EffectiveSpeed / enemy.BaseSpeed, Is.EqualTo(0.6f).Within(0.00001));
            enemy.Buffs.Advance(1);
            _f.Context.Battle.AddBuff(enemy, slow, source, 4, new Dictionary<string, float> { { "slow", 0.2f } });
            Assert.AreEqual(1, enemy.Buffs.Count); Assert.AreEqual(4, enemy.Buffs.Items[0].Remaining);
            _f.Context.Battle.AddBuff(enemy, slow, _f.Context.Battle.Opponent.OwnerRef, 2,
                new Dictionary<string, float> { { "slow", 1f } });
            Assert.That(enemy.EffectiveSpeed / enemy.BaseSpeed, Is.EqualTo(0.5f).Within(0.00001));
            enemy.Buffs.Advance(2);
            Assert.That(enemy.EffectiveSpeed / enemy.BaseSpeed, Is.EqualTo(0.9f).Within(0.00001));
            Assert.AreEqual(2, enemy.Buffs.Items[0].Remaining);
            enemy.Buffs.Advance(2); Assert.AreEqual(enemy.BaseSpeed, enemy.EffectiveSpeed);
        }

        [Test]
        public void BattlePauseStopsPoisonAndLargeFrameStopsAtLethalTick()
        {
            Prepare(603, 1);
            var view = _f.PlayerView; var id = view.InstanceId;
            Assert.IsTrue(Contact()); _combat.AdvanceExistingBullets(0.6f);
            _f.Context.Registry.TryGet(id, out var entity);
            _f.Context.Registry.ApplyDamage(id, 70, out _); // 两跳击败。
            var start = view.transform.position;
            _f.Host.SetFrozen(true); _f.Advance(10);
            Assert.AreEqual(10, ((EnemyEntity)entity).CurrentHp);
            Assert.AreEqual(5, entity.Buffs.Items[0].Remaining); Assert.AreEqual(start, view.transform.position);
            _f.Host.SetFrozen(false); _f.Advance(5);
            Assert.IsFalse(_f.Context.Registry.TryGet(id, out _));
            Assert.IsTrue(((EnemyBattleController)_f.Controller).TryGetDeathPosition(BattleSide.Player, id, out var death));
            Assert.That(Vector3.Distance(start, death), Is.EqualTo(0.6f).Within(0.00001));
            Assert.AreEqual(0, entity.Buffs.Count);
        }

        [Test]
        public void PoisonProjectileUsesCastSnapshotAndDoesNotTickAtImpact()
        {
            Prepare(603, 3);
            var id = _f.PlayerView.InstanceId;
            _robot.Buffs.Add(new BuffModel(100, "test"), _robot.OwnerRef, 10,
                behavior: new CastHook(c => { c.Parameters["poisonDamage"] = 12; c.Parameters["poisonDuration"] = 6; }));
            Assert.IsTrue(Contact()); _robot.Buffs.Clear(); _robot.TryUpgrade();
            _combat.AdvanceExistingBullets(0.6f);
            _f.Context.Registry.TryGet(id, out var entity); var enemy = (EnemyEntity)entity;
            Assert.AreEqual(60, enemy.CurrentHp); Assert.AreEqual(1, enemy.Buffs.Count);
            Assert.AreEqual(12, enemy.Buffs.Items[0].Get("poisonDamage")); Assert.AreEqual(6, enemy.Buffs.Items[0].Remaining);
            enemy.Buffs.Advance(0.99f); Assert.AreEqual(60, enemy.CurrentHp);
            enemy.Buffs.Advance(0.01f); Assert.AreEqual(48, enemy.CurrentHp);
        }

        [Test]
        public void PoisonKillGrantsExactlyOneNormalRewardAndKeepsDeathPresentation()
        {
            Prepare(603, 1);
            var id = _f.PlayerView.InstanceId;
            var before = _f.Context.GetState(BattleSide.Player).EnergyCoins;
            using (var rewards = new BattleRewardAdapter(_f.Context))
            {
                Assert.IsTrue(Contact()); _combat.AdvanceExistingBullets(0.6f);
                _f.Context.Registry.TryGet(id, out var entity);
                _f.Context.Registry.ApplyDamage(id, 75, out _); // 余5血，下一跳毒伤击杀。
                entity.Buffs.Advance(5);
                Assert.IsFalse(_f.Context.Registry.TryGet(id, out _));
                Assert.AreEqual(before + _f.Tables.TbEnemy.Get(2001).EnemyKillSP, _f.Context.GetState(BattleSide.Player).EnergyCoins);
                entity.Buffs.Advance(5);
                Assert.AreEqual(before + _f.Tables.TbEnemy.Get(2001).EnemyKillSP, _f.Context.GetState(BattleSide.Player).EnergyCoins);
                Assert.IsTrue(((EnemyBattleController)_f.Controller).TryGetDeathPosition(BattleSide.Player, id, out _));
            }
        }

        private void Prepare(int configId, int level)
        {
            _f = new EnemyBattleControllerTests.Fixture(); _f.Prepare(); _f.Start();
            var views = new Dictionary<int, GameObject>();
            _combat = new RobotCombatController(_f.Host, _f.Context, (EnemyBattleController)_f.Controller, views,
                (name, parent, token) => UniTask.FromResult(UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/AssetRaw/Actor/Bullets/Prefabs/" + name + ".prefab"), parent)));
            _combat.PrepareAsync(_f.Tables, CancellationToken.None).GetAwaiter().GetResult();
            _robot = _f.Context.Registry.CreateRobotFromConfig(_f.Tables.TbRobot.Get(configId), BattleSide.Player, level);
            _robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Robots/Prefabs/" + _robot.Configuration.RobotPrefab + ".prefab"), _f.Root.transform);
            instance.GetComponent<RobotView>().Bind(_f.Context.Registry, _robot.InstanceId); views.Add(_robot.InstanceId, instance);
            _combat.SyncRobots();
        }
        private bool Contact() => _combat.TryAttackFromContact(_f.Context.BattleId, _robot.Side, _robot.InstanceId);
        private sealed class CastHook : BuffBehavior
        {
            private readonly Action<SkillCastContext> _apply;
            internal CastHook(Action<SkillCastContext> apply) { _apply = apply; }
            public override void OnCast(BuffContainer c, BuffObj b, SkillCastContext cast) => _apply(cast);
        }
    }
}
