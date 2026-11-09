using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class ChainCombatTests
    {
        private BattleEntityRegistry _registry;
        private BattleRuntime _battle;
        private RobotEntity _source;
        private int[] _targets;

        [SetUp]
        public void SetUp()
        {
            _registry = new BattleEntityRegistry();
            _battle = new BattleRuntime(705, _registry, new CombatRuntime(705));
            _source = _registry.CreateRobot(701, BattleSide.Player, 1, 1605);
            _targets = new int[5];
            for (var i = 0; i < _targets.Length; i++)
                _targets[i] = _registry.CreateEnemy(1, BattleSide.Player, 1000, 1);
        }

        [TearDown] public void TearDown() { _battle.Dispose(); _registry.Dispose(); }

        [Test]
        public void ChainRoundsEachOriginalDamageOnceAndNeverRepeatsAttackHook()
        {
            var attacks = 0;
            _source.Buffs.Add(new BuffModel(1, "test"), _source.OwnerRef, 10,
                behavior: new Hook { Attack = d => attacks++ });
            var shot = Shot();
            var result = _battle.Combat.ResolveChainHit(shot, _targets, 0.7f);
            CollectionAssert.AreEqual(new[] { 20, 14, 10, 7, 5 }, Damages(result));
            Assert.AreEqual(1, attacks);
            for (var i = 0; i < _targets.Length; i++)
                Assert.AreEqual(DamageResult.Ignored, _battle.Combat.ResolveProjectileHit(shot, _targets[i]).DamageResult);
        }

        [Test]
        public void TargetPercentCombinesWithFrozenAttackBeforeOnlyRounding()
        {
            _source.Buffs.Add(new BuffModel(1, "test"), _source.OwnerRef, 10,
                behavior: new Hook { Attack = d => d.AddPercent(CombatModifierGroup.GlobalDamage, 0.25f) });
            var shot = Shot(3);
            _source.Buffs.Clear();
            _registry.TryGet(_targets[1], out var target);
            target.Buffs.Add(new BuffModel(2, "test"), target.OwnerRef, 10,
                behavior: new Hook { Hurt = c => c.Outcome.AddPercent(CombatModifierGroup.TargetCondition, 1f) });
            var result = _battle.Combat.ResolveChainHit(shot, new[] { _targets[0], _targets[1], _targets[2] }, 0.7f);
            CollectionAssert.AreEqual(new[] { 4, 5, 2 }, Damages(result));
        }

        [Test]
        public void RemovedTargetKeepsItsHopIndexAndDuplicateIdsCannotTakeDamageTwice()
        {
            var shot = Shot();
            _battle.Combat.TargetResolved += c =>
            {
                if (c.Result.TargetInstanceId == _targets[0])
                    _registry.Remove(_targets[1], EntityRemovalReason.Manual, out _);
            };
            var result = _battle.Combat.ResolveChainHit(shot,
                new[] { _targets[0], _targets[1], _targets[2], _targets[0] }, 0.7f);
            CollectionAssert.AreEqual(new[] { 20, 0, 10, 0 }, Damages(result));
            _registry.TryGet(_targets[0], out var first);
            Assert.AreEqual(980, ((EnemyEntity)first).CurrentHp);
        }

        [Test]
        public void ChainRejectsForeignSnapshotAndWrongSideTargets()
        {
            var other = _registry.CreateEnemy(1, BattleSide.Opponent, 1000, 1);
            var result = _battle.Combat.ResolveChainHit(Shot(), new[] { other }, 0.7f);
            Assert.AreEqual(DamageResult.Ignored, result[0].DamageResult);
            using (var registry = new BattleEntityRegistry())
            using (var battle = new BattleRuntime(706, registry, new CombatRuntime(706)))
                Assert.Throws<InvalidOperationException>(() => battle.Combat.ResolveChainHit(Shot(), _targets, 0.7f));
        }

        private ProjectileAttackSnapshot Shot(int damage = 20) => _battle.Combat.CaptureProjectileAttack(
            new CombatActionSnapshot(705, _source.InstanceId, BattleSide.Player, 1605, new[] { _targets[0] },
                CombatOutcomeTag.SingleDamage, damage, Vector3.zero, Vector3.right, 1));
        private static int[] Damages(IReadOnlyList<CombatTargetResolution> results)
        {
            var damage = new int[results.Count];
            for (var i = 0; i < damage.Length; i++) damage[i] = results[i].RequestedDamage;
            return damage;
        }
        private sealed class Hook : BuffBehavior
        {
            internal Action<CombatOutcomeDraft> Attack;
            internal Action<CombatDamageContext> Hurt;
            public override void OnAttack(BuffContainer c, BuffObj b, CombatOutcomeDraft d) => Attack?.Invoke(d);
            public override void OnBeHurt(BuffContainer c, BuffObj b, CombatDamageContext d) => Hurt?.Invoke(d);
        }
    }
}
