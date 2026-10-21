using System.Collections.Generic;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class OpponentAiPlannerTests
    {
        [Test]
        public void PlansPurchaseAndDeployWhenBoardHasFewerThanThreeRobots()
        {
            var board = new BoardModel(5, 5);
            var profile = Profile(501, 10, 1f, 1);
            var offer = new OpponentAiOfferSnapshot(0, 11, profile, 1, 6, false);
            var snapshot = Snapshot(board, 20, new[] { offer }, new OpponentAiRobotSnapshot[0], profile);

            var action = OpponentAiPlanner.Plan(snapshot);

            Assert.AreEqual(OpponentAiActionKind.PurchaseAndDeploy, action.Kind);
            Assert.AreEqual(0, action.SlotId);
            Assert.IsTrue(board.Evaluate(action.Anchor, profile.Definition).IsValid);
        }

        [Test]
        public void EstablishesThreeRobotsBeforeOfferMerge()
        {
            var board = new BoardModel(5, 5);
            var profile = Profile(501, 20, 1f, 1);
            var existing = Robot(7, profile, 1, new BoardCoordinate(1, 1));
            var offer = new OpponentAiOfferSnapshot(0, 11, profile, 1, 6, false);
            var snapshot = Snapshot(board, 20, new[] { offer }, new[] { existing }, profile);
            board.TryPlace(existing.Anchor.Value, existing.Definition);

            var action = OpponentAiPlanner.Plan(snapshot);

            Assert.AreEqual(OpponentAiActionKind.PurchaseAndDeploy, action.Kind);
            Assert.AreEqual(11, action.OfferId);
        }

        [Test]
        public void DoesNotMergeExistingRobotsWhenItWouldLoseTooMuchOutput()
        {
            var board = new BoardModel(5, 5);
            var profile = Profile(501, 10, 10f, 1);
            var first = Robot(7, profile, 1, new BoardCoordinate(1, 1));
            var second = Robot(8, profile, 1, new BoardCoordinate(3, 1));
            var snapshot = Snapshot(board, 0, new OpponentAiOfferSnapshot[0], new[] { first, second }, profile);
            board.TryPlace(first.Anchor.Value, first.Definition);
            board.TryPlace(second.Anchor.Value, second.Definition);

            var action = OpponentAiPlanner.Plan(snapshot);

            Assert.AreNotEqual(OpponentAiActionKind.Merge, action.Kind);
        }

        [Test]
        public void SnapshotDoesNotFollowLiveBoardChanges()
        {
            var board = new BoardModel(5, 5);
            var profile = Profile(501, 10, 1f, 1);
            var snapshot = Snapshot(board, 20, new OpponentAiOfferSnapshot[0], new OpponentAiRobotSnapshot[0], profile);
            board.TryPlace(new BoardCoordinate(2, 2), profile.Definition);
            Assert.IsFalse(snapshot.Board.IsOccupied(new BoardCoordinate(2, 2)));
        }

        [Test]
        public void DeploymentRanksDamagePerCoinBeforePosition()
        {
            var board = new BoardModel(5, 5);
            var expensive = Profile(501, 30, 1f, 1);
            var efficient = Profile(502, 20, 1f, 1);
            var snapshot = Snapshot(board, 100, new[] {
                new OpponentAiOfferSnapshot(0, 11, expensive, 1, 30, false),
                new OpponentAiOfferSnapshot(1, 12, efficient, 1, 5, false)
            }, new OpponentAiRobotSnapshot[0], expensive, efficient);
            Assert.AreEqual(12, OpponentAiPlanner.Plan(snapshot).OfferId);
        }

        [Test]
        public void RefreshRequiresAtLeastOneCandidateShapeToFit()
        {
            var board = new BoardModel(1, 1, new BoardCoordinate[0]);
            var profile = Profile(501, 10, 1f, 1);
            var snapshot = Snapshot(board, 100, new[] {
                new OpponentAiOfferSnapshot(0, 11, profile, 1, 5, false)
            }, new OpponentAiRobotSnapshot[0], profile);
            Assert.AreEqual(OpponentAiActionKind.Wait, OpponentAiPlanner.Plan(snapshot).Kind);
        }

        [Test]
        public void DirectOfferMergeStartsAfterCoverageAndStopsAtLevelFive()
        {
            var p = Profile(501, 10, 1f, 1);
            var board = new BoardModel(5, 5);
            var robots = new[] { Robot(1, p, 1, new BoardCoordinate(1, 1)),
                Robot(2, p, 5, new BoardCoordinate(3, 1)), Robot(3, p, 5, new BoardCoordinate(1, 3)) };
            foreach (var robot in robots) board.TryPlace(robot.Anchor.Value, robot.Definition);
            var offers = new[] { new OpponentAiOfferSnapshot(0, 11, p, 1, 6, false) };
            var action = OpponentAiPlanner.Plan(Snapshot(board, 20, offers, robots, p));
            Assert.AreEqual(OpponentAiActionKind.PurchaseAndMerge, action.Kind);
            Assert.AreEqual(1, action.TargetInstanceId);
            var maxOffers = new[] { new OpponentAiOfferSnapshot(0, 12, p, 5, 6, false) };
            Assert.AreEqual(OpponentAiActionKind.PurchaseAndDeploy,
                OpponentAiPlanner.Plan(Snapshot(board, 20, maxOffers, robots, p)).Kind);
        }

        [TestCase(0, OpponentAiActionKind.Wait)]
        [TestCase(20, OpponentAiActionKind.Merge)]
        public void BoardMergeUsesLinearDamageGrowthAndKeepsThreeRobots(int growth, OpponentAiActionKind expected)
        {
            var p = new OpponentAiRobotProfile(501, 1, 10, 1f, new PlaceableDefinition("p", 1, 1), growth);
            var board = new BoardModel(5, 5);
            var robots = new[] { Robot(1, p, 1, new BoardCoordinate(1, 1)), Robot(2, p, 1, new BoardCoordinate(3, 1)),
                Robot(3, p, 1, new BoardCoordinate(1, 3)), Robot(4, p, 1, new BoardCoordinate(3, 3)) };
            foreach (var robot in robots) board.TryPlace(robot.Anchor.Value, robot.Definition);
            var options = new OpponentAiPlanningOptions { AllowMove = false, AllowRefresh = false };
            Assert.AreEqual(expected, OpponentAiPlanner.Plan(Snapshot(board, 0,
                new OpponentAiOfferSnapshot[0], robots, p), options).Kind);
            var three = new[] { robots[0], robots[1], robots[2] };
            board.Release(robots[3].Definition.Id);
            Assert.AreEqual(OpponentAiActionKind.Wait, OpponentAiPlanner.Plan(Snapshot(board, 0,
                new OpponentAiOfferSnapshot[0], three, p), options).Kind);
        }

        [Test]
        public void MoveFreesBlockedOfferWithinTenPercentOutputLossWithoutTouchingLiveBoard()
        {
            var low = Profile(501, 10, 1f, 1);
            var high = Profile(502, 100, 1f, 1);
            var square = new OpponentAiRobotProfile(505, 1, 20, 1f, new PlaceableDefinition("square", 2, 2));
            var board = new BoardModel(3, 3);
            var robots = new[] { Robot(1, low, 1, new BoardCoordinate(1, 1)), Robot(2, high, 1, new BoardCoordinate(0, 0)) };
            foreach (var robot in robots) board.TryPlace(robot.Anchor.Value, robot.Definition);
            var action = OpponentAiPlanner.Plan(Snapshot(board, 20,
                new[] { new OpponentAiOfferSnapshot(0, 19, square, 1, 6, false) }, robots, low, high, square));
            Assert.AreEqual(OpponentAiActionKind.Move, action.Kind);
            Assert.AreEqual(19, action.FreedOfferId);
            Assert.AreEqual(robots[0].Definition.Id, board.GetOccupant(new BoardCoordinate(1, 1)));
            board.Release(robots[0].Definition.Id);
            Assert.IsTrue(board.TryPlace(action.Anchor, robots[0].Definition));
            var moved = new OpponentAiRobotSnapshot(1, low, 1, 5, 10, 1f, RobotLocation.Board, action.Anchor, robots[0].Definition);
            var next = OpponentAiPlanner.Plan(Snapshot(board, 20,
                new[] { new OpponentAiOfferSnapshot(0, 19, square, 1, 6, false) }, new[] { moved, robots[1] }, square),
                new OpponentAiPlanningOptions { PreferredOfferId = action.FreedOfferId });
            Assert.AreEqual(OpponentAiActionKind.PurchaseAndDeploy, next.Kind);
            Assert.AreEqual(19, next.OfferId);
        }

        [Test]
        [TestCase(3)]
        [TestCase(4)]
        public void MergeCanFreeBlockedOfferWithSmallOutputLoss(int count)
        {
            var low = Profile(501, 10, 1f, 1);
            var high = Profile(502, 200, 1f, 1);
            var square = new OpponentAiRobotProfile(505, 1, 20, 1f, new PlaceableDefinition("square", 2, 2));
            var board = new BoardModel(3, 3);
            var robots = new List<OpponentAiRobotSnapshot> {
                Robot(1, low, 1, new BoardCoordinate(1, 1)), Robot(2, low, 1, new BoardCoordinate(0, 0)),
                Robot(3, high, 1, new BoardCoordinate(2, 0)) };
            if (count == 4) robots.Add(Robot(4, high, 1, new BoardCoordinate(0, 2)));
            foreach (var robot in robots) board.TryPlace(robot.Anchor.Value, robot.Definition);
            var action = OpponentAiPlanner.Plan(Snapshot(board, 20,
                new[] { new OpponentAiOfferSnapshot(0, 19, square, 1, 6, false) }, robots, low, high, square));
            Assert.AreEqual(OpponentAiActionKind.Merge, action.Kind);
            Assert.AreEqual(1, action.SourceInstanceId);
            Assert.AreEqual(19, action.FreedOfferId);
        }

        [Test]
        public void RefreshUsesCandidatePriceAndProtectsPurchasedSlotsEvenWithoutOffers()
        {
            var p = new OpponentAiRobotProfile(501, 1, 10, 1f, new PlaceableDefinition("p", 1, 1), levelOnePrice: 7);
            var board = new BoardModel(3, 3);
            var empty = new OpponentAiOfferSnapshot[0];
            Assert.AreEqual(OpponentAiActionKind.Wait, OpponentAiPlanner.Plan(Snapshot(board, 11, empty,
                new OpponentAiRobotSnapshot[0], p)).Kind);
            Assert.AreEqual(OpponentAiActionKind.Refresh, OpponentAiPlanner.Plan(Snapshot(board, 12, empty,
                new OpponentAiRobotSnapshot[0], p)).Kind);
            var snapshot = new OpponentAiSnapshot(BattleSide.Opponent, 100, board, empty,
                new OpponentAiRobotSnapshot[0], new[] { p }, null, hasPurchasedShopRobots: true);
            Assert.AreEqual(OpponentAiActionKind.Wait, OpponentAiPlanner.Plan(snapshot).Kind);
        }

        [Test]
        public void SnapshotCopiesListsAndOccupantsAndActionsCarryBattleIdentity()
        {
            var p = Profile(501, 10, 1f, 1);
            var board = new BoardModel(3, 3);
            board.TryPlace(new BoardCoordinate(1, 1), new PlaceableDefinition("obstacle", 1, 1));
            var offers = new List<OpponentAiOfferSnapshot> { new OpponentAiOfferSnapshot(0, 11, p, 1, 6, false) };
            var snapshot = new OpponentAiSnapshot(BattleSide.Opponent, 20, board, offers,
                new OpponentAiRobotSnapshot[0], new[] { p }, null, battleId: 123);
            offers.Clear();
            snapshot.Board.Release("obstacle");
            var action = OpponentAiPlanner.Plan(snapshot);
            Assert.AreEqual(123, action.BattleId);
            Assert.AreEqual(11, action.OfferId);
            Assert.IsFalse(action.Anchor.Equals(new BoardCoordinate(1, 1)));
            Assert.AreEqual("obstacle", snapshot.Board.GetOccupant(new BoardCoordinate(1, 1)));
        }

        [Test]
        public void CooldownAndOfferBlacklistAllowOtherDecisions()
        {
            var p = Profile(501, 10, 1f, 1);
            var offers = new[] { new OpponentAiOfferSnapshot(0, 11, p, 1, 6, false),
                new OpponentAiOfferSnapshot(1, 12, p, 1, 6, false) };
            var action = OpponentAiPlanner.Plan(Snapshot(new BoardModel(5, 5), 20, offers,
                new OpponentAiRobotSnapshot[0], p), new OpponentAiPlanningOptions {
                AllowMove = false, AllowRefresh = false, BlockedOfferIds = new long[] { 11 } });
            Assert.AreEqual(12, action.OfferId);
        }

        [Test]
        public void EqualPlacementScoresBreakTiesByRowThenColumn()
        {
            var p = Profile(501, 10, 1f, 1);
            var action = OpponentAiPlanner.Plan(Snapshot(new BoardModel(4, 4), 20,
                new[] { new OpponentAiOfferSnapshot(0, 11, p, 1, 6, false) }, new OpponentAiRobotSnapshot[0], p));
            Assert.AreEqual(new BoardCoordinate(1, 1), action.Anchor);
        }

        private static OpponentAiSnapshot Snapshot(BoardModel board, int coins,
            IReadOnlyList<OpponentAiOfferSnapshot> offers,
            IReadOnlyList<OpponentAiRobotSnapshot> robots,
            params OpponentAiRobotProfile[] profiles)
        {
            return new OpponentAiSnapshot(BattleSide.Opponent, coins, board, offers, robots, profiles, null);
        }

        private static OpponentAiRobotProfile Profile(int id, int damage, float interval, int quality)
        {
            return new OpponentAiRobotProfile(id, quality, damage, interval,
                new PlaceableDefinition("profile-" + id, new[] { new BoardCoordinate(0, 0) }));
        }

        private static OpponentAiRobotSnapshot Robot(int id, OpponentAiRobotProfile profile,
            int level, BoardCoordinate anchor)
        {
            return new OpponentAiRobotSnapshot(id, profile, level, 5, profile.Damage, profile.TriggerInterval,
                RobotLocation.Board, anchor, new PlaceableDefinition("robot-" + id,
                    new[] { new BoardCoordinate(0, 0) }));
        }
    }
}
