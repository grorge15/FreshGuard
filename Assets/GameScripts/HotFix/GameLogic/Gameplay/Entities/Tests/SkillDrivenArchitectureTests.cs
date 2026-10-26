using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class SkillDrivenArchitectureTests
    {
        [Test]
        public void SkillTimelineLocksParametersAndTargetAtCastTime()
        {
            using (var battle = new BattleRuntime(901))
            {
                var values = new Dictionary<string, float> { { "damage", 37f }, { "slow", 0.4f }, { "duration", 3f } };
                var model = new SkillModel(1505, SkillType.Battlefield, "capture", 1, 0f, SkillTargetRule.SameSideEnemy);
                battle.Catalog.Add(model);
                battle.Catalog.Add(new SkillLevelModel(1505, 1, values));
                battle.Grant(battle.Player, model);
                var target = battle.Registry.CreateEnemy(2001, BattleSide.Player, 100, 10f);
                SkillCastSnapshot captured = null;
                battle.Timelines.RegisterAction("capture", new CaptureAction(snapshot => captured = snapshot));
                battle.Timelines.Register(new TimelineModel("capture", 0.5f, new[] { new TimelineNode(0.5f, "capture") }));

                Assert.IsTrue(battle.Skills.TryCast(new SkillCastRequest(battle.Player.OwnerRef, 1505,
                    SkillTriggerSource.Button, SkillTarget.Entity(BattleSide.Player, target))).Accepted);
                values["damage"] = 999f;
                values["slow"] = 0.9f;
                battle.Advance(0.5f);

                Assert.IsNotNull(captured);
                Assert.AreEqual(target, captured.Target.EntityId);
                Assert.AreEqual(37f, captured.Get("damage"));
                Assert.AreEqual(0.4f, captured.Get("slow"));
                Assert.AreEqual(3f, captured.Get("duration"));
                Assert.AreEqual(0, battle.Timelines.Count);
            }
        }

        [Test]
        public void SlowBuffsRefreshPerSourceAndUseTheStrongestActiveEntry()
        {
            using (var battle = new BattleRuntime(902))
            {
                var enemy = CreateEnemy(battle);
                AddSlow(battle, enemy, battle.Player.OwnerRef, 0.2f, 3f);
                AddSlow(battle, enemy, battle.Opponent.OwnerRef, 0.35f, 1f);
                Assert.That(enemy.EffectiveSpeed, Is.EqualTo(6.5f).Within(0.0001f));

                enemy.Buffs.Advance(1.1f);
                Assert.That(enemy.EffectiveSpeed, Is.EqualTo(8f).Within(0.0001f));
                AddSlow(battle, enemy, battle.Player.OwnerRef, 0.4f, 2f);
                Assert.AreEqual(1, enemy.Buffs.Count);
                Assert.That(enemy.EffectiveSpeed, Is.EqualTo(6f).Within(0.0001f));

                enemy.Buffs.Advance(2f);
                Assert.AreEqual(0, enemy.Buffs.Count);
                Assert.AreEqual(10f, enemy.EffectiveSpeed);
            }
        }

        [Test]
        public void SlowSpeedNeverFallsBelowHalfOfBaseSpeed()
        {
            using (var battle = new BattleRuntime(903))
            {
                var enemy = CreateEnemy(battle);
                AddSlow(battle, enemy, battle.Player.OwnerRef, 1f, 5f);
                Assert.AreEqual(5f, enemy.EffectiveSpeed);
            }
        }

        [Test]
        public void BattleRuntimeDisposesSkillsBuffsAndCombatTogether()
        {
            using (var battle = new BattleRuntime(904))
            {
                var model = new SkillModel(1, SkillType.Battlefield, "pending", 1, 0f, SkillTargetRule.None);
                battle.Catalog.Add(model);
                battle.Catalog.Add(new SkillLevelModel(1, 1, null));
                battle.Grant(battle.Player, model);
                battle.Timelines.Register(new TimelineModel("pending", 1f, Array.Empty<TimelineNode>()));
                Assert.IsTrue(battle.Skills.TryCast(new SkillCastRequest(battle.Player.OwnerRef, 1,
                    SkillTriggerSource.Button, SkillTarget.None)).Accepted);
                battle.Advance(0.25f);
                Assert.That(battle.Combat.BattleTime, Is.EqualTo(0.25f).Within(0.0001f));

                battle.Dispose();

                Assert.IsTrue(battle.Combat.IsDisposed);
                Assert.AreEqual(0, battle.Player.Skills.Count);
                Assert.AreEqual(0, battle.Timelines.Count);
                Assert.AreEqual(SkillCastFailure.BattleEnded, battle.Skills.TryCast(new SkillCastRequest(
                    battle.Player.OwnerRef, 1, SkillTriggerSource.Button, SkillTarget.None)).Failure);
            }
        }

        [TestCase(100, false)]
        [TestCase(30, true)]
        public void DamagePrecedesSlowAndOnlySurvivingTargetsReceiveIt(int hp, bool killed)
        {
            using (var battle = new BattleRuntime(905))
            {
                var source = battle.Registry.CreateRobot(501, BattleSide.Player, 1, 1505);
                var target = battle.Registry.CreateEnemy(2001, BattleSide.Player, hp, 10f);
                battle.Registry.TryGet(target, out var raw);
                var enemy = (EnemyEntity)raw;
                var observedHp = -1;
                battle.Catalog.Add(new BuffModel(2505, "slow"));
                battle.Combat.DamageApplying += context =>
                {
                    Assert.AreEqual(hp, enemy.CurrentHp);
                    Assert.AreEqual(0, enemy.Buffs.Count, "伤害提交前不应提前附加减速。");
                };
                battle.Combat.TargetResolved += context =>
                {
                    if (!context.Result.IsKilled) observedHp = enemy.CurrentHp;
                };
                var action = battle.Combat.CaptureAttack(new CombatActionSnapshot(battle.BattleId,
                    source.InstanceId, source.Side, 1505, new[] { target }, CombatOutcomeTag.SingleDamage,
                    37, Vector3.zero, Vector3.one, 1, hitBuffs: new[]
                    {
                        new CombatBuffApplication(2505, 3f, new Dictionary<string, float> { { "slow", 0.4f } })
                    }));

                var result = battle.Combat.Resolve(action);

                Assert.AreEqual(killed, result.Targets[0].IsKilled);
                if (killed)
                {
                    Assert.AreEqual(-1, observedHp);
                    Assert.IsFalse(battle.Registry.TryGet(target, out _));
                }
                else
                {
                    Assert.AreEqual(63, observedHp);
                    Assert.AreEqual(1, enemy.Buffs.Count);
                    Assert.That(enemy.EffectiveSpeed, Is.EqualTo(6f).Within(0.0001f));
                }
            }
        }

        [Test]
        public void PooledEnemyReuseStartsWithoutSlowBuffs()
        {
            using (var battle = new BattleRuntime(906))
            {
                var enemy = CreateEnemy(battle);
                var id = enemy.InstanceId;
                AddSlow(battle, enemy, battle.Player.OwnerRef, 0.8f, 2f);
                Assert.AreEqual(5f, enemy.EffectiveSpeed);

                Assert.IsTrue(battle.Registry.Remove(id, EntityRemovalReason.Manual, out _));
                var next = CreateEnemy(battle);

                Assert.AreNotEqual(id, next.InstanceId);
                Assert.AreEqual(0, next.Buffs.Count);
                Assert.AreEqual(10f, next.EffectiveSpeed);
            }
        }

        private static EnemyEntity CreateEnemy(BattleRuntime battle)
        {
            var id = battle.Registry.CreateEnemy(2001, BattleSide.Player, 100, 10f);
            Assert.IsTrue(battle.Registry.TryGet(id, out var entity));
            return (EnemyEntity)entity;
        }

        private static void AddSlow(BattleRuntime battle, EnemyEntity enemy, SkillOwnerRef source, float ratio, float duration)
            => battle.AddBuff(enemy, new BuffModel(2505, "slow"), source, duration,
                new Dictionary<string, float> { { "slow", ratio } });

        private sealed class CaptureAction : ITimelineAction
        {
            private readonly Action<SkillCastSnapshot> _capture;
            public CaptureAction(Action<SkillCastSnapshot> capture) { _capture = capture; }
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute)
            {
                execute = () => _capture(cast);
                return true;
            }
        }

    }
}
