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
        [TestCase(0, false)] [TestCase(1, false)] [TestCase(2, false)]
        [TestCase(0, true)] [TestCase(1, true)] [TestCase(2, true)]
        public void DeployingOfferOrPurchasedRobotKeepsThreeSlotsAndOtherOffersInPlace(int index, bool dragOffer)
        {
            foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
            using (var context = ShopTestData.Context())
            {
                var state = context.GetState(side);
                var slots = state.Slots.ToArray();
                var offers = slots.Select(s => s.Offer).ToArray();
                var board = new BoardModel(4, 2);
                RobotEntity robot;
                if (dragOffer)
                {
                    RobotDragSession session;
                    Assert.That(context.TryBeginDrag(side, RobotDragSource.Offer(slots[index].SlotId, offers[index].OfferId), out session), Is.EqualTo(ShopOperationResult.Success));
                    Assert.That(context.TryCommitDeploy(session.InteractionId, side, board, new BoardCoordinate(0, 0), out robot), Is.EqualTo(ShopOperationResult.Success));
                }
                else
                {
                    robot = ShopTestData.Purchase(context, slots[index].SlotId, side);
                    Assert.That(context.TryDeploy(side, robot.InstanceId, side, board, new BoardCoordinate(0, 0)), Is.EqualTo(ShopOperationResult.Success));
                }
                Assert.That(state.Slots.Count, Is.EqualTo(3));
                for (var i = 0; i < slots.Length; i++)
                {
                    Assert.That(state.Slots[i], Is.SameAs(slots[i]));
                    if (i == index) Assert.That(state.Slots[i].IsEmpty, Is.True);
                    else Assert.That(state.Slots[i].Offer, Is.SameAs(offers[i]));
                }
                Assert.That(robot.Location, Is.EqualTo(RobotLocation.Board));
                Assert.That(state.EnergyCoins, Is.EqualTo(20 - offers[index].Price));
            }
        }

        [Test]
        public void ReturnUsesChosenEmptySlotOrFirstEmptySlotWithoutAddingSlots()
        {
            using (var context = ShopTestData.Context())
            {
                var state = context.GetState(BattleSide.Player);
                var slots = state.Slots.ToArray();
                var lastOffer = slots[2].Offer;
                var board = new BoardModel(8, 2);
                var first = Deployed(context, board, 0);
                var second = Deployed(context, board, 2);
                var coins = state.EnergyCoins;
                Return(context, first, slots[1].SlotId);
                Assert.That(slots[0].IsEmpty, Is.True);
                Assert.That(slots[1].InstanceId, Is.EqualTo(first.InstanceId));
                Return(context, second);
                Assert.That(slots[0].InstanceId, Is.EqualTo(second.InstanceId));
                Assert.That(slots[2].Offer, Is.SameAs(lastOffer));
                Assert.That(state.Slots, Is.EqualTo(slots));
                Assert.That(state.Slots.Count, Is.EqualTo(3));
                Assert.That(state.EnergyCoins, Is.EqualTo(coins));
            }
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
                Assert.That(state.Slots[0].InstanceId, Is.EqualTo(robot.InstanceId));
                Assert.That(robot.Location, Is.EqualTo(RobotLocation.PurchasedShopSlot));
                Assert.That(board.IsOccupied(new BoardCoordinate(0, 0)), Is.False);
                Assert.That(context.TryDeploy(BattleSide.Player, robot.InstanceId, BattleSide.Player, board, new BoardCoordinate(2, 0)), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(state.Slots.Count, Is.EqualTo(3));
                Assert.That(state.Slots[0].IsEmpty, Is.True);
                Assert.That(state.EnergyCoins, Is.EqualTo(coins));
            }
        }
        [Test]
        public void FullShopRejectsBoardReturnButAcceptsMergeWithoutSwappingTargetOntoBoard()
        {
            using (var context = ShopTestData.Context(random: new ShopSequenceRandom { DefaultValue = 0.99 }))
            {
                typeof(BattleShopState).GetProperty("EnergyCoins").SetValue(context.GetState(BattleSide.Player), 100);
                var board = new BoardModel(10, 2);
                var third = Deployed(context, board, 0);
                Assert.That(context.TryRefresh(BattleSide.Player), Is.EqualTo(ShopOperationResult.Success));
                var state = context.GetState(BattleSide.Player);
                var first = ShopTestData.Purchase(context, state.Slots[0].SlotId);
                var session = Begin(context, third);
                Assert.That(context.TryCommitShopDrop(session.InteractionId, null).ToString(), Is.EqualTo("ShopFull"));
                context.CancelInteraction(session.InteractionId);
                Assert.That(board.GetOccupant(new BoardCoordinate(0, 0)), Is.EqualTo(third.Definition.Id));
                Return(context, third, state.Slots.Single(s => s.InstanceId == first.InstanceId).SlotId);
                Assert.That(state.Slots.Count, Is.EqualTo(3));
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
                    Assert.That(state.Slots.Select(s => s.SlotId), Is.EqualTo(new[] { 0, 1, 2 }));
                    Assert.That(state.FindSlot(0).IsEmpty, Is.True);
                    Assert.That(source.IsRemoved, Is.True);
                }
                else { context.CancelInteraction(session.InteractionId); Assert.That(source.IsRemoved, Is.False); }
            }
        }

        [Test]
        public void FullShopMismatchRestoresBoardRobotWithoutSwappingShopTarget()
        {
            using (var context = ShopTestData.Context())
            {
                Coins(context, 100);
                var board = new BoardModel(12, 2);
                var c = Deployed(context, board, 0);
                Assert.That(context.TryRefresh(BattleSide.Player), Is.EqualTo(ShopOperationResult.Success));
                var a = ShopTestData.Purchase(context, context.GetState(BattleSide.Player).Slots[0].SlotId);
                UpgradeTo(a, 2);
                var target = context.GetState(BattleSide.Player).Slots.Single(s => s.InstanceId == a.InstanceId);
                var session = Begin(context, c);
                Assert.That(context.TryCommitShopDrop(session.InteractionId, target.SlotId), Is.EqualTo(ShopOperationResult.ShopFull));
                context.CancelInteraction(session.InteractionId);
                Assert.That(board.GetOccupant(new BoardCoordinate(0, 0)), Is.EqualTo(c.Definition.Id));
                Assert.That(target.InstanceId, Is.EqualTo(a.InstanceId));
                Assert.That(a.Location, Is.EqualTo(RobotLocation.PurchasedShopSlot));
                Assert.That(c.Location, Is.EqualTo(RobotLocation.Board));
                Assert.That(context.GetState(BattleSide.Player).Slots.Count, Is.EqualTo(3));
            }
        }

        [Test]
        public void MixedShopStatusesNeverConsumeOrChargeAndShopBlankIsNoOp()
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
        public void RefreshClearsReturnedAndPurchasedRobotsAndNeverReusesSlotIdentity()
        {
            using (var context = ShopTestData.Context())
            {
                Coins(context, 100);
                var boardRobot = Deployed(context, new BoardModel(8, 2));
                var stored = ShopTestData.Purchase(context, 1);
                Return(context, boardRobot);
                var state = context.GetState(BattleSide.Player);
                var oldIds = state.Slots.Select(s => s.SlotId).ToArray();
                var coins = state.EnergyCoins;
                Assert.That(context.TryRefresh(BattleSide.Player), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(state.EnergyCoins, Is.EqualTo(coins - 5));
                Assert.That(boardRobot.IsRemoved, Is.True);
                Assert.That(stored.IsRemoved, Is.True);
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
                Assert.That(context.TryDeploy(BattleSide.Player, robot.InstanceId, BattleSide.Player, new BoardModel(8, 2), new BoardCoordinate(0, 0)), Is.EqualTo(ShopOperationResult.Busy));
                Assert.That(context.TryLiftDrag(session.InteractionId), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(session.IsLifted, Is.True);
                Assert.That(context.TryCommitShopDrop(session.InteractionId, null), Is.EqualTo(ShopOperationResult.Success));
                Assert.That(context.IsBusy, Is.False);
            }
        }
    }
}

