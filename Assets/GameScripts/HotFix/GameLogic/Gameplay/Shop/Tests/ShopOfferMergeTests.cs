using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TEngine;

namespace GameLogic.Tests
{
    public class ShopOfferMergeTests
    {
        private static ShopOffer OfferAt(BattleShopContext context, int slot) =>
            context.GetState(BattleSide.Player).FindSlot(slot)?.Offer;

        private static RobotDragSource Source(BattleShopContext context, int slot = 0) =>
            RobotDragSource.Offer(slot, OfferAt(context, slot).OfferId);

        private static RobotDragSession Begin(BattleShopContext context, int slot = 0)
        {
            RobotDragSession session;
            Assert.AreEqual(ShopOperationResult.Success,
                context.TryBeginDrag(BattleSide.Player, Source(context, slot), out session, allowUnaffordableOffer: true));
            return session;
        }

        private static void SetCoins(BattleShopContext context, int coins) =>
            typeof(BattleShopState).GetProperty("EnergyCoins").SetValue(context.GetState(BattleSide.Player), coins);

        private static void SetOffer(BattleShopContext context, int slot, int level, int robotId = 501)
        {
            var current = OfferAt(context, slot);
            var offer = (ShopOffer)Activator.CreateInstance(typeof(ShopOffer), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { current.OfferId, robotId, level, context.Rules.GetPrice(2, level) }, null);
            typeof(ShopSlot).GetProperty("Offer").SetValue(context.GetState(BattleSide.Player).FindSlot(slot), offer);
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void MergeWithoutCoinsCreatesUnpurchasedNextLevelOfferAndNoEntity(int level)
        {
            using (var context = ShopTestData.Context())
            {
                SetOffer(context, 0, level);
                SetOffer(context, 1, level);
                SetCoins(context, 0);
                var source = Source(context);
                var target = OfferAt(context, 1);
                Assert.AreEqual(ShopOperationResult.Success,
                    context.CheckOfferMerge(BattleSide.Player, source, 1, target.OfferId));
                var session = Begin(context);
                Assert.AreEqual(ShopOperationResult.Success,
                    context.TryCommitOfferMerge(session.InteractionId, 1, target.OfferId));
                var merged = OfferAt(context, 1);
                Assert.IsNull(OfferAt(context, 0));
                Assert.AreEqual(501, merged.RobotId);
                Assert.AreEqual(level + 1, merged.Level);
                Assert.AreEqual(6 * (level + 1), merged.Price);
                Assert.AreNotEqual(source.OfferId, merged.OfferId);
                Assert.AreNotEqual(target.OfferId, merged.OfferId);
                Assert.IsFalse(context.GetState(BattleSide.Player).FindSlot(1).IsPurchased);
                Assert.AreEqual(0, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.IsFalse(context.IsBusy);
                Assert.AreEqual(ShopOperationResult.InvalidSource,
                    context.TryCommitOfferMerge(session.InteractionId, 1, target.OfferId));
                Assert.AreEqual(ShopOperationResult.StaleOffer,
                    context.CheckOfferMerge(BattleSide.Player, source, 1, merged.OfferId));
                Assert.AreEqual(ShopOperationResult.StaleOffer,
                    context.CheckOfferMerge(BattleSide.Player, Source(context, 2), 1, target.OfferId));
                SetCoins(context, merged.Price);
                RobotEntity robot;
                Assert.AreEqual(ShopOperationResult.Success,
                    context.TryPurchase(BattleSide.Player, 1, merged.OfferId, out robot));
                Assert.AreEqual(1, robot.InstanceId, "合成不应分配战斗实体ID。");
                Assert.AreEqual(level + 1, robot.Level);
                Assert.AreEqual(0, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(ShopOperationResult.AlreadyPurchased,
                    context.TryPurchase(BattleSide.Player, 1, merged.OfferId, out robot));
                Assert.AreEqual(0, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [TestCase(502, 1, ShopOperationResult.IncompatibleMerge)]
        [TestCase(501, 2, ShopOperationResult.IncompatibleMerge)]
        [TestCase(501, 5, ShopOperationResult.MaxLevelReached)]
        public void IncompatibleOrMaxLevelDoesNotConsumeEitherOffer(int targetRobot, int targetLevel,
            ShopOperationResult expected)
        {
            using (var context = ShopTestData.Context())
            {
                if (targetLevel == 5) SetOffer(context, 0, 5);
                SetOffer(context, 1, targetLevel, targetRobot);
                var source = OfferAt(context, 0);
                var target = OfferAt(context, 1);
                var session = Begin(context);
                Assert.AreEqual(expected, context.TryCommitOfferMerge(session.InteractionId, 1, target.OfferId));
                Assert.AreSame(source, OfferAt(context, 0));
                Assert.AreSame(target, OfferAt(context, 1));
                context.CancelInteraction(session.InteractionId);
                Assert.IsFalse(context.IsBusy);
                Assert.AreEqual(20, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void SourceEmptyPurchasedAndStaleTargetsAreRejectedAndCancelRestoresOffers()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = ShopTestData.Purchase(context, 2);
                var source = OfferAt(context, 0);
                var target = OfferAt(context, 1);
                var session = Begin(context);
                Assert.AreEqual(ShopOperationResult.IncompatibleMerge,
                    context.TryCommitOfferMerge(session.InteractionId, 0, source.OfferId));
                Assert.AreEqual(ShopOperationResult.AlreadyPurchased,
                    context.TryCommitOfferMerge(session.InteractionId, 2, 0));
                Assert.AreEqual(ShopOperationResult.StaleOffer,
                    context.TryCommitOfferMerge(session.InteractionId, 1, target.OfferId + 1000));
                Assert.AreEqual(ShopOperationResult.InvalidSlot,
                    context.TryCommitOfferMerge(session.InteractionId, 3, 0));
                context.CancelInteraction(session.InteractionId);
                Assert.AreSame(source, OfferAt(context, 0));
                Assert.AreSame(target, OfferAt(context, 1));
                Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
                var owned = RobotDragSource.Owned(robot.InstanceId);
                Assert.AreEqual(ShopOperationResult.IncompatibleMerge,
                    context.CheckOfferMerge(BattleSide.Player, owned, 1, target.OfferId));
                session = Begin(context);
                Assert.AreEqual(ShopOperationResult.Success,
                    context.TryCommitOfferMerge(session.InteractionId, 1, target.OfferId));
                session = Begin(context, 1);
                Assert.AreEqual(ShopOperationResult.StaleOffer,
                    context.TryCommitOfferMerge(session.InteractionId, 0, source.OfferId));
                context.CancelInteraction(session.InteractionId);
            }
        }

        [Test]
        public void FreePickupDoesNotBypassPurchaseChecksForStorageDeployOrBoardMerge()
        {
            using (var context = ShopTestData.Context())
            {
                var target = ShopTestData.Purchase(context, 2);
                var board = new BoardModel(5, 2);
                Assert.AreEqual(ShopOperationResult.Success, context.TryDeploy(BattleSide.Player, target.InstanceId,
                    BattleSide.Player, board, new BoardCoordinate(0, 0)));
                SetCoins(context, 0);
                var source = OfferAt(context, 0);
                var session = Begin(context);
                RobotEntity created;
                Assert.AreEqual(ShopOperationResult.InsufficientCoins,
                    context.TryCommitStorage(session.InteractionId, out created));
                Assert.IsNull(created);
                Assert.AreEqual(ShopOperationResult.InsufficientCoins,
                    context.TryCommitDeploy(session.InteractionId, BattleSide.Player, board,
                        new BoardCoordinate(2, 0), out created));
                Assert.IsNull(created);
                Assert.AreEqual(ShopOperationResult.InsufficientCoins,
                    context.TryCommitMerge(session.InteractionId, target.InstanceId));
                Assert.AreEqual(1, target.Level);
                Assert.AreSame(source, OfferAt(context, 0));
                Assert.IsNull(board.GetOccupant(new BoardCoordinate(2, 0)));
                context.CancelInteraction(session.InteractionId);
            }
        }

        [Test]
        public void MergeNotificationContainsFinalStateAndRejectsReentryWithoutBoardMergeEvent()
        {
            using (var context = ShopTestData.Context())
            {
                var target = OfferAt(context, 1);
                var session = Begin(context);
                var notifications = 0;
                var boardMerges = 0;
                Action<int> changed = side =>
                {
                    notifications++;
                    Assert.IsNull(OfferAt(context, 0));
                    Assert.AreEqual(2, OfferAt(context, 1).Level);
                    Assert.IsNull(context.ActiveInteraction);
                    Assert.AreEqual(ShopOperationResult.Busy,
                        context.TryCommitOfferMerge(session.InteractionId, 1, target.OfferId));
                    Assert.AreEqual(ShopOperationResult.Busy, context.TryRefresh(BattleSide.Player));
                };
                Action<RobotMergeSnapshot> merged = snapshot => boardMerges++;
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, changed);
                GameEvent.AddEventListener<RobotMergeSnapshot>(BattleShopEvents.Merged, merged);
                try
                {
                    Assert.AreEqual(ShopOperationResult.Success,
                        context.TryCommitOfferMerge(session.InteractionId, 1, target.OfferId));
                }
                finally
                {
                    GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, changed);
                    GameEvent.RemoveEventListener<RobotMergeSnapshot>(BattleShopEvents.Merged, merged);
                }
                Assert.AreEqual(1, notifications);
                Assert.AreEqual(0, boardMerges);
                Assert.AreEqual(20, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void RefreshReplacesMergedOfferAndOldBattleCannotCommitIntoNewBattle()
        {
            using (var context = ShopTestData.Context())
            using (var next = ShopTestData.Context())
            {
                var target = OfferAt(context, 1);
                var session = Begin(context);
                Assert.AreEqual(ShopOperationResult.Busy, context.TryRefresh(BattleSide.Player));
                Assert.AreEqual(ShopOperationResult.Success,
                    context.TryCommitOfferMerge(session.InteractionId, 1, target.OfferId));
                var merged = Source(context, 1);
                Assert.AreEqual(ShopOperationResult.Success, context.TryRefresh(BattleSide.Player));
                RobotDragSession stale;
                Assert.AreEqual(ShopOperationResult.StaleOffer,
                    context.TryBeginDrag(BattleSide.Player, merged, out stale, allowUnaffordableOffer: true));
                session = Begin(context, context.GetState(BattleSide.Player).Slots[0].SlotId);
                var nextSession = Begin(next);
                Assert.AreEqual(ShopOperationResult.InvalidSource,
                    next.TryCommitOfferMerge(session.InteractionId, 1, OfferAt(next, 1).OfferId));
                Assert.AreSame(nextSession, next.ActiveInteraction);
                context.EndBattle();
                Assert.AreEqual(ShopOperationResult.BattleEnded,
                    context.TryCommitOfferMerge(session.InteractionId, 1, target.OfferId));
            }
        }

        [Test]
        public void InvalidConfigurationAndPriceOverflowPreserveOffersAndIdentitySequence()
        {
            var config = ShopTestData.Robot(501);
            using (var context = new BattleShopContext(ShopTestData.Rules(1, 0, 0), ShopTestData.Candidates,
                ShopTestData.Candidates, id => id == 501 ? config : ShopTestData.Robot(id), new ShopSequenceRandom()))
            {
                var source = OfferAt(context, 0);
                var target = OfferAt(context, 1);
                var session = Begin(context);
                var shape = config.ShapeId_Ref;
                config.ShapeId_Ref = null;
                Assert.AreEqual(ShopOperationResult.InvalidConfiguration,
                    context.TryCommitOfferMerge(session.InteractionId, 1, target.OfferId));
                Assert.AreSame(source, OfferAt(context, 0));
                Assert.AreSame(target, OfferAt(context, 1));
                config.ShapeId_Ref = shape;
                Assert.AreEqual(ShopOperationResult.Success,
                    context.TryCommitOfferMerge(session.InteractionId, 1, target.OfferId));
                Assert.AreEqual(7, OfferAt(context, 1).OfferId);
            }
            var weights = new IReadOnlyDictionary<int, double>[]
            {
                new Dictionary<int, double> { { 1, 1 } }, new Dictionary<int, double> { { 1, 1 } },
                new Dictionary<int, double> { { 1, 1 } }
            };
            var prices = new Dictionary<int, int> { { 2, int.MaxValue / 2 }, { 3, 8 }, { 4, 12 }, { 5, 16 }, { 6, 20 } };
            using (var context = ShopTestData.Context(new ShopRules(weights, prices)))
            {
                SetOffer(context, 0, 2);
                SetOffer(context, 1, 2);
                var source = OfferAt(context, 0);
                var target = OfferAt(context, 1);
                var session = Begin(context);
                Assert.AreEqual(ShopOperationResult.InvalidConfiguration,
                    context.TryCommitOfferMerge(session.InteractionId, 1, target.OfferId));
                Assert.AreSame(source, OfferAt(context, 0));
                Assert.AreSame(target, OfferAt(context, 1));
                Assert.AreEqual(20, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(7, typeof(BattleShopContext).GetField("_nextOfferId",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(context));
                context.CancelInteraction(session.InteractionId);
            }
        }

        [TestCase(0)] [TestCase(6)]
        public void PricingRejectsLevelsOutsideExistingRobotLimits(int level)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ShopTestData.Rules().GetPrice(2, level));
        }
    }
}
