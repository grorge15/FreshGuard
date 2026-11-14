using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class JuiceChargeTests
    {
        [Test]
        public void SixAcceptedCastsProduceTwoExtraVolleysWithoutLaunchCallback()
        {
            using (var f = new Fixture())
            {
                for (var i = 0; i < 6; i++)
                {
                    Assert.IsTrue(f.Cast().Accepted);
                    Assert.AreEqual(i % 3 + 1, f.Counter.AttackCount);
                    f.Battle.TriggeredSkills.Process();
                }
                Assert.AreEqual(6, f.Action.Casts.Count(c => c.SkillId == 1603));
                Assert.AreEqual(2, f.Action.Casts.Count(c => c.SkillId == 1604));
                Assert.AreEqual(0, f.Counter.AttackCount);
                Assert.IsFalse(f.Counter.Pending);
            }
        }

        [Test]
        public void PreparationCancellationAndContactFailureDoNotCommitJuice()
        {
            using (var f = new Fixture())
            {
                f.Action.FailNormal = true;
                Assert.AreEqual(SkillCastFailure.NotPrepared, f.Cast().Failure);
                f.Action.FailNormal = false;
                Assert.AreEqual(SkillCastFailure.NotAllowed, f.Battle.Skills.TryCast(f.Request(), () => false).Failure);
                f.Robot.Buffs.Add(new BuffModel(90, "test", priority: -10), f.Robot.OwnerRef, 100, behavior: new CancelCast());
                Assert.AreEqual(SkillCastFailure.Cancelled, f.Cast().Failure);
                Assert.AreEqual(0, f.Counter.AttackCount);
                f.Robot.Buffs.Remove(f.Robot.Buffs.Items.Single(b => b.Model.Id == 90));
                Assert.IsTrue(f.Cast().Accepted);
                Assert.AreEqual(1, f.Counter.AttackCount);
            }
        }

        [Test]
        public void FullChargeSurvivesFailedChildAndCapsWhileNormalAttacksContinue()
        {
            using (var f = new Fixture())
            {
                f.Action.FailChild = true;
                for (var i = 0; i < 9; i++) { Assert.IsTrue(f.Cast().Accepted); f.Battle.TriggeredSkills.Process(); }
                Assert.AreEqual(3, f.Counter.AttackCount);
                Assert.AreEqual(1, f.Battle.TriggeredSkills.Count);
                Assert.AreEqual(9, f.Action.Casts.Count);
                f.Action.FailChild = false;
                f.Battle.TriggeredSkills.Process(); f.Battle.TriggeredSkills.Process();
                Assert.AreEqual(0, f.Counter.AttackCount);
                Assert.AreEqual(1, f.Action.Casts.Count(c => c.SkillId == 1604));
            }
        }

        [Test]
        public void PauseAndCycleResetPreventPendingChildFromCasting()
        {
            using (var f = new Fixture())
            {
                for (var i = 0; i < 3; i++) Assert.IsTrue(f.Cast().Accepted);
                f.Battle.CanCastNow = () => false;
                f.Battle.TriggeredSkills.Process();
                Assert.AreEqual(3, f.Counter.AttackCount);
                f.Robot.AttackCycle.Reset();
                Assert.AreEqual(0, f.Battle.TriggeredSkills.Count);
                f.Battle.CanCastNow = () => true;
                f.Battle.TriggeredSkills.Process();
                Assert.AreEqual(3, f.Action.Casts.Count);
            }
        }

        [Test]
        public void ChildIsGrantedOnceAndUpgradeKeepsChargeAndUpdatesBothLevels()
        {
            using (var f = new Fixture())
            {
                var original = f.Counter;
                Assert.IsTrue(f.Cast().Accepted);
                Assert.IsTrue(f.Robot.TryUpgrade());
                Assert.AreSame(original, f.Counter);
                Assert.AreEqual(1, f.Counter.AttackCount);
                Assert.IsTrue(f.Robot.Skills.TryGet(1604, out var child));
                Assert.AreEqual(2, child.Level);
                Assert.IsTrue(f.Robot.Skills.TryGet(1603, out var main));
                f.Battle.Grant(f.Robot, main.Model);
                Assert.IsEmpty(f.Robot.Buffs.Items);
                f.Robot.AttackCycle.Configure(f.Battle, 1604);
                Assert.AreSame(original, f.Counter);
                Assert.AreEqual(1, f.Counter.AttackCount);
                Assert.IsTrue(f.Cast().Accepted); Assert.IsTrue(f.Cast().Accepted);
                f.Battle.TriggeredSkills.Process();
                Assert.AreEqual(2, f.Action.Casts.Last().Level);
            }
        }

        [Test]
        public void DifferentRobotsDoNotShareJuice()
        {
            using (var f = new Fixture())
            {
                var second = f.Battle.Registry.CreateRobot(704, BattleSide.Player, 1, 1603);
                second.AttackCycle.Configure(f.Battle, 1604);
                second.TrySetLocation(RobotLocation.Board, new BoardCoordinate(1, 0));
                for (var i = 0; i < 3; i++) Assert.IsTrue(f.Cast().Accepted);
                Assert.IsTrue(f.Battle.Skills.TryCast(new SkillCastRequest(second.OwnerRef, 1603, SkillTriggerSource.AI, SkillTarget.None)).Accepted);
                var other = second.AttackCycle;
                Assert.AreEqual(1, other.AttackCount);
                f.Battle.TriggeredSkills.Process();
                Assert.AreEqual(0, f.Counter.AttackCount); Assert.AreEqual(1, other.AttackCount);
            }
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly BattleRuntime Battle = new BattleRuntime(704);
            internal readonly RobotEntity Robot;
            internal readonly CastAction Action = new CastAction();
            internal RobotAttackCycle Counter => Robot.AttackCycle;
            internal Fixture()
            {
                var catalog = new SkillCatalog();
                catalog.Add(new SkillModel(1603, SkillType.NormalAttack, "test", 5, 0, SkillTargetRule.None));
                catalog.Add(new SkillModel(1604, SkillType.Robot, "test", 5, 0, SkillTargetRule.SameSidePosition));
                for (var i = 1; i <= 5; i++)
                {
                    catalog.Add(new SkillLevelModel(1603, i, new Dictionary<string,float> { { "attackCycleCount", 3 } }));
                    catalog.Add(new SkillLevelModel(1604, i, new Dictionary<string,float> { { "damage", 6 + i * 6 } }));
                }
                Battle.Configure(catalog);
                Battle.Timelines.RegisterAction("test", Action);
                Battle.Timelines.Register(new TimelineModel("test", 0, new[] { new TimelineNode(0, "test") }));
                Robot = Battle.Registry.CreateRobot(704, BattleSide.Player, 1, 1603);
                Robot.AttackCycle.Configure(Battle, 1604);
                Robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            }
            internal SkillCastRequest Request() => new SkillCastRequest(Robot.OwnerRef, 1603, SkillTriggerSource.AI, SkillTarget.None);
            internal SkillCastResult Cast() => Battle.Skills.TryCast(Request());
            public void Dispose() => Battle.Dispose();
        }
        private sealed class CastAction : ITimelineAction
        {
            internal bool FailNormal, FailChild;
            internal readonly List<SkillCastSnapshot> Casts = new List<SkillCastSnapshot>();
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute)
            { execute = () => Casts.Add(cast); return cast.SkillId == 1603 ? !FailNormal : !FailChild; }
        }
        private sealed class CancelCast : BuffBehavior
        { public override void OnCast(BuffContainer container, BuffObj buff, SkillCastContext context) => context.Cancel(); }
    }
}
