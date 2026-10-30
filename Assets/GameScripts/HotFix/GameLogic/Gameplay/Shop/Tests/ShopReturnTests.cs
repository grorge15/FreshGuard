using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class ShopReturnTests
    {
        private static RobotDragSession Begin(BattleShopContext context, RobotEntity robot)
        {
            RobotDragSession session;
            Assert.That(context.TryBeginDrag(BattleSide.Player, RobotDragSource.Owned(robot.InstanceId), out session), Is.EqualTo(ShopOperationResult.Success));
            return session;
        }
        private static RobotEntity Deployed(BattleShopContext context, BoardModel board, int column = 0)
        {
            var slot = context.GetState(BattleSide.Player).Slots.First(s => s.Offer != null);
            RobotEntity robot;
            Assert.That(context.TryPurchase(BattleSide.Player, slot.SlotId, slot.Offer.OfferId, out robot), Is.EqualTo(ShopOperationResult.Success));
            Assert.That(context.TryDeploy(BattleSide.Player, robot.InstanceId, BattleSide.Player, board, new BoardCoordinate(column, 0)), Is.EqualTo(ShopOperationResult.Success));
            return robot;
        }
        private static void Return(BattleShopContext context, RobotEntity robot, int? target = null)
        {
            var session = Begin(context, robot);
            Assert.That(context.TryCommitShopDrop(session.InteractionId, target), Is.EqualTo(ShopOperationResult.Success));
        }
        [Test]
        public void BoardReturnKeepsIdentityAndMoneyAndRedeployIsFree()
        {
            using (var context = ShopTestData.Context())
            {
                var board = new BoardModel(8, 2);
                var robot = Deployed(context, board);
                var state = context.GetState(BattleSide.Player);
                var coins = state.EnergyCoins;
                Return(context, robot, state.Slots[0].SlotId);
                Assert.That(state.Slots.Count, Is.EqualTo(3));
                Assert.That(state.Slots.Last().InstanceId, Is.EqualTo(robot.InstanceId));
                Assert.That(robot.Location, Is.EqualTo(RobotLocation.PurchasedShopSlot));
                Assert.That(board.IsOccupied(new BoardCoordinate(0, 0)), Is.False);
                Assert.That(context.TryDeploy(BattleSide.Player, robot.InstanceId, BattleSide.Player, board, new BoardCoordinate(2, 0)), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(state.Slots.Count, Is.EqualTo(2));
                Assert.That(state.EnergyCoins, Is.EqualTo(coins));
            }
        }
        [Test]
        public void FullShopRejectsBoardReturnButAcceptsMergeWithoutSwappingTargetOntoBoard()
        {
            using (var context = ShopTestData.Context())
            {
                typeof(BattleShopState).GetProperty("EnergyCoins").SetValue(context.GetState(BattleSide.Player), 100);
                var board = new BoardModel(10, 2);
                var first = Deployed(context, board, 0);
                var second = Deployed(context, board, 2);
                var third = Deployed(context, board, 4);
                Assert.That(context.TryRefresh(BattleSide.Player), Is.EqualTo(ShopOperationResult.Success));
                Return(context, first); Return(context, second);
                var state = context.GetState(BattleSide.Player);
                var session = Begin(context, third);
                Assert.That(context.TryCommitShopDrop(session.InteractionId, null).ToString(), Is.EqualTo("ShopFull"));
                context.CancelInteraction(session.InteractionId);
                Assert.That(board.GetOccupant(new BoardCoordinate(4, 0)), Is.EqualTo(third.Definition.Id));
                Return(context, third, state.Slots.Single(s => s.InstanceId == first.InstanceId).SlotId);
                Assert.That(state.Slots.Count, Is.EqualTo(5));
                Assert.That(first.Level, Is.EqualTo(2));
                Assert.That(first.Location, Is.EqualTo(RobotLocation.PurchasedShopSlot));
                Assert.That(third.IsRemoved, Is.True);
            }
        }
        [Test]
        public void PressReservationBlocksCommandsWithoutReleasingBoardAndCancellationKeepsFootprint()
        {
            using (var context = ShopTestData.Context())
            {
                var board = new BoardModel(8, 2); var robot = Deployed(context, board);
                RobotDragSession session;
                Assert.That(context.TryReserveDrag(BattleSide.Player, RobotDragSource.Owned(robot.InstanceId), out session), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(context.IsBusy, Is.True);
                Assert.That(board.GetOccupant(new BoardCoordinate(0, 0)), Is.EqualTo(robot.Definition.Id));
                Assert.That(context.TryRefresh(BattleSide.Player), Is.EqualTo(ShopOperationResult.Busy));
                Assert.That(context.TryDeploy(BattleSide.Player, robot.InstanceId, BattleSide.Player, board, new BoardCoordinate(2, 0)), Is.EqualTo(ShopOperationResult.Busy));
                context.CancelInteraction(session.InteractionId);
                Assert.That(context.IsBusy, Is.False);
                Assert.That(board.GetOccupant(new BoardCoordinate(1, 0)), Is.EqualTo(robot.Definition.Id));
                Assert.That(robot.IsDragging, Is.False);
            }
        }

        private static void Coins(BattleShopContext context, int coins) =>
            typeof(BattleShopState).GetProperty("EnergyCoins").SetValue(context.GetState(BattleSide.Player), coins);

        private static void UpgradeTo(RobotEntity robot, int level)
        {
            var upgrade = typeof(RobotEntity).GetMethod("TryUpgrade", BindingFlags.Instance | BindingFlags.NonPublic);
            while (robot.Level < level) Assert.That(upgrade.Invoke(robot, null), Is.True);
        }

        [TestCase(4, 5, ShopOperationResult.Success)]
        [TestCase(5, 5, ShopOperationResult.MaxLevelReached)]
        public void PurchasedShopMergeKeepsTargetIdentityAndOrderAndHonorsLevelCap(int level, int expectedLevel, ShopOperationResult expected)
        {
            using (var context = ShopTestData.Context())
            {
                var source = ShopTestData.Purchase(context);
                var target = ShopTestData.Purchase(context, 1);
                UpgradeTo(source, level); UpgradeTo(target, level);
                var state = context.GetState(BattleSide.Player);
                var session = Begin(context, source);
                Assert.That(context.CheckShopDrop(BattleSide.Player, session.Source, 1), Is.EqualTo(expected));
                Assert.That(context.TryCommitShopDrop(session.InteractionId, 1), Is.EqualTo(expected));
                Assert.That(target.Level, Is.EqualTo(expectedLevel));
                Assert.That(state.EnergyCoins, Is.EqualTo(8));
                Assert.That(state.FindSlot(1).InstanceId, Is.EqualTo(target.InstanceId));
                if (expected == ShopOperationResult.Success)
                {
                    Assert.That(state.Slots.Select(s => s.SlotId), Is.EqualTo(new[] { 1, 2 }));
                    Assert.That(source.IsRemoved, Is.True);
                }
                else { context.CancelInteraction(session.InteractionId); Assert.That(source.IsRemoved, Is.False); }
            }
        }

        [Test]
        public void FullShopStorageMismatchSwapsButBoardMismatchReturnsAndRestoresFootprint()
        {
            using (var context = ShopTestData.Context())
            {
                Coins(context, 100);
                var board = new BoardModel(12, 2);
                var a = Deployed(context, board, 0); var b = Deployed(context, board, 2); var c = Deployed(context, board, 4);
                Assert.That(context.TryRefresh(BattleSide.Player), Is.EqualTo(ShopOperationResult.Success));
                Return(context, a); Return(context, b);
                UpgradeTo(a, 2);
                var target = context.GetState(BattleSide.Player).Slots.Single(s => s.InstanceId == a.InstanceId);
                var session = Begin(context, c);
                Assert.That(context.TryCommitShopDrop(session.InteractionId, target.SlotId), Is.EqualTo(ShopOperationResult.ShopFull));
                context.CancelInteraction(session.InteractionId);
                Assert.That(board.GetOccupant(new BoardCoordinate(4, 0)), Is.EqualTo(c.Definition.Id));
                Assert.That(context.TryStoreOrSwap(BattleSide.Player, c.InstanceId), Is.EqualTo(ShopOperationResult.Success));
                session = Begin(context, c);
                Assert.That(context.TryCommitShopDrop(session.InteractionId, target.SlotId), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(context.GetState(BattleSide.Player).StorageInstanceId, Is.EqualTo(a.InstanceId));
                Assert.That(a.Location, Is.EqualTo(RobotLocation.Storage));
                Assert.That(target.InstanceId, Is.EqualTo(c.InstanceId));
                Assert.That(c.Location, Is.EqualTo(RobotLocation.PurchasedShopSlot));
                Assert.That(context.GetState(BattleSide.Player).Slots.Count, Is.EqualTo(5));
            }
        }

        [Test]
        public void MixedShopStatusesNeverConsumeOrChargeAndStoreBlankIsNoOp()
        {
            using (var context = ShopTestData.Context())
            {
                var owned = ShopTestData.Purchase(context);
                var state = context.GetState(BattleSide.Player);
                var offer = state.FindSlot(1).Offer;
                var session = Begin(context, owned);
                Assert.That(context.TryCommitShopDrop(session.InteractionId, 1), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(state.Slots.Count, Is.EqualTo(3));
                Assert.That(state.FindSlot(0).InstanceId, Is.EqualTo(owned.InstanceId));
                Assert.That(state.FindSlot(1).Offer, Is.SameAs(offer));
                Assert.That(context.TryBeginDrag(BattleSide.Player, RobotDragSource.Offer(1, offer.OfferId), out session), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(context.TryCommitShopDrop(session.InteractionId, 0), Is.EqualTo(ShopOperationResult.IncompatibleMerge));
                context.CancelInteraction(session.InteractionId);
                Assert.That(state.EnergyCoins, Is.EqualTo(14));
                Assert.That(owned.Level, Is.EqualTo(1));
                Assert.That(state.FindSlot(1).Offer, Is.SameAs(offer));
            }
        }

        [Test]
        public void DeletedOrForeignTargetDoesNotFallBackToReturnOrSwap()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = Deployed(context, new BoardModel(8, 2));
                var state = context.GetState(BattleSide.Player);
                var count = state.Slots.Count;
                var session = Begin(context, robot);
                Assert.That(context.TryCommitShopDrop(session.InteractionId, 999), Is.EqualTo(ShopOperationResult.InvalidSlot));
                Assert.That(state.Slots.Count, Is.EqualTo(count));
                context.CancelInteraction(session.InteractionId);
                Assert.That(robot.Location, Is.EqualTo(RobotLocation.Board));
            }
        }

        [Test]
        public void RefreshClearsReturnedRobotButKeepsStorageAndNeverReusesSlotIdentity()
        {
            using (var context = ShopTestData.Context())
            {
                Coins(context, 100);
                var boardRobot = Deployed(context, new BoardModel(8, 2));
                var stored = ShopTestData.Purchase(context, 1);
                context.TryStoreOrSwap(BattleSide.Player, stored.InstanceId);
                Return(context, boardRobot);
                var state = context.GetState(BattleSide.Player);
                var oldIds = state.Slots.Select(s => s.SlotId).ToArray();
                var coins = state.EnergyCoins;
                Assert.That(context.TryRefresh(BattleSide.Player), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(state.EnergyCoins, Is.EqualTo(coins - 5));
                Assert.That(boardRobot.IsRemoved, Is.True);
                Assert.That(stored.IsRemoved, Is.False);
                Assert.That(state.StorageInstanceId, Is.EqualTo(stored.InstanceId));
                Assert.That(state.Slots.Count, Is.EqualTo(3));
                Assert.That(state.Slots.All(s => s.Offer != null && !oldIds.Contains(s.SlotId)), Is.True);
            }
        }

        [Test]
        public void ReservedSessionMustLiftBeforeCommitAndRejectsLegacySameSourceCommand()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = Deployed(context, new BoardModel(8, 2));
                RobotDragSession session;
                Assert.That(context.TryReserveDrag(BattleSide.Player, RobotDragSource.Owned(robot.InstanceId), out session), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(session.IsLifted, Is.False);
                Assert.That(context.TryCommitShopDrop(session.InteractionId, null), Is.EqualTo(ShopOperationResult.InvalidSource));
                Assert.That(context.TryStoreOrSwap(BattleSide.Player, robot.InstanceId), Is.EqualTo(ShopOperationResult.Busy));
                Assert.That(context.TryLiftDrag(session.InteractionId), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(session.IsLifted, Is.True);
                Assert.That(context.TryCommitShopDrop(session.InteractionId, null), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(context.IsBusy, Is.False);
            }
        }
    }
}

