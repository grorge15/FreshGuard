using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class ChainCycleTests
    {
        [Test]
        public void CastChoosesFifthBulletWithoutSpendingFailedAttempts()
        {
            using (var f = new Fixture())
            {
                var skip = true;
                f.Action.Launch = cast =>
                {
                    if (skip) { skip = false; return; }
                    f.Battle.Timelines.NotifyAttackLaunched(cast);
                };
                for (var i = 0; i < 11; i++) Assert.IsTrue(f.Cast(f.First).Accepted);
                CollectionAssert.AreEqual(new[] { 3605, 3605, 3605, 3605, 3605, 3600, 3605, 3605, 3605, 3605, 3600 },
                    f.Action.Executed.Select(c => c.BulletId));
                Assert.AreEqual(0, f.Counter(f.First).AttackCount);
            }
        }

        [Test]
        public void FailedPreparationAndCancelledCastOrTimelineDoNotSpendCounter()
        {
            using (var f = new Fixture(true))
            {
                f.Action.Prepared = false;
                Assert.AreEqual(SkillCastFailure.NotPrepared, f.Cast(f.First).Failure);
                Assert.AreEqual(0, f.Counter(f.First).AttackCount);
                f.Action.Prepared = true;
                var gate = new CastGate();
                f.First.Buffs.Add(new BuffModel(9, "test", priority: 100), f.First.OwnerRef, 10, behavior: gate);
                Assert.AreEqual(SkillCastFailure.Cancelled, f.Cast(f.First).Failure);
                Assert.AreEqual(0, f.Counter(f.First).AttackCount);
                gate.Cancel = false;
                Assert.IsTrue(f.Cast(f.First).Accepted);
                f.Battle.Timelines.Cancel(f.First.OwnerRef);
                f.Battle.Timelines.Advance(1);
                Assert.AreEqual(0, f.Action.Executed.Count);
                Assert.AreEqual(0, f.Counter(f.First).AttackCount);
                Assert.IsTrue(f.Cast(f.First).Accepted);
                f.Battle.Timelines.Advance(1);
                Assert.AreEqual(1, f.Counter(f.First).AttackCount);
            }
        }

        [Test]
        public void MultipleLaunchNotificationsInOneCastOnlyCommitOnce()
        {
            using (var f = new Fixture())
            {
                f.Action.Launch = cast =>
                {
                    for (var pellet = 0; pellet < 5; pellet++) f.Battle.Timelines.NotifyAttackLaunched(cast);
                };
                Assert.IsTrue(f.Cast(f.First).Accepted);
                Assert.AreEqual(1, f.Counter(f.First).AttackCount);
                f.Battle.Timelines.NotifyAttackLaunched(f.Action.Executed[0]);
                Assert.AreEqual(1, f.Counter(f.First).AttackCount);
                Assert.IsTrue(f.Cast(f.First).Accepted);
                Assert.AreEqual(2, f.Counter(f.First).AttackCount);
            }
        }

        [Test]
        public void RobotsKeepIndependentCountersAndRefreshDoesNotResetProgress()
        {
            using (var f = new Fixture())
            {
                var second = f.Robot();
                for (var i = 0; i < 4; i++) Assert.IsTrue(f.Cast(f.First).Accepted);
                Assert.IsTrue(f.Cast(second).Accepted);
                var original = f.Counter(f.First);
                f.Refresh(f.First);
                Assert.AreSame(original, f.Counter(f.First));
                Assert.AreEqual(4, original.AttackCount);
                Assert.AreEqual(1, f.Counter(second).AttackCount);
                Assert.IsTrue(f.Cast(f.First).Accepted);
                Assert.AreEqual(3600, f.Action.Executed.Last().BulletId);
                Assert.IsTrue(f.Cast(second).Accepted);
                Assert.AreEqual(3605, f.Action.Executed.Last().BulletId);
                Assert.AreEqual(0, original.AttackCount);
                Assert.AreEqual(2, f.Counter(second).AttackCount);
            }
        }

        [Test]
        public void UpgradeKeepsCounterAndNextCastUsesNewLevelCycle()
        {
            using (var f = new Fixture())
            {
                for (var i = 0; i < 3; i++) Assert.IsTrue(f.Cast(f.First).Accepted);
                Assert.IsTrue(f.First.TryUpgrade());
                Assert.AreEqual(3, f.Counter(f.First).AttackCount);
                Assert.IsTrue(f.Cast(f.First).Accepted);
                Assert.AreEqual(3600, f.Action.Executed.Last().BulletId, "二级周期为4，应沿用之前已发射的三次");
                Assert.AreEqual(0, f.Counter(f.First).AttackCount);
            }
        }

        [Test]
        public void PendingCastKeepsItsLevelParametersAcrossUpgrade()
        {
            using (var f = new Fixture(true))
            {
                Assert.IsTrue(f.Cast(f.First).Accepted);
                var pending = f.Action.LastPrepared;
                Assert.IsTrue(f.First.TryUpgrade());
                f.Battle.Timelines.Advance(1);
                Assert.AreEqual(1, pending.Level);
                Assert.AreEqual(5, pending.Get("attackCycleCount"));
                Assert.AreEqual(20, pending.Get("damage"));
                Assert.AreEqual(1, f.Counter(f.First).AttackCount);
                Assert.IsTrue(f.Cast(f.First).Accepted);
                Assert.AreEqual(2, f.Action.LastPrepared.Level);
                Assert.AreEqual(4, f.Action.LastPrepared.Get("attackCycleCount"));
                Assert.AreEqual(30, f.Action.LastPrepared.Get("damage"));
            }
        }

        [Test]
        public void RecreatedPassiveRejectsNotificationFromItsRemovedPredecessor()
        {
            using (var f = new Fixture(true))
            {
                Assert.IsTrue(f.Cast(f.First).Accepted);
                var oldBuff = f.First.Buffs.Items.Single(b => b.Model.Id == 2602);
                var oldCast = f.Action.LastPrepared;
                f.First.Buffs.Remove(oldBuff);
                f.Refresh(f.First);
                var current = f.First.Buffs.Items.Single(b => b.Model.Id == 2602);
                Assert.AreNotEqual(oldCast.CycleBuffInstanceId, current.InstanceId);
                f.Battle.Timelines.Advance(1);
                Assert.AreEqual(0, f.Counter(f.First).AttackCount);
                Assert.IsTrue(f.Cast(f.First).Accepted);
                f.Battle.Timelines.Advance(1);
                Assert.AreEqual(1, f.Counter(f.First).AttackCount);
            }
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly BattleRuntime Battle = new BattleRuntime(711);
            internal readonly RobotEntity First;
            internal readonly LaunchAction Action = new LaunchAction();
            private readonly BuffModel _passive = new BuffModel(2602, "chain_cycle");
            internal Fixture(bool delayed = false)
            {
                var catalog = new SkillCatalog();
                catalog.Add(_passive);
                catalog.Add(new SkillModel(1605, SkillType.Robot, "test", 5, 0, SkillTargetRule.None,
                    new[] { 2602 }, 3605));
                catalog.Add(new SkillLevelModel(1605, 1, Parameters(5, 20)));
                catalog.Add(new SkillLevelModel(1605, 2, Parameters(4, 30)));
                Battle.Configure(catalog);
                Action.Launch = cast => Battle.Timelines.NotifyAttackLaunched(cast);
                Battle.Timelines.RegisterAction("launch", Action);
                Battle.Timelines.Register(new TimelineModel("test", delayed ? 1 : 0,
                    new[] { new TimelineNode(delayed ? 0.5f : 0, "launch") }));
                First = Robot();
            }
            private static Dictionary<string, float> Parameters(int cycle, int damage) =>
                new Dictionary<string, float> { { "attackCycleCount", cycle }, { "chainBulletId", 3600 }, { "damage", damage } };
            internal RobotEntity Robot()
            {
                var robot = Battle.Registry.CreateRobot(701, BattleSide.Player, 1, 1605);
                Assert.IsTrue(robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0)));
                return robot;
            }
            internal SkillCastResult Cast(RobotEntity robot) => Battle.Skills.TryCast(new SkillCastRequest(robot.OwnerRef,
                1605, SkillTriggerSource.AI, SkillTarget.None));
            internal ChainCycleBuffBehavior Counter(RobotEntity robot) =>
                (ChainCycleBuffBehavior)robot.Buffs.Items.Single(b => b.Model.Id == 2602).Behavior;
            internal void Refresh(RobotEntity robot) => Battle.Buffs.Submit(
                new AddBuffInfo(_passive, robot.OwnerRef, robot.OwnerRef, permanent: true));
            public void Dispose() => Battle.Dispose();
        }
        private sealed class LaunchAction : ITimelineAction
        {
            internal bool Prepared = true;
            internal Action<SkillCastSnapshot> Launch;
            internal SkillCastSnapshot LastPrepared;
            internal readonly List<SkillCastSnapshot> Executed = new List<SkillCastSnapshot>();
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute)
            {
                LastPrepared = cast;
                execute = () => { Executed.Add(cast); Launch(cast); };
                return Prepared;
            }
        }
        private sealed class CastGate : BuffBehavior
        {
            internal bool Cancel = true;
            public override void OnCast(BuffContainer container, BuffObj buff, SkillCastContext cast)
            { if (Cancel) cast.Cancel(); }
        }
    }
}
