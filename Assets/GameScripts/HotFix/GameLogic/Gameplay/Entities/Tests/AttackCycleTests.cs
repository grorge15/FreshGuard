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
        public void AttackCycleBelongsToRobotAndNoLongerRequiresCountingBuffs()
        {
            Assert.IsNotNull(typeof(RobotEntity).GetProperty("AttackCycle"), "周期计数必须由机器人持有");
            Assert.IsNull(typeof(BuffModel).GetProperty("CountSkillId"));
            Assert.IsNull(typeof(GameConfig.buff.Buff).GetField("CountSkillId"));
            var tables = Tables();
            foreach (var id in new[] { 2601, 2602, 2603, 2604 }) Assert.IsNull(tables.TbBuff.GetOrDefault(id));
        }

        [Test]
        public void CountingBuffContractDoesNotExposeSelectableCycleActions()
        {
            Assert.IsNull(typeof(GameConfig.buff.Buff).GetField("CycleAction"), "源表不再选择周期动作");
            Assert.IsNull(typeof(BuffModel).GetProperty("CycleAction"));
            Assert.IsNull(typeof(BuffModel).Assembly.GetType("GameLogic.AttackCycleAction"));
        }

        [Test]
        public void CounterOnlyCountsRobotsOwnAttackAndQueuesAnIndependentChild()
        {
            using (var battle = new BattleRuntime(81))
            {
                var catalog = Catalog();
                catalog.Add(new SkillModel(899, SkillType.Robot, "test", 1, 0, SkillTargetRule.None));
                catalog.Add(new SkillLevelModel(899, 1, new Dictionary<string, float>()));
                battle.Configure(catalog);
                var emitted = Register(battle);
                var robot = Robot(battle);
                battle.Grant(robot, Get(catalog, 899));
                var counter = robot.AttackCycle;
                Assert.IsTrue(Cast(battle, robot, 801).Accepted);
                Assert.AreEqual(1, counter.AttackCount);
                Assert.IsTrue(Cast(battle, robot, 899).Accepted);
                Assert.AreEqual(1, counter.AttackCount);
                Assert.IsTrue(Cast(battle, robot, 801).Accepted);
                Assert.AreEqual(801, emitted.Casts.Last().SkillId);
                Assert.AreEqual(2, counter.AttackCount);
                Assert.AreEqual(1, battle.TriggeredSkills.Count);
                battle.TriggeredSkills.Process();
                Assert.AreEqual(802, emitted.Casts.Last().SkillId);
                Assert.AreEqual(0, counter.AttackCount);
                Assert.AreEqual(990, Get(catalog, 801).BulletId, "不能修改共享技能配置");
            }
        }

        [Test]
        public void TwoRobotsKeepIndependentRequestsAndOnlyConsumeTheirOwn()
        {
            using (var battle = new BattleRuntime(82))
            {
                battle.Configure(Catalog());
                var action = Register(battle); var first = Robot(battle); var second = Robot(battle);
                foreach (var robot in new[] { first, second })
                { Assert.IsTrue(Cast(battle, robot, 801).Accepted); Assert.IsTrue(Cast(battle, robot, 801).Accepted); }
                Assert.AreEqual(2, battle.TriggeredSkills.Count);
                action.Ready = false; battle.TriggeredSkills.Process();
                Assert.AreEqual(2, battle.TriggeredSkills.Count);
                Assert.AreEqual(2, first.AttackCycle.AttackCount);
                Assert.AreEqual(SkillCastFailure.NotPrepared, Cast(battle, first, 801).Failure);
                Assert.AreEqual(2, second.AttackCycle.AttackCount);
                action.Ready = true; battle.TriggeredSkills.Process();
                Assert.AreEqual(2, action.Casts.Count(c => c.SkillId == 802));
                Assert.AreEqual(0, battle.TriggeredSkills.Count);
                Assert.AreEqual(0, first.AttackCycle.AttackCount);
                Assert.AreEqual(0, second.AttackCycle.AttackCount);
                battle.TriggeredSkills.Process();
                Assert.AreEqual(2, action.Casts.Count(c => c.SkillId == 802));
            }
        }

        [Test]
        public void ResettingOneRobotOnlyCancelsItsOwnPendingRequest()
        {
            using (var battle = new BattleRuntime(83))
            {
                battle.Configure(Catalog());
                var action = Register(battle); var first = Robot(battle); var second = Robot(battle);
                foreach (var robot in new[] { first, second })
                { Cast(battle, robot, 801); Cast(battle, robot, 801); }
                first.AttackCycle.Reset();
                Assert.AreEqual(1, battle.TriggeredSkills.Count);
                battle.TriggeredSkills.Process();
                Assert.AreEqual(1, action.Casts.Count(c => c.SkillId == 802));
                Assert.AreEqual(second.OwnerRef, action.Casts.Last().Owner);
                Assert.AreEqual(0, first.AttackCycle.AttackCount);
                Assert.AreEqual(0, second.AttackCycle.AttackCount);
            }
        }

        [Test]
        public void TriggeredSkillCannotBypassChargeByCastingDirectly()
        {
            using (var battle = new BattleRuntime(85))
            {
                battle.Configure(Catalog());
                Register(battle); var robot = Robot(battle);
                Assert.AreEqual(SkillCastFailure.Cancelled, Cast(battle, robot, 802).Failure);
                Assert.AreEqual(0, robot.AttackCycle.AttackCount);
            }
        }

        [Test]
        public void ClearingBuffContainerDoesNotResetRobotsOwnProgress()
        {
            using (var battle = new BattleRuntime(88))
            {
                battle.Configure(Catalog()); Register(battle); var robot = Robot(battle);
                Assert.IsTrue(Cast(battle, robot, 801).Accepted);
                robot.Buffs.Clear();
                Assert.AreEqual(1, robot.AttackCycle.AttackCount);
                robot.AttackCycle.Configure(battle, 802);
                Assert.AreEqual(1, robot.AttackCycle.AttackCount, "重复配置不能重置固有进度");
            }
        }

        [Test]
        public void TriggeredSkillCanPreserveAnEntityTargetInsteadOfAlwaysUsingPosition()
        {
            using (var battle = new BattleRuntime(84))
            {
                var catalog = Catalog(SkillTargetRule.SameSideEnemy);
                battle.Configure(catalog); var action = Register(battle); var robot = Robot(battle);
                var enemy = battle.Registry.CreateEnemy(2001, BattleSide.Player, 100, 1);
                var target = SkillTarget.Entity(BattleSide.Player, enemy, Vector3.right);
                for (var i = 0; i < 2; i++) Assert.IsTrue(battle.Skills.TryCast(
                    new SkillCastRequest(robot.OwnerRef, 801, SkillTriggerSource.Contact, target)).Accepted);
                battle.TriggeredSkills.Process();
                var cast = action.Casts.Last();
                Assert.AreEqual(802, cast.SkillId);
                Assert.AreEqual(SkillTriggerSource.AttackCycle, cast.Trigger);
                Assert.AreEqual(SkillTargetKind.Entity, cast.Target.Kind);
                Assert.AreEqual(enemy, cast.Target.EntityId);
            }
        }

        [Test]
        public void LostEntityTargetWaitsAndThenRetargetsWithoutSpendingCharge()
        {
            using (var battle = new BattleRuntime(86))
            {
                battle.Configure(Catalog(SkillTargetRule.SameSideEnemy));
                var action = Register(battle); var robot = Robot(battle);
                var enemy = battle.Registry.CreateEnemy(2001, BattleSide.Player, 100, 1);
                var target = SkillTarget.Entity(BattleSide.Player, enemy);
                for (var i = 0; i < 2; i++) Assert.IsTrue(battle.Skills.TryCast(
                    new SkillCastRequest(robot.OwnerRef, 801, SkillTriggerSource.Contact, target)).Accepted);
                battle.Registry.ApplyDamage(enemy, 100, out _);
                battle.TriggeredSkills.Process();
                Assert.AreEqual(2, robot.AttackCycle.AttackCount);
                Assert.AreEqual(1, battle.TriggeredSkills.Count);
                var replacement = battle.Registry.CreateEnemy(2001, BattleSide.Player, 100, 1);
                battle.TriggeredSkills.Process();
                Assert.AreEqual(replacement, action.Casts.Last().Target.EntityId);
                Assert.AreEqual(0, robot.AttackCycle.AttackCount);
                Assert.AreEqual(0, battle.TriggeredSkills.Count);
            }
        }

        [Test]
        public void ExportedRobotConfigurationOwnsChildAndKeepsLevelThresholds()
        {
            var tables = Tables();
            foreach (var pair in new[] { (704, 1603, 1604, 3), (701, 1605, 1600, 5), (702, 1606, 1601, 5), (703, 1607, 1602, 5) })
            {
                var robot = tables.TbRobot.Get(pair.Item1);
                Assert.AreEqual(pair.Item2, robot.SkillId);
                Assert.AreEqual(pair.Item3, robot.AttackCycleSkillId);
                Assert.IsEmpty(tables.TbSkill.Get(pair.Item2).PassiveBuffIds);
                for (var level = 1; level <= 5; level++)
                    Assert.AreEqual(pair.Item4, tables.TbSkillLevel.Get(pair.Item2 * 100 + level).AttackCycleCount);
            }
            Assert.IsNull(typeof(IBuffBehavior).GetMethod("OnAttackLaunched"));
        }

        [TestCase(-1)]
        [TestCase(1603)]
        [TestCase(99999)]
        public void InvalidRobotChildConfigurationRejectsBeforeCombat(int childId)
        {
            var tables = Tables();
            typeof(GameConfig.robot.Robot).GetField("AttackCycleSkillId").SetValue(tables.TbRobot.Get(704), childId);
            Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
        }

        [Test]
        public void DisabledChildOfEnabledRobotRejectsBeforeCombat()
        {
            var tables = Tables();
            typeof(GameConfig.skill.Skill).GetField("Enabled").SetValue(tables.TbSkill.Get(1604), false);
            Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
        }

        [Test]
        public void ZeroThresholdOfConfiguredRobotRejectsBeforeCombat()
        {
            var tables = Tables();
            typeof(GameConfig.skill.SkillLevel).GetField("AttackCycleCount").SetValue(tables.TbSkillLevel.Get(160301), 0);
            Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
        }

        private static Tables Tables() => new Tables(file => new ByteBuf(File.ReadAllBytes(
            Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", file + ".bytes"))));
        private static SkillCatalog Catalog(SkillTargetRule target = SkillTargetRule.None)
        {
            var catalog = new SkillCatalog();
            catalog.Add(new SkillModel(801, SkillType.Robot, "test", 1, 0, target, bulletId: 990));
            catalog.Add(new SkillLevelModel(801, 1, new Dictionary<string, float> { { "attackCycleCount", 2 } }));
            catalog.Add(new SkillModel(802, SkillType.Robot, "test", 1, 0, target));
            catalog.Add(new SkillLevelModel(802, 1, new Dictionary<string, float>()));
            return catalog;
        }
        private static SkillModel Get(SkillCatalog catalog, int id) { catalog.TryGet(id, out var model); return model; }
        private static RobotEntity Robot(BattleRuntime battle)
        {
            var robot = battle.Registry.CreateRobot(901, BattleSide.Player, 1, 801);
            robot.AttackCycle.Configure(battle, 802);
            robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0)); return robot;
        }
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
