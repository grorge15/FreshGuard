using System;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public class RobotEntityTests
    {
        private BattleEntityRegistry _registry;
        private RobotEntity _robot;

        [SetUp]
        public void SetUp()
        {
            _registry = new BattleEntityRegistry();
            _robot = _registry.CreateRobot(101, BattleSide.Player, 1, 1001);
        }

        [TearDown]
        public void TearDown() { _registry.Dispose(); }

        [Test]
        public void DraggingSuspendsParticipationButPreservesSourceAndProtection()
        {
            var anchor = new BoardCoordinate(2, 3);
            Assert.IsFalse(_robot.CanParticipate);
            Assert.IsFalse(_robot.TryConsumeAttackTrigger(2f));
            Assert.IsTrue(_robot.TrySetLocation(RobotLocation.Board, anchor));
            Assert.IsTrue(_robot.TryConsumeAttackTrigger(2f));
            Assert.IsFalse(_robot.TryConsumeAttackTrigger(2f));
            Assert.IsTrue(_robot.TrySetDragging(true));
            Assert.IsFalse(_robot.CanParticipate);
            _registry.AdvanceTime(0.5f);
            Assert.AreEqual(1.5f, _robot.TriggerCooldownRemaining);
            Assert.IsTrue(_robot.TrySetDragging(false));
            Assert.AreEqual(anchor, _robot.Anchor);
            Assert.IsTrue(_robot.CanParticipate);
            Assert.IsTrue(_robot.TrySetLocation(RobotLocation.Storage, null));
            Assert.IsFalse(_robot.CanParticipate);
            Assert.IsNull(_robot.Anchor);
            Assert.AreEqual(1.5f, _robot.TriggerCooldownRemaining);
        }

        [Test]
        public void InvalidPlacementDoesNotPartiallyCommitOrEndDrag()
        {
            var anchor = new BoardCoordinate(2, 3);
            _robot.TrySetLocation(RobotLocation.Board, anchor);
            _robot.TrySetDragging(true);
            Assert.IsFalse(_robot.TrySetLocation(RobotLocation.Board, null));
            Assert.IsFalse(_robot.TrySetLocation(RobotLocation.Storage, anchor));
            Assert.IsFalse(_robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(-1, 0)));
            Assert.IsFalse(_robot.TrySetLocation((RobotLocation)99, null));
            Assert.AreEqual(RobotLocation.Board, _robot.Location);
            Assert.AreEqual(anchor, _robot.Anchor);
            Assert.IsTrue(_robot.IsDragging);
            Assert.IsTrue(_robot.TrySetLocation(RobotLocation.PurchasedShopSlot, null));
            Assert.IsFalse(_robot.IsDragging);
        }

        [Test]
        public void UpgradeStopsAtLimitAndResetsOnlyOwnProtection()
        {
            var limited = _registry.CreateRobot(101, BattleSide.Player, 1, 1001, 1, 2);
            limited.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            _robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(1, 0));
            limited.TryConsumeAttackTrigger(3f);
            _robot.TryConsumeAttackTrigger(3f);
            Assert.IsTrue(limited.TryUpgrade());
            Assert.AreEqual(2, limited.Level);
            Assert.AreEqual(0f, limited.TriggerCooldownRemaining);
            Assert.AreEqual(3f, _robot.TriggerCooldownRemaining);
            Assert.IsFalse(limited.TryUpgrade());
            Assert.AreEqual(2, limited.Level);
        }

        [Test]
        public void OnlyEffectiveTimeAdvancesProtectionAndItClampsAtZero()
        {
            _robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            _robot.TryConsumeAttackTrigger(3f);
            _registry.AdvanceTime(0f);
            Assert.AreEqual(3f, _robot.TriggerCooldownRemaining);
            _registry.AdvanceTime(1f);
            Assert.AreEqual(2f, _robot.TriggerCooldownRemaining);
            _registry.AdvanceTime(10f);
            Assert.AreEqual(0f, _robot.TriggerCooldownRemaining);
            Assert.IsTrue(_robot.TryConsumeAttackTrigger(0f));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidTimeNeverChangesProtection(float value)
        {
            _robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            _robot.TryConsumeAttackTrigger(3f);
            Assert.Throws<ArgumentOutOfRangeException>(() => _robot.AdvanceTime(value));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.AdvanceTime(value));
            Assert.Throws<ArgumentOutOfRangeException>(() => _robot.TryConsumeAttackTrigger(value));
            Assert.AreEqual(3f, _robot.TriggerCooldownRemaining);
        }

        [Test]
        public void ConsumptionSnapshotsLevelAndPermanentlyDisablesRobot()
        {
            _robot.TryUpgrade();
            _robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            _robot.TryConsumeAttackTrigger(3f);
            _robot.TrySetDragging(true);
            EntityRemovalSnapshot removal;
            Assert.IsTrue(_registry.Remove(_robot.InstanceId, EntityRemovalReason.Consumed, out removal));
            Assert.AreEqual(EntityKind.Robot, removal.Kind);
            Assert.AreEqual(2, removal.RobotLevel);
            Assert.IsNull(removal.EnemyHp);
            Assert.IsNull(removal.EnemyRouteProgress);
            Assert.AreEqual(RobotLocation.Unplaced, _robot.Location);
            Assert.IsNull(_robot.Anchor);
            Assert.IsFalse(_robot.IsDragging);
            Assert.AreEqual(0f, _robot.TriggerCooldownRemaining);
            Assert.IsTrue(_robot.IsRemoved);
            Assert.IsFalse(_robot.TryUpgrade());
            Assert.IsFalse(_robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0)));
            Assert.IsFalse(_robot.TrySetDragging(true));
            Assert.IsFalse(_robot.TryConsumeAttackTrigger(1f));
        }
    }
}
