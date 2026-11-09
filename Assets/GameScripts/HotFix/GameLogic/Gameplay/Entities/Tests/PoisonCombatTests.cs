using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class PoisonCombatTests
    {
        private BattleEntityRegistry _registry;
        private BattleRuntime _battle;
        private RobotEntity _source;
        private EnemyEntity _target;
        private readonly BuffModel _poison = new BuffModel(2507, "poison");
        private readonly BuffModel _slow = new BuffModel(2505, "slow");

        [SetUp]
        public void SetUp()
        {
            _registry = new BattleEntityRegistry(); _battle = new BattleRuntime(502, _registry, new CombatRuntime(502));
            var catalog = new SkillCatalog(); catalog.Add(_poison); catalog.Add(_slow); _battle.Configure(catalog);
            _source = _registry.CreateRobot(603, BattleSide.Player, 20, 1507);
            var id = _registry.CreateEnemy(2001, BattleSide.Player, 1000, 1);
            _registry.TryGet(id, out var target); _target = (EnemyEntity)target;
        }
        [TearDown] public void TearDown() { _battle.Dispose(); _registry.Dispose(); }

        private void Poison(int damage = 10, float duration = 5, float interval = 1) =>
            _battle.AddBuff(_target, _poison, _source.OwnerRef, duration, Parameters(damage, interval));
        internal static Dictionary<string, float> Parameters(int damage, float interval = 1) =>
            new Dictionary<string, float> { { "poisonDamage", damage }, { "poisonInterval", interval }, { "poisonMaxLayers", 3 } };
        private ProjectileAttackSnapshot Shot(int damage, CombatBuffApplication effect) =>
            _battle.Combat.CaptureProjectileAttack(new CombatActionSnapshot(502, _source.InstanceId, BattleSide.Player,
                1507, new[] { _target.InstanceId }, CombatOutcomeTag.SingleDamage, damage, Vector3.zero, Vector3.one, 1,
                hitBuffs: new[] { effect }));

        [Test]
        public void SameRobotThreeLayersTickThirtyAndRemovingSourceDoesNotCancelThem()
        {
            Poison(); Poison(); Poison();
            var id = _target.InstanceId;
            _registry.Remove(_source.InstanceId, EntityRemovalReason.Consumed, out _);
            _target.Buffs.Advance(0.99f); Assert.AreEqual(1000, _target.CurrentHp);
            _target.Buffs.Advance(0.01f); Assert.AreEqual(970, _target.CurrentHp);
            _target.Buffs.Advance(4); Assert.AreEqual(850, _target.CurrentHp);
            Assert.IsTrue(_registry.TryGet(id, out _)); Assert.AreEqual(0, _target.Buffs.Count);
        }

        [Test]
        public void EveryLayerKeepsItsFrozenDamageAndOwnTickTime()
        {
            var values = Parameters(5);
            _battle.AddBuff(_target, _poison, _source.OwnerRef, 2, values);
            values["poisonDamage"] = 999;
            _target.Buffs.Advance(0.5f); Poison(7, 2);
            _target.Buffs.Advance(0.5f); Assert.AreEqual(995, _target.CurrentHp);
            _target.Buffs.Advance(0.5f); Assert.AreEqual(988, _target.CurrentHp);
            _target.Buffs.Advance(0.5f); Assert.AreEqual(983, _target.CurrentHp);
            _target.Buffs.Advance(0.5f); Assert.AreEqual(976, _target.CurrentHp);
            Assert.AreEqual(0, _target.Buffs.Count);
        }

        [Test]
        public void MixedSourcesDropFromNineteenDamageToSevenWhenOlderLayersExpire()
        {
            var other = _registry.CreateRobot(603, BattleSide.Player, 20, 1507);
            Poison(5, 1);
            _battle.AddBuff(_target, _poison, other.OwnerRef, 1, Parameters(7));
            _battle.AddBuff(_target, _poison, other.OwnerRef, 2, Parameters(7));
            _target.Buffs.Advance(1); Assert.AreEqual(981, _target.CurrentHp); Assert.AreEqual(1, _target.Buffs.Count);
            _target.Buffs.Advance(1); Assert.AreEqual(974, _target.CurrentHp); Assert.AreEqual(0, _target.Buffs.Count);
        }

        [Test]
        public void FollowUpFactsCannotCreateAnotherAttackSkillOrBuff()
        {
            _source.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            _battle.Catalog.Add(new SkillModel(1507, SkillType.Robot, "test.empty", 1, 0, SkillTargetRule.None));
            _battle.Catalog.Add(new SkillLevelModel(1507, 1, new Dictionary<string, float>()));
            _battle.Timelines.Register(new TimelineModel("test.empty", 0, Array.Empty<TimelineNode>()));
            _battle.Grant(_source, _battle.Catalog.TryGet(1507, out var model) ? model : null);
            var attack = _battle.Combat.CaptureAttack(new CombatActionSnapshot(502, _source.InstanceId, BattleSide.Player,
                1507, new[] { _target.InstanceId }, CombatOutcomeTag.SingleDamage, 100, Vector3.zero, Vector3.one, 1));
            var projectile = Shot(100, new CombatBuffApplication(2507, 5, Parameters(10)));
            var facts = 0;
            _battle.Combat.TargetResolved += c =>
            {
                facts++;
                Assert.AreEqual(DamageOrigin.FollowUp, c.Outcome.Action.DamageOrigin);
                Assert.IsFalse(_battle.Combat.AllowsGameplayTriggers);
                Assert.Throws<InvalidOperationException>(() => Shot(100, new CombatBuffApplication(2507, 5, Parameters(10))));
                Assert.AreEqual(DamageResult.Ignored, _battle.Combat.ResolveProjectileHit(projectile, _target.InstanceId).DamageResult);
                Assert.AreEqual(0, _battle.Combat.Resolve(attack).Targets.Count);
                Assert.AreEqual(SkillCastFailure.NotAllowed, _battle.Skills.TryCast(new SkillCastRequest(_source.OwnerRef, 1507,
                    SkillTriggerSource.Button, SkillTarget.None)).Failure);
                _battle.AddBuff(_target, _slow, _source.OwnerRef, 3);
                _battle.Buffs.Submit(new AddBuffInfo(_slow, _source.OwnerRef, _target.OwnerRef, 3, true));
                _target.Buffs.Add(_slow, _source.OwnerRef, 3);
                Assert.AreEqual(DamageResult.Ignored,
                    _battle.Combat.ResolvePeriodicDamage(_source.OwnerRef, _target.OwnerRef, 1507, 100).DamageResult);
            };
            Poison(); _target.Buffs.Advance(1);
            Assert.AreEqual(1, facts); Assert.AreEqual(990, _target.CurrentHp); Assert.AreEqual(1, _target.Buffs.Count);
            Assert.IsTrue(_battle.Combat.AllowsGameplayTriggers);
        }

        [Test]
        public void PoisonBypassesEveryCombatHookAndRemovalGameplayHook()
        {
            var calls = 0;
            var hook = new AllHooks(() => calls++);
            _source.Buffs.Add(new BuffModel(1, "custom"), _source.OwnerRef, 10, behavior: hook);
            _battle.Player.Buffs.Add(new BuffModel(2, "custom"), _source.OwnerRef, 10, behavior: hook);
            _target.Buffs.Add(new BuffModel(3, "custom"), _source.OwnerRef, 10, behavior: hook);
            var targetId = _target.InstanceId;
            var kills = 0; var hits = 0;
            _battle.Combat.TargetResolved += c => { hits++; if (c.Result.IsKilled) kills++; };
            Poison(600);
            _target.Buffs.Advance(5);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(2, hits); Assert.AreEqual(1, kills);
            Assert.IsFalse(_registry.TryGet(targetId, out _));
        }

        [TestCase(false)] [TestCase(true)]
        public void ZeroDirectDamageStillAppliesEffectToLivingTarget(bool poison)
        {
            var effect = poison ? new CombatBuffApplication(2507, 5, Parameters(10)) :
                new CombatBuffApplication(2505, 3, new Dictionary<string, float> { { "slow", 0.3f } });
            var shot = Shot(0, effect);
            _battle.Combat.ResolveProjectileHit(shot, _target.InstanceId);
            Assert.AreEqual(1, _target.Buffs.Count);
            Assert.AreEqual(1000, _target.CurrentHp);
            if (poison) { _target.Buffs.Advance(1); Assert.AreEqual(990, _target.CurrentHp); }
            else Assert.That(_target.MoveSpeed, Is.EqualTo(0.7f).Within(0.00001));
        }

        [Test]
        public void RejectedFourthPoisonDoesNotCancelDirectDamage()
        {
            Poison(); Poison(); Poison();
            var before = _target.Buffs.Items.ToArray();
            var shot = Shot(20, new CombatBuffApplication(2507, 5, Parameters(1)));
            _battle.Combat.ResolveProjectileHit(shot, _target.InstanceId);
            Assert.AreEqual(980, _target.CurrentHp);
            CollectionAssert.AreEqual(before, _target.Buffs.Items);
        }

        [Test]
        public void PrimaryAttackDamageBonusDoesNotMultiplyPoisonDamage()
        {
            _source.Buffs.Add(new BuffModel(100, "bonus"), _source.OwnerRef, 10, behavior: new AttackBonus());
            var shot = Shot(20, new CombatBuffApplication(2507, 5, Parameters(10)));
            _battle.Combat.ResolveProjectileHit(shot, _target.InstanceId);
            Assert.AreEqual(960, _target.CurrentHp);
            _target.Buffs.Advance(1); Assert.AreEqual(950, _target.CurrentHp);
        }

        [Test]
        public void LethalFirstLayerDoesNotTickOtherLayersIntoReusedEnemy()
        {
            // 内存池按FIFO取用，先清空闲置项以确定下一次获取复用本次死亡对象。
            TEngine.MemoryPool.RemoveAll<EnemyEntity>();
            Poison(1000); Poison(1000); Poison(1000);
            var old = _target; var oldId = old.InstanceId;
            EnemyEntity next = null;
            _battle.Combat.TargetResolved += c =>
            {
                if (!c.Result.IsKilled) return;
                var id = _registry.CreateEnemy(2001, BattleSide.Player, 1000, 1);
                _registry.TryGet(id, out var entity); next = (EnemyEntity)entity;
            };
            var oldBuffs = old.Buffs;
            oldBuffs.Advance(5);
            Assert.IsNotNull(next); Assert.AreSame(old, next, "验证实际复用同一实体对象");
            Assert.AreNotEqual(oldId, next.InstanceId); Assert.AreEqual(1000, next.CurrentHp);
            Assert.AreEqual(0, next.Buffs.Count); Assert.IsTrue(oldBuffs.IsDisposed);
        }

        private sealed class AttackBonus : BuffBehavior
        {
            public override void OnAttack(BuffContainer c, BuffObj b, CombatOutcomeDraft d) =>
                d.AddPercent(CombatModifierGroup.GlobalDamage, 1f);
        }

        [Test]
        public void LethalHitDoesNotApplyPoisonToDeadOrRecycledEnemy()
        {
            var id = _target.InstanceId;
            var shot = Shot(1000, new CombatBuffApplication(2507, 5, Parameters(10)));
            _battle.Combat.ResolveProjectileHit(shot, id);
            Assert.IsFalse(_registry.TryGet(id, out _));
            var next = _registry.CreateEnemy(2001, BattleSide.Player, 1000, 1);
            _registry.TryGet(next, out var entity); Assert.AreEqual(0, entity.Buffs.Count);
        }

        private sealed class AllHooks : BuffBehavior
        {
            private readonly Action _called;
            internal AllHooks(Action called) { _called = called; }
            public override void OnAttack(BuffContainer c, BuffObj b, CombatOutcomeDraft d) => _called();
            public override void OnHit(BuffContainer c, BuffObj b, CombatDamageContext d) => _called();
            public override void OnBeHurt(BuffContainer c, BuffObj b, CombatDamageContext d) => _called();
            public override void OnKill(BuffContainer c, BuffObj b, CombatDamageContext d) => _called();
            public override void OnBeKilled(BuffContainer c, BuffObj b, CombatDamageContext d) => _called();
            public override void OnRemoved(BuffContainer c, BuffObj b) => _called();
        }
    }
}
