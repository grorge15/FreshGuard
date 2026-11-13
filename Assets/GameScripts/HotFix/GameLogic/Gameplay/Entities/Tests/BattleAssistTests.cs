using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Luban;
using GameConfig.glass;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class BattleAssistTests
    {
        private BattleEntityRegistry _registry;
        private BattleRuntime _battle;
        private GameConfig.Tables _tables;

        [SetUp]
        public void SetUp()
        {
            _registry = new BattleEntityRegistry();
            _battle = new BattleRuntime(18015, _registry, new CombatRuntime(18015));
            _tables = new GameConfig.Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
        }

        [TearDown]
        public void TearDown() { _battle.Dispose(); _registry.Dispose(); }

        private void Configure(params MatchBuff[] buffs) =>
            _battle.ConfigureAssists(_tables, buffs, Array.Empty<MatchBuff>());
        private static MatchBuff Carry(int id, int level = 1) => new MatchBuff(id, "test", "test", level);

        [Test]
        public void OpeningSpeedExpiresWithoutClearingComboSpeedOrOpponentState()
        {
            Configure(Carry(1801), Carry(1802, 2));
            _battle.Player.Assists.SetCombo(20);
            Assert.AreEqual(0.55f, _battle.Player.Assists.NormalBallSpeedRate, 0.0001f);
            Assert.AreEqual(0f, _battle.Opponent.Assists.NormalBallSpeedRate);
            _battle.Advance(29.99f);
            Assert.AreEqual(0.15f, _battle.Player.Assists.OpeningBallSpeedRate, 0.0001f);
            _battle.Advance(0.02f);
            Assert.AreEqual(0.4f, _battle.Player.Assists.NormalBallSpeedRate, 0.0001f);
        }

        [TestCase(1, 9, 0f)] [TestCase(1, 10, 0.1f)] [TestCase(2, 20, 0.4f)]
        [TestCase(3, 30, 0.9f)] [TestCase(3, 90, 0.9f)]
        public void ComboBonusesUseLevelStepAndCapThenReset(int level, int combo, float expected)
        {
            Configure(Carry(1802, level), Carry(1803, level));
            _battle.Player.Assists.SetCombo(combo);
            Assert.AreEqual(expected, _battle.Player.Assists.ComboBallSpeedRate, 0.0001f);
            Assert.AreEqual(expected, _battle.Player.Assists.RobotDamageRate, 0.0001f);
            _battle.Player.Assists.SetCombo(0);
            Assert.AreEqual(0f, _battle.Player.Assists.NormalBallSpeedRate);
            Assert.AreEqual(0f, _battle.Player.Assists.RobotDamageRate);
            Assert.AreEqual(2, _battle.Player.Buffs.Items.Count);
        }

        [Test]
        public void DirectRobotDamageIsFrozenAndOtherSourcesDoNotReceiveBonus()
        {
            Configure(Carry(1803, 2));
            var robot = _registry.CreateRobot(1, BattleSide.Player, 1, 1);
            var enemy = _registry.CreateEnemy(1, BattleSide.Player, 1000, 1f);
            _battle.Player.Assists.SetCombo(20);
            var shot = _battle.Combat.CaptureAttack(Action(robot.InstanceId, enemy));
            _battle.Player.Assists.SetCombo(0);
            Assert.AreEqual(140, _battle.Combat.Resolve(shot).Targets[0].RequestedDamage);
            _battle.Player.Assists.SetCombo(30);
            var nonRobot = _battle.Combat.CaptureAttack(Action(robot.InstanceId, enemy, CombatSourceKind.Other));
            Assert.AreEqual(100, _battle.Combat.Resolve(nonRobot).Targets[0].RequestedDamage);
        }

        [Test]
        public void SideSpecificLevelsAndComboDoNotLeakToOtherBattlefield()
        {
            _battle.ConfigureAssists(_tables, new[] { Carry(1802), Carry(1803) },
                new[] { Carry(1802, 3), Carry(1803, 3) });
            _battle.Player.Assists.SetCombo(10);
            _battle.Opponent.Assists.SetCombo(20);
            Assert.AreEqual(0.1f, _battle.Player.Assists.RobotDamageRate, 0.0001f);
            Assert.AreEqual(0.6f, _battle.Opponent.Assists.RobotDamageRate, 0.0001f);
            _battle.Player.Assists.SetCombo(0);
            Assert.AreEqual(0.6f, _battle.Opponent.Assists.ComboBallSpeedRate, 0.0001f);
        }

        [Test]
        public void InvalidCarryDoesNotPartiallyApplyEarlierValidEffects()
        {
            Assert.Throws<InvalidOperationException>(() => Configure(Carry(1801), Carry(1802, 4)));
            Assert.AreEqual(0, _battle.Player.Buffs.Count);
            Assert.AreEqual(0f, _battle.Player.Assists.OpeningBallSpeedRate);
            Configure(Carry(1801));
            Assert.AreEqual(1, _battle.Player.Buffs.Count);
        }

        [Test]
        public void PeriodicDamageReadsCurrentBonusOnceWithoutAttackHooksEvenAfterSourceRemoval()
        {
            Configure(Carry(1803, 2));
            var robot = _registry.CreateRobot(1, BattleSide.Player, 1, 1);
            var source = robot.OwnerRef;
            var enemyId = _registry.CreateEnemy(1, BattleSide.Player, 1000, 1f);
            _registry.TryGet(enemyId, out var enemy);
            var attacks = 0;
            _battle.Player.Buffs.Add(new BuffModel(9999, "test"), _battle.Player.OwnerRef, 60,
                behavior: new AttackCounter(() => attacks++));
            _battle.Player.Assists.SetCombo(20);
            Assert.AreEqual(14, _battle.Combat.ResolvePeriodicDamage(source, enemy.OwnerRef, 1507, 10).RequestedDamage);
            _registry.Remove(robot.InstanceId, EntityRemovalReason.Consumed, out _);
            _battle.Player.Assists.SetCombo(0);
            Assert.AreEqual(10, _battle.Combat.ResolvePeriodicDamage(source, enemy.OwnerRef, 1507, 10).RequestedDamage);
            Assert.AreEqual(0, attacks);
        }

        [Test]
        public void NonRobotPeriodicDamageBypassesRobotAssist()
        {
            Configure(Carry(1803, 3));
            var source = _registry.CreateRobot(1, BattleSide.Player, 1, 1);
            var enemyId = _registry.CreateEnemy(1, BattleSide.Player, 1000, 1f);
            _registry.TryGet(enemyId, out var enemy);
            _battle.Player.Assists.SetCombo(30);
            Assert.AreEqual(10, _battle.Combat.ResolvePeriodicDamage(source.OwnerRef, enemy.OwnerRef, 1507, 10,
                CombatSourceKind.Other).RequestedDamage);
        }

        [Test]
        public void GlassOverrideOnlyChangesNormalHitsAndSharesOriginalCooldown()
        {
            Configure(Carry(1804));
            var glass = _registry.CreateGlass(1, BattleSide.Player, new BoardCoordinate(0, 0), GlassKind.Normal, 8, 1f);
            var loss = _battle.Player.Assists.NormalBallGlassLossOverride;
            Assert.AreEqual(GlassCollisionResult.Damaged,
                _registry.ApplyGlassCollision(glass.InstanceId, false, out _, loss));
            Assert.AreEqual(6, glass.CurrentDurability);
            Assert.AreEqual(1, glass.HitDurabilityLoss);
            Assert.AreEqual(GlassCollisionResult.Ignored,
                _registry.ApplyGlassCollision(glass.InstanceId, true, out _, loss));
            _registry.AdvanceTime(1f);
            _registry.ApplyGlassCollision(glass.InstanceId, true, out _, loss);
            Assert.AreEqual(5, glass.CurrentDurability);
        }

        [Test]
        public void ClearingBattlefieldRemovesRulesAndReconfigurationIsRejected()
        {
            Configure(Carry(1801), Carry(1802, 3), Carry(1803), Carry(1804), Carry(1805));
            _battle.Player.Assists.SetCombo(30);
            Assert.AreEqual(1, _battle.Player.Assists.ExtraBerserkBallCount);
            Assert.Throws<InvalidOperationException>(() => Configure(Carry(1801)));
            _battle.Player.Buffs.Clear();
            Assert.AreEqual(0f, _battle.Player.Assists.NormalBallSpeedRate);
            Assert.AreEqual(0f, _battle.Player.Assists.RobotDamageRate);
            Assert.IsNull(_battle.Player.Assists.NormalBallGlassLossOverride);
            Assert.AreEqual(0, _battle.Player.Assists.ExtraBerserkBallCount);
        }

        [Test]
        public void BallSpeedCombinesAssistRatesAndBerserkIgnoresThem()
        {
            Configure(Carry(1801), Carry(1802, 2));
            _battle.Player.Assists.SetCombo(20);
            var root = new GameObject("AssistSpeedBall");
            try
            {
                root.SetActive(false);
                var ball = root.AddComponent<PhysicalCollisionBall>();
                root.SetActive(true);
                if (Get(ball, "_body") == null) Invoke(ball, "Awake");
                ball.GlobalValueProvider = _ => 1f;
                ball.BindBattle(_battle.BattleId, BattleSide.Player, () => true);
                ball.BindAssistState(_battle.Player.Assists);
                _battle.Player.Assists.SetCombo(20);
                Set(ball, "speed", 10f);
                Set(ball, "_bounceSpeedIncrease", 2f); Set(ball, "_bounceSpeedDuration", 1f);
                Set(ball, "_bounceSpeedRemaining", 1f);
                Assert.AreEqual(17.5f, ball.EffectiveSpeed, 0.0001f);
                ball.SetBerserk(true);
                Assert.AreEqual(12f, ball.EffectiveSpeed, 0.0001f);
                Assert.AreEqual(0f, _battle.Player.Assists.ComboBallSpeedRate);
                Assert.AreEqual(0.15f, _battle.Player.Assists.OpeningBallSpeedRate, 0.0001f);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private CombatActionSnapshot Action(int source, int target, CombatSourceKind kind = CombatSourceKind.Robot) =>
            new CombatActionSnapshot(_battle.BattleId, source, BattleSide.Player, 1, new[] { target },
                CombatOutcomeTag.SingleDamage, 100, Vector3.zero, Vector3.right, 1, kind);
        private static object Get(object target, string field) => target.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string method) => target.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
        private sealed class AttackCounter : BuffBehavior
        {
            private readonly System.Action _attack;
            public AttackCounter(System.Action attack) { _attack = attack; }
            public override void OnAttack(BuffContainer container, BuffObj buff, CombatOutcomeDraft attack) => _attack();
        }
    }
}
