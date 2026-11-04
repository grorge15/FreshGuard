using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class RobotSkillEffectsTests
    {
        [Test]
        public void PiercingFreezesAttackOnceAndConsumesEachVictimOnce()
        {
            using (var registry = new BattleEntityRegistry())
            using (var battle = new BattleRuntime(731, registry, new CombatRuntime(731)))
            {
                var robot = registry.CreateRobot(1, BattleSide.Player, 1, 1);
                var first = registry.CreateEnemy(1, BattleSide.Player, 100, 1);
                var second = registry.CreateEnemy(1, BattleSide.Player, 100, 1);
                var attackCalls = 0;
                robot.Buffs.Add(new BuffModel(1, "test"), robot.OwnerRef, 10,
                    behavior: new AttackHook(() => attackCalls++));
                var shot = battle.Combat.CaptureProjectileAttack(new CombatActionSnapshot(731, robot.InstanceId,
                    BattleSide.Player, 1502, new[] { first }, CombatOutcomeTag.SingleDamage, 20,
                    Vector3.zero, Vector3.right, 1));
                robot.Buffs.Clear();
                Assert.AreEqual(30, battle.Combat.ResolveProjectileHit(shot, first).AppliedDamage);
                Assert.AreEqual(DamageResult.Ignored, battle.Combat.ResolveProjectileHit(shot, first).DamageResult);
                Assert.AreEqual(30, battle.Combat.ResolveProjectileHit(shot, second).AppliedDamage);
                Assert.AreEqual(1, attackCalls);
                Assert.Throws<InvalidOperationException>(() => battle.Combat.Resolve(shot.Action));
            }
        }

        [TestCase(20, 100, 1)]
        [TestCase(100, 100, 0)]
        [TestCase(0, 100, 1)]
        public void KnockbackIsRequestedAfterDamageOnlyForLivingVictim(int damage, int hp, int calls)
        {
            using (var registry = new BattleEntityRegistry())
            using (var battle = new BattleRuntime(732, registry, new CombatRuntime(732)))
            {
                var source = registry.CreateRobot(1, BattleSide.Player, 1, 1);
                var target = registry.CreateEnemy(1, BattleSide.Player, hp, 1);
                var actualCalls = 0;
                battle.Combat.KnockbackRequested += (side, id, distance) =>
                {
                    actualCalls++;
                    Assert.AreEqual(target, id);
                    Assert.AreEqual(1, distance);
                    Assert.IsTrue(registry.TryGet(id, out var entity));
                    Assert.AreEqual(hp - damage, ((EnemyEntity)entity).CurrentHp);
                };
                var shot = battle.Combat.CaptureProjectileAttack(new CombatActionSnapshot(732, source.InstanceId,
                    BattleSide.Player, 1504, new[] { target }, CombatOutcomeTag.SingleDamage | CombatOutcomeTag.Knockback,
                    damage, Vector3.zero, Vector3.right, 1, knockbackDistance: 1));
                battle.Combat.ResolveProjectileHit(shot, target);
                Assert.AreEqual(calls, actualCalls);
            }
        }

        [TestCase(1f)]
        [TestCase(0.5f)]
        public void RouteKnockbackUpdatesPositionProgressAndNextSegment(float factor)
        {
            using (var f = new EnemyBattleControllerTests.Fixture())
            {
                foreach (var config in f.Tables.TbEnemy.DataList)
                    typeof(GameConfig.enemy.Enemy).GetField("KnockbackResist").SetValue(config, factor);
                f.Prepare(); f.Start(); f.Advance(4);
                var view = f.PlayerView;
                f.Context.Registry.TryGet(view.InstanceId, out var entity);
                var enemy = (EnemyEntity)entity;
                var before = enemy.RouteProgress;
                Assert.IsTrue(((EnemyBattleController)f.Controller).TryKnockback(BattleSide.Player, view.InstanceId, 1));
                Assert.That(enemy.RouteProgress, Is.EqualTo(Math.Max(0, before - factor)).Within(0.00001));
                var movedBack = enemy.RouteProgress;
                f.Advance(0.2f);
                Assert.That(enemy.RouteProgress, Is.EqualTo(movedBack + enemy.EffectiveSpeed * 0.2f).Within(0.00001));
                Assert.IsTrue(((EnemyBattleController)f.Controller).TryKnockback(BattleSide.Player, view.InstanceId, 100));
                Assert.AreEqual(0, enemy.RouteProgress);
                Assert.That(Vector3.Distance(view.transform.position, new Vector3(-3, 1)), Is.LessThan(0.00001));
            }
        }

        private sealed class AttackHook : BuffBehavior
        {
            private readonly Action _called;
            internal AttackHook(Action called) { _called = called; }
            public override void OnAttack(BuffContainer container, BuffObj buff, CombatOutcomeDraft attack)
            { _called(); attack.AddPercent(CombatModifierGroup.GlobalDamage, 0.5f); }
        }
    }
}
