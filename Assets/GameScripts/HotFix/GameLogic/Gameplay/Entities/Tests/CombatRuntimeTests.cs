using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class CombatRuntimeTests
    {
        private BattleEntityRegistry _registry;
        private BattleRuntime _battle;
        private CombatRuntime Runtime => _battle.Combat;
        private RobotEntity _robot;
        private int _target;
        [SetUp]
        public void SetUp()
        {
            _registry = new BattleEntityRegistry();
            _battle = new BattleRuntime(41, _registry, new CombatRuntime(41));
            _robot = _registry.CreateRobot(1, BattleSide.Player, 1, 1);
            _target = _registry.CreateEnemy(1, BattleSide.Player, 1000, 1f);
        }
        [TearDown] public void TearDown() { _battle.Dispose(); _registry.Dispose(); }

        [Test]
        public void SameGroupAddsAndDifferentGroupsMultiplyThroughBuffs()
        {
            AddBuff(_battle.Player, 1, new Hook { Attack = draft => draft.AddPercent(CombatModifierGroup.GlobalDamage, 0.1f) });
            AddBuff(_battle.Player, 2, new Hook { Attack = draft => draft.AddPercent(CombatModifierGroup.GlobalDamage, 0.2f) });
            AddBuff(_robot, 3, new Hook { Attack = draft => draft.AddPercent(CombatModifierGroup.AreaDamage, 0.5f) });
            var result = Runtime.Resolve(Capture());
            Assert.AreEqual(195, result.Targets[0].AppliedDamage);
            Assert.AreEqual(805, Hp(_target));
        }

        [Test]
        public void RefreshAndRemovalOnlyChangeNewShots()
        {
            var owner = _battle.Player;
            AddBuff(owner, 1, new Hook { Attack = draft => draft.AddPercent(CombatModifierGroup.GlobalDamage, 0.1f) });
            var old = Capture();
            owner.Buffs.Clear();
            AddBuff(owner, 2, new Hook { Attack = draft => draft.AddPercent(CombatModifierGroup.GlobalDamage, 0.5f) });
            var upgraded = Capture();
            owner.Buffs.Clear();
            var plain = Capture();
            Assert.AreEqual(110, Runtime.Resolve(old).Targets[0].AppliedDamage);
            Assert.AreEqual(150, Runtime.Resolve(upgraded).Targets[0].AppliedDamage);
            Assert.AreEqual(100, Runtime.Resolve(plain).Targets[0].AppliedDamage);
        }

        [Test]
        public void SidesStayIndependentAndCrossFieldTargetsAreIgnored()
        {
            var opponent = _registry.CreateRobot(1, BattleSide.Opponent, 1, 1);
            var otherTarget = _registry.CreateEnemy(1, BattleSide.Opponent, 1000, 1f);
            AddBuff(_battle.Player, 1, new Hook { Attack = d => d.AddPercent(CombatModifierGroup.GlobalDamage, 0.5f) });
            var other = Runtime.CaptureAttack(Action(opponent.InstanceId, BattleSide.Opponent, otherTarget));
            Assert.AreEqual(100, Runtime.Resolve(other).Targets[0].AppliedDamage);
            Assert.AreEqual(150, Runtime.Resolve(Capture()).Targets[0].AppliedDamage);
            var wrong = Runtime.CaptureAttack(Action(_robot.InstanceId, BattleSide.Player, otherTarget));
            Assert.AreEqual(0, Runtime.Resolve(wrong).HitCount);
        }

        [Test]
        public void EachTargetGetsItsOwnDefenseDraftAtImpact()
        {
            var second = _registry.CreateEnemy(1, BattleSide.Player, 1000, 1f);
            _registry.TryGet(_target, out var enemy);
            AddBuff(enemy, 1, new Hook { Hurt = c => c.Outcome.AddPercent(CombatModifierGroup.TargetCondition, -0.25f) });
            var action = Runtime.CaptureAttack(new CombatActionSnapshot(41, _robot.InstanceId, BattleSide.Player, 1,
                new[] { _target, second }, CombatOutcomeTag.AreaDamage, 100, Vector3.zero, Vector3.one, 1));
            var result = Runtime.Resolve(action);
            Assert.AreEqual(75, result.Targets[0].AppliedDamage);
            Assert.AreEqual(100, result.Targets[1].AppliedDamage);
        }

        [Test]
        public void RemovedSourceDoesNotCancelFrozenDamageOrHitBuff()
        {
            _battle.Catalog.Add(new BuffModel(99, "noop"));
            AddBuff(_robot, 1, new Hook { Attack = d => d.AddPercent(CombatModifierGroup.GlobalDamage, 0.5f) });
            var parameters = new Dictionary<string, float> { { "slow", 0.3f } };
            var action = Runtime.CaptureAttack(new CombatActionSnapshot(41, _robot.InstanceId, BattleSide.Player, 1,
                new[] { _target }, CombatOutcomeTag.SingleDamage, 100, Vector3.zero, Vector3.one, 1,
                hitBuffs: new[] { new CombatBuffApplication(99, 3f, parameters) }));
            parameters["slow"] = 0.9f;
            var source = _robot.OwnerRef;
            _registry.Remove(_robot.InstanceId, EntityRemovalReason.Consumed, out _);
            Assert.AreEqual(150, Runtime.Resolve(action).Targets[0].AppliedDamage);
            _registry.TryGet(_target, out var enemy);
            Assert.AreEqual(0.3f, enemy.Buffs.Find(99, source).Get("slow"));
        }

        [Test]
        public void DuplicateForeignAndAlreadyCapturedSnapshotsAreRejected()
        {
            var action = Capture();
            using (var registry = new BattleEntityRegistry())
            using (var other = new BattleRuntime(41, registry, new CombatRuntime(41)))
                Assert.Throws<InvalidOperationException>(() => other.Combat.Resolve(action));
            Assert.Throws<InvalidOperationException>(() => Runtime.CaptureAttack(action));
            Assert.Throws<InvalidOperationException>(() => Runtime.CaptureAttack(new CombatActionSnapshot(42,
                _robot.InstanceId, BattleSide.Player, 1, new[] { _target }, CombatOutcomeTag.SingleDamage,
                100, Vector3.zero, Vector3.one, 1)));
            Runtime.Resolve(action);
            Assert.Throws<InvalidOperationException>(() => Runtime.Resolve(action));
            Assert.AreEqual(900, Hp(_target));
        }

        [Test]
        public void SnapshotCopiesAndDeduplicatesTargetIds()
        {
            var targets = new[] { _target, _target };
            var action = new CombatActionSnapshot(41, _robot.InstanceId, BattleSide.Player, 1, targets,
                CombatOutcomeTag.SingleDamage, 100, Vector3.zero, Vector3.one, 1);
            targets[0] = 999;
            Assert.AreEqual(1, action.TargetInstanceIds.Count);
            Assert.AreEqual(_target, action.TargetInstanceIds[0]);
            Assert.Throws<NotSupportedException>(() => ((IList<int>)action.TargetInstanceIds)[0] = 999);
        }

        [TestCase(-2f, 0)] [TestCase(-1f, 0)] [TestCase(0.5f, 150)]
        public void DamageCannotBecomeNegative(float percent, int expected)
        {
            AddBuff(_robot, 1, new Hook { Attack = d => d.AddPercent(CombatModifierGroup.GlobalDamage, percent) });
            var result = Runtime.Resolve(Capture());
            Assert.AreEqual(expected, result.Targets[0].AppliedDamage);
            Assert.AreEqual(1000 - expected, Hp(_target));
        }

        [Test]
        public void CancellationDoesNotDamageOrTriggerHitBuffs()
        {
            AddBuff(_robot, 1, new Hook { Hit = c => c.Outcome.Cancel() });
            var result = Runtime.Resolve(Capture());
            Assert.AreEqual(0, result.HitCount);
            Assert.AreEqual(CombatOutcomeTag.None, result.ActualTags);
            Assert.AreEqual(1000, Hp(_target));
        }

        [Test]
        public void ReentryAgainstSameTargetCannotApplyDamageTwice()
        {
            AddBuff(_robot, 1, new Hook { Hit = c => Assert.AreEqual(0, Runtime.Resolve(Capture()).HitCount) });
            Assert.AreEqual(100, Runtime.Resolve(Capture()).Targets[0].AppliedDamage);
            Assert.AreEqual(900, Hp(_target));
        }

        [Test]
        public void BuffRemovalDuringHitUsesStablePriorityOrder()
        {
            var seen = new List<int>();
            AddBuff(_robot, 1, new Hook { Hit = c => { seen.Add(1); _robot.Buffs.Clear(); } });
            AddBuff(_robot, 2, new Hook { Hit = c => seen.Add(2) });
            Runtime.Resolve(Capture());
            CollectionAssert.AreEqual(new[] { 1, 2 }, seen);
            Assert.AreEqual(0, _robot.Buffs.Count);
        }

        [Test]
        public void EndingBattleInsideHookStopsDamageAndFollowingHooks()
        {
            var action = Capture();
            AddBuff(_robot, 1, new Hook { Hit = c => _battle.Dispose() });
            AddBuff(_robot, 2, new Hook { Hit = c => Assert.Fail("终局后不得继续触发") });
            Assert.AreEqual(0, Runtime.Resolve(action).HitCount);
            Assert.AreEqual(1000, Hp(_target));
            Assert.Throws<ObjectDisposedException>(() => Runtime.Resolve(action));
        }

        [Test]
        public void ThrowingDeathHookStillRemovesEnemyAndAttackCannotRetry()
        {
            _registry.TryGet(_target, out var enemy);
            AddBuff(enemy, 1, new Hook { Killed = c => throw new InvalidOperationException("expected death hook failure") });
            var action = Capture(2000);
            Assert.Throws<InvalidOperationException>(() => Runtime.Resolve(action));
            Assert.IsFalse(_registry.TryGet(_target, out _));
            Assert.Throws<InvalidOperationException>(() => Runtime.Resolve(action));
        }

        [Test]
        public void DisposalInvalidatesPendingAttacksAndIsIdempotent()
        {
            var action = Capture();
            Runtime.Dispose(); Runtime.Dispose();
            Assert.Throws<ObjectDisposedException>(() => Runtime.Resolve(action));
            Assert.AreEqual(1000, Hp(_target));
        }

        private CombatActionSnapshot Capture(int damage = 100) => Runtime.CaptureAttack(Action(_robot.InstanceId, BattleSide.Player, _target, damage));
        private static CombatActionSnapshot Action(int source, BattleSide side, int target, int damage = 100)
            => new CombatActionSnapshot(41, source, side, 1, new[] { target }, CombatOutcomeTag.SingleDamage,
                damage, Vector3.zero, Vector3.one, 1);
        private int Hp(int id) { _registry.TryGet(id, out var entity); return ((EnemyEntity)entity).CurrentHp; }
        private static void AddBuff(ISkillOwner owner, int id, Hook hook)
            => owner.Buffs.Add(new BuffModel(id, "custom"), owner.OwnerRef, 10f, behavior: hook);
        private sealed class Hook : BuffBehavior
        {
            internal Action<CombatOutcomeDraft> Attack;
            internal Action<CombatDamageContext> Hit, Hurt, Killed;
            public override void OnAttack(BuffContainer c, BuffObj b, CombatOutcomeDraft d) => Attack?.Invoke(d);
            public override void OnHit(BuffContainer c, BuffObj b, CombatDamageContext d) => Hit?.Invoke(d);
            public override void OnBeHurt(BuffContainer c, BuffObj b, CombatDamageContext d) => Hurt?.Invoke(d);
            public override void OnBeKilled(BuffContainer c, BuffObj b, CombatDamageContext d) => Killed?.Invoke(d);
        }
    }
}
