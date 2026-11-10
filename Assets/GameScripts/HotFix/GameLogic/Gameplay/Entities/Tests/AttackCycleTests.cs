using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class AttackCycleTests
    {
        [Test]
        public void ReplacementUsesConfiguredSkillAndCountsAcceptanceBeforeTimeline()
        {
            using (var battle = new BattleRuntime(81))
            {
                var catalog = Catalog(new BuffModel(91, "attack_cycle", countSkillId: 801,
                    cycleAction: AttackCycleAction.ReplaceBullet));
                catalog.Add(new SkillModel(899, SkillType.Robot, "test", 1, 0, SkillTargetRule.None));
                catalog.Add(new SkillLevelModel(899, 1, new Dictionary<string, float>()));
                battle.Configure(catalog);
                var emitted = Register(battle);
                var robot = Robot(battle);
                battle.Grant(robot, Get(catalog, 899));
                var counter = Counter(robot, 91);
                Assert.IsTrue(Cast(battle, robot, 801).Accepted);
                Assert.AreEqual(1, counter.AttackCount);
                Assert.IsTrue(Cast(battle, robot, 899).Accepted);
                Assert.AreEqual(1, counter.AttackCount);
                Assert.IsTrue(Cast(battle, robot, 801).Accepted);
                Assert.AreEqual(991, emitted.Casts.Last().BulletId);
                Assert.AreEqual(0, counter.AttackCount);
                Assert.AreEqual(990, Get(catalog, 801).BulletId, "不能修改共享技能配置");
            }
        }

        [Test]
        public void TwoPassivesOnSameRobotKeepIndependentRequestsAndOnlyConsumeTheirOwn()
        {
            using (var battle = new BattleRuntime(82))
            {
                var catalog = Catalog(
                    new BuffModel(91, "attack_cycle", triggerSkillId: 802, countSkillId: 801, cycleAction: AttackCycleAction.CastSkill),
                    new BuffModel(92, "attack_cycle", triggerSkillId: 802, countSkillId: 801, cycleAction: AttackCycleAction.CastSkill));
                battle.Configure(catalog);
                var action = Register(battle); var robot = Robot(battle);
                Assert.IsTrue(Cast(battle, robot, 801).Accepted);
                Assert.IsTrue(Cast(battle, robot, 801).Accepted);
                Assert.AreEqual(2, battle.TriggeredSkills.Count);
                action.Ready = false; battle.TriggeredSkills.Process();
                Assert.AreEqual(2, battle.TriggeredSkills.Count);
                Assert.AreEqual(2, Counter(robot, 91).AttackCount);
                Assert.IsTrue(Cast(battle, robot, 801).Failure == SkillCastFailure.NotPrepared);
                Assert.AreEqual(2, Counter(robot, 92).AttackCount);
                action.Ready = true; battle.TriggeredSkills.Process();
                Assert.AreEqual(2, action.Casts.Count(c => c.SkillId == 802));
                Assert.AreEqual(0, battle.TriggeredSkills.Count);
                Assert.AreEqual(0, Counter(robot, 91).AttackCount);
                Assert.AreEqual(0, Counter(robot, 92).AttackCount);
                battle.TriggeredSkills.Process();
                Assert.AreEqual(2, action.Casts.Count(c => c.SkillId == 802));
            }
        }

        [Test]
        public void RemovingOnePassiveOnlyCancelsItsOwnPendingRequest()
        {
            using (var battle = new BattleRuntime(83))
            {
                battle.Configure(Catalog(
                    new BuffModel(91, "attack_cycle", triggerSkillId: 802, countSkillId: 801, cycleAction: AttackCycleAction.CastSkill),
                    new BuffModel(92, "attack_cycle", triggerSkillId: 802, countSkillId: 801, cycleAction: AttackCycleAction.CastSkill)));
                var action = Register(battle); var robot = Robot(battle);
                Cast(battle, robot, 801); Cast(battle, robot, 801);
                robot.Buffs.Remove(robot.Buffs.Items.Single(b => b.Model.Id == 91));
                Assert.AreEqual(1, battle.TriggeredSkills.Count);
                battle.TriggeredSkills.Process();
                Assert.AreEqual(1, action.Casts.Count(c => c.SkillId == 802));
                Assert.AreEqual(0, Counter(robot, 92).AttackCount);
            }
        }

        [Test]
        public void TriggeredSkillCannotBypassChargeByCastingDirectly()
        {
            using (var battle = new BattleRuntime(85))
            {
                battle.Configure(Catalog(new BuffModel(91, "attack_cycle", triggerSkillId: 802,
                    countSkillId: 801, cycleAction: AttackCycleAction.CastSkill)));
                Register(battle); var robot = Robot(battle);
                Assert.AreEqual(SkillCastFailure.Cancelled, Cast(battle, robot, 802).Failure);
                Assert.AreEqual(0, Counter(robot, 91).AttackCount);
            }
        }

        [Test]
        public void TriggeredSkillCanPreserveAnEntityTargetInsteadOfAlwaysUsingPosition()
        {
            using (var battle = new BattleRuntime(84))
            {
                var catalog = Catalog(new BuffModel(91, "attack_cycle", triggerSkillId: 802,
                    countSkillId: 801, cycleAction: AttackCycleAction.CastSkill), target: SkillTargetRule.SameSideEnemy);
                battle.Configure(catalog); var action = Register(battle); var robot = Robot(battle);
                var enemy = battle.Registry.CreateEnemy(2001, BattleSide.Player, 100, 1);
                var target = SkillTarget.Entity(BattleSide.Player, enemy, Vector3.right);
                for (var i = 0; i < 2; i++) Assert.IsTrue(battle.Skills.TryCast(
                    new SkillCastRequest(robot.OwnerRef, 801, SkillTriggerSource.Contact, target)).Accepted);
                battle.TriggeredSkills.Process();
                var cast = action.Casts.Last();
                Assert.AreEqual(802, cast.SkillId);
                Assert.AreEqual(SkillTargetKind.Entity, cast.Target.Kind);
                Assert.AreEqual(enemy, cast.Target.EntityId);
            }
        }

        [Test]
        public void LostEntityTargetWaitsAndThenRetargetsWithoutSpendingCharge()
        {
            using (var battle = new BattleRuntime(86))
            {
                battle.Configure(Catalog(new BuffModel(91, "attack_cycle", triggerSkillId: 802,
                    countSkillId: 801, cycleAction: AttackCycleAction.CastSkill), target: SkillTargetRule.SameSideEnemy));
                var action = Register(battle); var robot = Robot(battle);
                var enemy = battle.Registry.CreateEnemy(2001, BattleSide.Player, 100, 1);
                var target = SkillTarget.Entity(BattleSide.Player, enemy);
                for (var i = 0; i < 2; i++) Assert.IsTrue(battle.Skills.TryCast(
                    new SkillCastRequest(robot.OwnerRef, 801, SkillTriggerSource.Contact, target)).Accepted);
                battle.Registry.ApplyDamage(enemy, 100, out _);
                battle.TriggeredSkills.Process();
                Assert.AreEqual(2, Counter(robot, 91).AttackCount);
                Assert.AreEqual(1, battle.TriggeredSkills.Count);
                var replacement = battle.Registry.CreateEnemy(2001, BattleSide.Player, 100, 1);
                battle.TriggeredSkills.Process();
                Assert.AreEqual(replacement, action.Casts.Last().Target.EntityId);
                Assert.AreEqual(0, Counter(robot, 91).AttackCount);
                Assert.AreEqual(0, battle.TriggeredSkills.Count);
            }
        }

        [Test]
        public void ExportedPassivesShareBehaviorAndAllThresholdsUseAttackCycleCount()
        {
            var tables = Tables();
            foreach (var pair in new[] { (2601, 1603, 3), (2602, 1605, 5), (2603, 1606, 5), (2604, 1607, 5) })
            {
                var buff = tables.TbBuff.Get(pair.Item1);
                Assert.AreEqual("attack_cycle", buff.BehaviorKey);
                Assert.AreEqual(pair.Item2, buff.CountSkillId);
                Assert.AreEqual(pair.Item1 == 2602 ? "replace_bullet" : "cast_skill", buff.CycleAction);
                for (var level = 1; level <= 5; level++)
                    Assert.AreEqual(pair.Item3, tables.TbSkillLevel.Get(pair.Item2 * 100 + level).AttackCycleCount);
            }
            Assert.IsNull(typeof(GameConfig.skill.SkillLevel).GetField("JuiceRequired"));
            Assert.IsNull(typeof(GameConfig.skill.SkillLevel).GetField("ContactsRequired"));
            Assert.IsNull(typeof(IBuffBehavior).GetMethod("OnAttackLaunched"));
        }

        [TestCase("CountSkillId", 0)]
        [TestCase("CountSkillId", 1605)]
        [TestCase("CycleAction", "unknown")]
        [TestCase("TriggerSkillId", 1603)]
        public void InvalidCycleConfigurationRejectsBeforeCombat(string field, object value)
        {
            var tables = Tables();
            typeof(GameConfig.buff.Buff).GetField(field).SetValue(tables.TbBuff.Get(2601), value);
            Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
        }

        private static Tables Tables() => new Tables(file => new ByteBuf(File.ReadAllBytes(
            Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", file + ".bytes"))));
        private static SkillCatalog Catalog(BuffModel first, BuffModel second = null, SkillTargetRule target = SkillTargetRule.None)
        {
            var catalog = new SkillCatalog();
            catalog.Add(first); if (second != null) catalog.Add(second);
            catalog.Add(new SkillModel(801, SkillType.Robot, "test", 1, 0, target,
                second == null ? new[] { first.Id } : new[] { first.Id, second.Id }, 990));
            catalog.Add(new SkillLevelModel(801, 1, new Dictionary<string, float> { { "attackCycleCount", 2 }, { "cycleBulletId", 991 } }));
            catalog.Add(new SkillModel(802, SkillType.Robot, "test", 1, 0, target));
            catalog.Add(new SkillLevelModel(802, 1, new Dictionary<string, float>()));
            return catalog;
        }
        private static SkillModel Get(SkillCatalog catalog, int id) { catalog.TryGet(id, out var model); return model; }
        private static RobotEntity Robot(BattleRuntime battle)
        {
            var robot = battle.Registry.CreateRobot(901, BattleSide.Player, 1, 801);
            robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0)); return robot;
        }
        private static AttackCycleBuffBehavior Counter(RobotEntity robot, int id) =>
            (AttackCycleBuffBehavior)robot.Buffs.Items.Single(b => b.Model.Id == id).Behavior;
        private static SkillCastResult Cast(BattleRuntime battle, RobotEntity robot, int id) =>
            battle.Skills.TryCast(new SkillCastRequest(robot.OwnerRef, id, SkillTriggerSource.Contact, SkillTarget.None));
        private static ActionRecorder Register(BattleRuntime battle)
        {
            var action = new ActionRecorder(); battle.Timelines.RegisterAction("record", action);
            battle.Timelines.Register(new TimelineModel("test", 0, new[] { new TimelineNode(0, "record") })); return action;
        }
        private sealed class ActionRecorder : ITimelineAction
        {
            internal bool Ready = true;
            internal readonly List<SkillCastSnapshot> Casts = new List<SkillCastSnapshot>();
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute)
            { execute = () => Casts.Add(cast); return Ready; }
        }
    }
}
