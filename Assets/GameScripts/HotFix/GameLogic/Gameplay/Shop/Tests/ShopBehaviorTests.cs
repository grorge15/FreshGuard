using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TEngine;

namespace GameLogic.Tests
{
    public class ShopBehaviorTests
    {
        [TestCase(2, 1, 6)]
        [TestCase(3, 1, 8)]
        [TestCase(4, 1, 12)]
        [TestCase(5, 1, 16)]
        [TestCase(6, 1, 20)]
        [TestCase(2, 2, 12)]
        [TestCase(3, 2, 16)]
        [TestCase(4, 2, 24)]
        [TestCase(5, 2, 32)]
        [TestCase(6, 2, 40)]
        public void QualityAndStarDetermineLiteralPrice(int quality, int level, int price)
        {
            Assert.AreEqual(price, ShopTestData.Rules().GetPrice(quality, level));
        }

        [Test]
        public void FirstBatchHasNoRefreshFeeButPurchasingLevelOneOffersCostsCoins()
        {
            var random = new ShopSequenceRandom { DefaultValue = 0.99 };
            using (var context = ShopTestData.Context(ShopTestData.Rules(), random))
            {
                Assert.Greater(context.BattleId, 0L);
                var player = context.GetState(BattleSide.Player);
                var opponent = context.GetState(BattleSide.Opponent);
                Assert.AreNotSame(player, opponent);
                foreach (var state in new[] { player, opponent })
                {
                    Assert.AreEqual(20, state.EnergyCoins);
                    Assert.AreEqual(3, state.Slots.Count);
                    Assert.IsNull(state.StorageInstanceId);
                    CollectionAssert.AreEqual(new[] { 1003, 1003, 1003 }, state.LastBatchRobotIds);
                    foreach (var slot in state.Slots)
                    {
                        Assert.AreEqual(1, slot.Offer.Level);
                        Assert.AreEqual(6, slot.Offer.Price);
                        Assert.IsNull(slot.InstanceId);
                        Assert.IsFalse(slot.IsPurchased);
                    }
                }
                Assert.AreEqual(6, random.DoubleCalls, "首批只独立抽六次机器人，不抽二星。");
                ShopTestData.Purchase(context);
                Assert.AreEqual(14, player.EnergyCoins);
                Assert.AreEqual(20, opponent.EnergyCoins);
                Assert.IsNotNull(opponent.Slots[0].Offer);
                Assert.IsFalse(opponent.Slots[0].IsPurchased);
            }
        }

        [Test]
        public void WeightedSlotsAreIndependentAndExcludeZeroWeightCandidates()
        {
            var random = new ShopSequenceRandom();
            random.Enqueue(0, 0.499, 0.5, 0.99, 0.1, 0.7);
            using (var context = ShopTestData.Context(ShopTestData.Rules(1, 0, 1), random))
            {
                CollectionAssert.AreEqual(new[] { 1001, 1001, 1003 }, context.GetState(BattleSide.Player).LastBatchRobotIds);
                CollectionAssert.AreEqual(new[] { 1003, 1001, 1003 }, context.GetState(BattleSide.Opponent).LastBatchRobotIds);
            }
        }

        [Test]
        public void PurchaseImmediatelyOwnsRegisteredEntityAndCannotRepeatOrReuseStaleOffer()
        {
            using (var context = ShopTestData.Context())
            {
                var offerId = context.GetState(BattleSide.Player).Slots[0].Offer.OfferId;
                var robot = ShopTestData.Purchase(context);
                Assert.AreEqual(RobotLocation.PurchasedShopSlot, robot.Location);
                Assert.AreEqual(robot.InstanceId, context.GetState(BattleSide.Player).Slots[0].InstanceId);
                Assert.IsNull(context.GetState(BattleSide.Player).Slots[0].Offer);
                Entity registered;
                Assert.IsTrue(context.Registry.TryGet(robot.InstanceId, out registered));
                Assert.AreSame(robot, registered);
                RobotEntity repeated;
                Assert.AreEqual(ShopOperationResult.AlreadyPurchased,
                    context.TryPurchase(BattleSide.Player, 0, offerId, out repeated));
                Assert.IsNull(repeated);
                Assert.AreEqual(ShopOperationResult.Success, context.TryRefresh(BattleSide.Player));
                Assert.AreEqual(ShopOperationResult.StaleOffer,
                    context.TryPurchase(BattleSide.Player, 0, offerId, out repeated));
                Assert.AreEqual(9, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void PaidPurchaseDeductsOnceAndCancelOrInvalidDeployNeverRefunds()
        {
            using (var context = ShopTestData.Context())
            {
                Assert.AreEqual(ShopOperationResult.Success, context.TryRefresh(BattleSide.Player));
                var robot = ShopTestData.Purchase(context);
                Assert.AreEqual(3, context.GetState(BattleSide.Player).EnergyCoins, "刷新5，二星购买12。");
                Assert.IsTrue(context.TryBeginInteraction(BattleSide.Player, robot.InstanceId));
                Assert.IsTrue(robot.IsDragging);
                context.CancelInteraction();
                Assert.IsFalse(robot.IsDragging);
                Assert.AreEqual(RobotLocation.PurchasedShopSlot, robot.Location);
                Assert.IsTrue(context.TryBeginInteraction(BattleSide.Player, robot.InstanceId));
                Assert.AreEqual(ShopOperationResult.InvalidPlacement, context.TryDeploy(BattleSide.Player,
                    robot.InstanceId, BattleSide.Player, new BoardModel(1, 1), new BoardCoordinate(0, 0)));
                Assert.IsTrue(context.IsBusy);
                Assert.IsTrue(robot.IsDragging);
                context.CancelInteraction();
                Assert.IsFalse(context.IsBusy);
                Assert.IsFalse(robot.IsDragging);
                Assert.AreEqual(robot.InstanceId, context.GetState(BattleSide.Player).Slots[0].InstanceId);
                Assert.AreEqual(3, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void InsufficientFundsDoesNotCreateEntityOrReplaceOffer()
        {
            using (var context = ShopTestData.Context(quality: 6))
            {
                context.TryRefresh(BattleSide.Player);
                var offer = context.GetState(BattleSide.Player).Slots[0].Offer;
                Assert.AreEqual(40, offer.Price);
                RobotEntity robot;
                Assert.AreEqual(ShopOperationResult.InsufficientCoins,
                    context.TryPurchase(BattleSide.Player, 0, offer.OfferId, out robot));
                Assert.IsNull(robot);
                Assert.AreSame(offer, context.GetState(BattleSide.Player).Slots[0].Offer);
                Assert.AreEqual(15, context.GetState(BattleSide.Player).EnergyCoins);
                context.Rewards.TryGrantGlassBreak(context.BattleId, "funds", BattleSide.Player, 25);
                Assert.AreEqual(ShopOperationResult.Success,
                    context.TryPurchase(BattleSide.Player, 0, offer.OfferId, out robot));
                Assert.AreEqual(1, robot.InstanceId, "失败购买不分配实体ID。");
                Assert.AreEqual(0, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void SecondStarProbabilityUsesStrictPointZeroFiveBoundary()
        {
            var random = new ShopSequenceRandom();
            using (var context = ShopTestData.Context(random: random))
            {
                // 一个初抽和最多十个追加重抽，各三次；只有一个正权重候选。
                random.Enqueue(Enumerable.Repeat(0d, 33).Concat(new[] { 0.049999, 0.05, 0.999 }).ToArray());
                Assert.AreEqual(ShopOperationResult.Success, context.TryRefresh(BattleSide.Player));
                var slots = context.GetState(BattleSide.Player).Slots;
                CollectionAssert.AreEqual(new[] { 2, 1, 1 }, slots.Select(s => s.Offer.Level));
                CollectionAssert.AreEqual(new[] { 12, 6, 6 }, slots.Select(s => s.Offer.Price));
            }
        }

        [Test]
        public void InteractionBlocksOtherMutationsButAllowsItsOwnCommit()
        {
            using (var context = ShopTestData.Context())
            {
                var first = ShopTestData.Purchase(context);
                var second = ShopTestData.Purchase(context, 1);
                Assert.IsTrue(context.TryBeginInteraction(BattleSide.Player, first.InstanceId));
                Assert.IsFalse(context.TryBeginInteraction(BattleSide.Player, second.InstanceId));
                Assert.AreEqual(ShopOperationResult.Busy, context.TryRefresh(BattleSide.Player));
                Assert.AreEqual(ShopOperationResult.Busy, context.TryRefresh(BattleSide.Opponent));
                Assert.AreEqual(ShopOperationResult.Busy, context.TryStoreOrSwap(BattleSide.Player, second.InstanceId));
                RobotEntity robot;
                var offer = context.GetState(BattleSide.Player).Slots[2].Offer;
                Assert.AreEqual(ShopOperationResult.Busy, context.TryPurchase(BattleSide.Player, 2, offer.OfferId, out robot));
                Assert.AreEqual(ShopOperationResult.Success, context.TryDeploy(BattleSide.Player, first.InstanceId,
                    BattleSide.Player, new BoardModel(4, 2), new BoardCoordinate(0, 0)));
                Assert.IsFalse(context.IsBusy);
                Assert.IsFalse(first.IsDragging);
            }
        }

        [Test]
        public void WrongSideAndOccupiedOrClosedBoardNeverWritePartialFootprint()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = ShopTestData.Purchase(context);
                var board = new BoardModel(4, 2);
                Assert.AreEqual(ShopOperationResult.WrongSide, context.TryDeploy(BattleSide.Player,
                    robot.InstanceId, BattleSide.Opponent, board, new BoardCoordinate(0, 0)));
                Assert.IsFalse(board.IsOccupied(new BoardCoordinate(0, 0)));
                Assert.AreEqual(ShopOperationResult.WrongSide,
                    context.TryStoreOrSwap(BattleSide.Opponent, robot.InstanceId));
                board.SetOpen(new BoardCoordinate(1, 0), false);
                Assert.AreEqual(ShopOperationResult.InvalidPlacement, context.TryDeploy(BattleSide.Player,
                    robot.InstanceId, BattleSide.Player, board, new BoardCoordinate(0, 0)));
                Assert.IsFalse(board.IsOccupied(new BoardCoordinate(0, 0)));
                board.SetOpen(new BoardCoordinate(1, 0), true);
                board.TryPlace(new BoardCoordinate(1, 0), new PlaceableDefinition("other", 1, 1));
                Assert.AreEqual(ShopOperationResult.InvalidPlacement, context.TryDeploy(BattleSide.Player,
                    robot.InstanceId, BattleSide.Player, board, new BoardCoordinate(0, 0)));
                Assert.AreEqual("other", board.GetOccupant(new BoardCoordinate(1, 0)));
                Assert.IsFalse(board.IsOccupied(new BoardCoordinate(0, 0)));
                Assert.AreEqual(RobotLocation.PurchasedShopSlot, robot.Location);
            }
        }

        [Test]
        public void StorageDeployIsFreeAndBoardSourcesCannotDragMoveOrStore()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = ShopTestData.Purchase(context);
                Assert.AreEqual(ShopOperationResult.Success, context.TryStoreOrSwap(BattleSide.Player, robot.InstanceId));
                var board = new BoardModel(4, 2);
                board.TryPlace(new BoardCoordinate(3, 1), new PlaceableDefinition("other", 1, 1));
                Assert.AreEqual(ShopOperationResult.Success, context.TryDeploy(BattleSide.Player, robot.InstanceId,
                    BattleSide.Player, board, new BoardCoordinate(0, 0)));
                Assert.IsFalse(context.GetState(BattleSide.Player).Slots[0].IsPurchased);
                Assert.IsNull(context.GetState(BattleSide.Player).Slots[0].Offer);
                Assert.IsFalse(context.TryBeginInteraction(BattleSide.Player, robot.InstanceId));
                Assert.AreEqual(ShopOperationResult.InvalidLocation, context.TryDeploy(BattleSide.Player, robot.InstanceId,
                    BattleSide.Player, board, new BoardCoordinate(1, 0)));
                Assert.AreEqual(robot.InstanceId.ToString(), board.GetOccupant(new BoardCoordinate(0, 0)));
                Assert.IsNull(board.GetOccupant(new BoardCoordinate(2, 0)));
                Assert.AreEqual(new BoardCoordinate(0, 0), robot.Anchor);
                Assert.AreEqual(ShopOperationResult.InvalidLocation, context.TryStoreOrSwap(BattleSide.Player, robot.InstanceId));
                Assert.AreEqual("other", board.GetOccupant(new BoardCoordinate(3, 1)));
                Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(RobotLocation.Board, robot.Location);
            }
        }

        [Test]
        public void StorageSwapsWithPurchasedSlotAndReturningStorageToItselfSucceeds()
        {
            using (var context = ShopTestData.Context())
            {
                var first = ShopTestData.Purchase(context);
                var second = ShopTestData.Purchase(context, 1);
                var third = ShopTestData.Purchase(context, 2);
                Assert.AreEqual(ShopOperationResult.Success, context.TryStoreOrSwap(BattleSide.Player, first.InstanceId));
                Assert.AreEqual(ShopOperationResult.Success, context.TryStoreOrSwap(BattleSide.Player, second.InstanceId));
                Assert.AreEqual(second.InstanceId, context.GetState(BattleSide.Player).StorageInstanceId);
                Assert.AreEqual(first.InstanceId, context.GetState(BattleSide.Player).Slots[1].InstanceId);
                Assert.AreEqual(RobotLocation.PurchasedShopSlot, first.Location);
                Assert.AreEqual(RobotLocation.Storage, second.Location);
                Assert.IsTrue(context.TryBeginInteraction(BattleSide.Player, second.InstanceId));
                Assert.AreEqual(ShopOperationResult.Success, context.TryStoreOrSwap(BattleSide.Player, second.InstanceId));
                Assert.IsFalse(context.IsBusy);
                Assert.IsFalse(second.IsDragging);
                Assert.AreEqual(second.InstanceId, context.GetState(BattleSide.Player).StorageInstanceId);
                var board = new BoardModel(4, 2);
                context.TryDeploy(BattleSide.Player, third.InstanceId, BattleSide.Player, board, new BoardCoordinate(0, 0));
                Assert.AreEqual(ShopOperationResult.InvalidLocation, context.TryStoreOrSwap(BattleSide.Player, third.InstanceId));
                Assert.AreEqual(third.InstanceId.ToString(), board.GetOccupant(new BoardCoordinate(0, 0)));
                Assert.AreEqual(2, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void MergeConsumesSourceAndUpgradesTargetWithoutMovingItOrCharging()
        {
            using (var context = ShopTestData.Context())
            {
                var source = ShopTestData.Purchase(context);
                var target = ShopTestData.Purchase(context, 1);
                var board = new BoardModel(4, 2);
                context.TryDeploy(BattleSide.Player, target.InstanceId, BattleSide.Player, board, new BoardCoordinate(0, 0));
                context.TryStoreOrSwap(BattleSide.Player, source.InstanceId);
                Assert.IsTrue(context.TryBeginInteraction(BattleSide.Player, source.InstanceId));
                Assert.AreEqual(ShopOperationResult.Success, context.TryMerge(BattleSide.Player, source.InstanceId, target.InstanceId));
                Assert.AreEqual(2, target.Level);
                Assert.AreEqual(RobotLocation.Board, target.Location);
                Assert.IsNull(context.GetState(BattleSide.Player).StorageInstanceId);
                Assert.IsTrue(source.IsRemoved);
                Assert.AreEqual(EntityRemovalReason.Consumed, source.RemovalReason);
                Assert.AreEqual(target.InstanceId.ToString(), board.GetOccupant(new BoardCoordinate(0, 0)));
                Assert.AreEqual(new BoardCoordinate(0, 0), target.Anchor);
                Assert.AreEqual(8, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.IsFalse(context.IsBusy);
                Assert.AreEqual(ShopOperationResult.EntityNotFound, context.TryMerge(BattleSide.Player, source.InstanceId, target.InstanceId));
                Assert.AreEqual(2, target.Level);
            }
        }

        [Test]
        public void MergeRejectsDifferentConfigurationLevelOtherSideAndUnownedSource()
        {
            var random = new ShopSequenceRandom();
            random.Enqueue(0, 0.4, 0, 0, 0, 0);
            using (var context = ShopTestData.Context(ShopTestData.Rules(), random))
            {
                var first = ShopTestData.Purchase(context);
                var different = ShopTestData.Purchase(context, 1);
                var same = ShopTestData.Purchase(context, 2);
                var enemy = ShopTestData.Purchase(context, side: BattleSide.Opponent);
                var board = new BoardModel(4, 2);
                context.TryDeploy(BattleSide.Player, first.InstanceId, BattleSide.Player, board, new BoardCoordinate(0, 0));
                Assert.AreEqual(ShopOperationResult.IncompatibleMerge, context.TryMerge(BattleSide.Player, different.InstanceId, first.InstanceId));
                Assert.AreEqual(ShopOperationResult.InvalidLocation, context.TryMerge(BattleSide.Player, first.InstanceId, first.InstanceId));
                Assert.AreEqual(ShopOperationResult.WrongSide, context.TryMerge(BattleSide.Player, same.InstanceId, enemy.InstanceId));
                Assert.AreEqual(ShopOperationResult.Success, context.TryMerge(BattleSide.Player, same.InstanceId, first.InstanceId));
                var unowned = context.Registry.CreateRobotFromConfig(ShopTestData.Robot(1001), BattleSide.Player);
                Assert.AreEqual(ShopOperationResult.EntityNotFound, context.TryMerge(BattleSide.Player, unowned.InstanceId, first.InstanceId));
                Assert.AreEqual(2, first.Level);
                Assert.AreEqual(1, different.Level);
            }
        }

        [Test]
        public void RefreshDestroysOwnedSlotsWithoutRefundButPreservesBoardAndStorage()
        {
            using (var context = ShopTestData.Context())
            {
                var boardRobot = ShopTestData.Purchase(context);
                var storageRobot = ShopTestData.Purchase(context, 1);
                var slotRobot = ShopTestData.Purchase(context, 2);
                var board = new BoardModel(4, 2);
                context.TryDeploy(BattleSide.Player, boardRobot.InstanceId, BattleSide.Player, board, new BoardCoordinate(0, 0));
                context.TryStoreOrSwap(BattleSide.Player, storageRobot.InstanceId);
                context.Rewards.TryGrantGlassBreak(context.BattleId, "funds", BattleSide.Player, 20);
                context.TryRefresh(BattleSide.Player);
                Assert.IsTrue(slotRobot.IsRemoved);
                Assert.IsFalse(boardRobot.IsRemoved);
                Assert.IsFalse(storageRobot.IsRemoved);
                Assert.AreEqual(storageRobot.InstanceId, context.GetState(BattleSide.Player).StorageInstanceId);
                Assert.AreEqual(boardRobot.InstanceId.ToString(), board.GetOccupant(new BoardCoordinate(0, 0)));
                Assert.AreEqual(17, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(20, context.GetState(BattleSide.Opponent).EnergyCoins);
                var paid = ShopTestData.Purchase(context);
                context.TryRefresh(BattleSide.Player);
                Assert.IsTrue(paid.IsRemoved);
                Assert.AreEqual(0, context.GetState(BattleSide.Player).EnergyCoins, "首批18+两次刷新10+后续购买12，总计40，不退款。");
            }
        }

        [Test]
        public void RefreshWithInsufficientCoinsPreservesAllSlotsAndDoesNotDraw()
        {
            var random = new ShopSequenceRandom();
            using (var context = ShopTestData.Context(random: random))
            {
                context.TryRefresh(BattleSide.Player);
                ShopTestData.Purchase(context);
                var before = context.GetState(BattleSide.Player).Slots[1].Offer;
                var draws = random.DoubleCalls;
                Assert.AreEqual(ShopOperationResult.InsufficientCoins, context.TryRefresh(BattleSide.Player));
                Assert.AreSame(before, context.GetState(BattleSide.Player).Slots[1].Offer);
                Assert.AreEqual(draws, random.DoubleCalls);
                Assert.AreEqual(3, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void RepeatedMultisetRetriesTenTimesThenChangesRandomSlotToUnseenPositiveCandidate()
        {
            var random = new ShopSequenceRandom { SlotIndex = 2 };
            using (var context = ShopTestData.Context(ShopTestData.Rules(), random))
            {
                var before = random.DoubleCalls;
                context.TryRefresh(BattleSide.Player);
                Assert.AreEqual(37, random.DoubleCalls - before, "初抽3+追加重抽30+兜底权重抽1+星级3。");
                Assert.AreEqual(1, random.IntCalls);
                CollectionAssert.AreEqual(new[] { 1001, 1001, 1002 }, context.GetState(BattleSide.Player).LastBatchRobotIds);
            }
        }

        [Test]
        public void ReorderedSameMultisetAlsoTriggersProtectionButNoUnseenCandidateAcceptsDuplicate()
        {
            var random = new ShopSequenceRandom();
            random.Enqueue(0, 0.4, 0.8, 0, 0.4, 0.8);
            using (var context = ShopTestData.Context(ShopTestData.Rules(), random))
            {
                random.Enqueue(Enumerable.Range(0, 11).SelectMany(_ => new[] { 0.8, 0d, 0.4 }).ToArray());
                var before = random.DoubleCalls;
                context.TryRefresh(BattleSide.Player);
                Assert.AreEqual(36, random.DoubleCalls - before);
                CollectionAssert.AreEqual(new[] { 1003, 1001, 1002 }, context.GetState(BattleSide.Player).LastBatchRobotIds);
            }
        }

        [Test]
        public void OnePositiveCandidateTerminatesRetryAndAcceptsDuplicate()
        {
            var random = new ShopSequenceRandom();
            using (var context = ShopTestData.Context(random: random))
            {
                var before = random.DoubleCalls;
                context.TryRefresh(BattleSide.Player);
                Assert.AreEqual(36, random.DoubleCalls - before);
                CollectionAssert.AreEqual(new[] { 1001, 1001, 1001 }, context.GetState(BattleSide.Player).LastBatchRobotIds);
                Assert.AreEqual(1, random.IntCalls);
            }
        }

        [Test]
        public void RewardsDeduplicateAcrossRecipientsByBattleKindAndSourceAndCreditExplicitRecipient()
        {
            using (var context = ShopTestData.Context())
            {
                Assert.AreEqual(ShopOperationResult.Success,
                    context.Rewards.TryGrantEnemyKill(context.BattleId, 7, BattleSide.Opponent, 4));
                Assert.AreEqual(ShopOperationResult.DuplicateReward,
                    context.Rewards.TryGrantEnemyKill(context.BattleId, 7, BattleSide.Player, 4));
                Assert.AreEqual(ShopOperationResult.Success,
                    context.Rewards.TryGrantGlassBreak(context.BattleId, "7", BattleSide.Player, 3));
                Assert.AreEqual(ShopOperationResult.DuplicateReward,
                    context.Rewards.TryGrantGlassBreak(context.BattleId, "7", BattleSide.Opponent, 3));
                Assert.AreEqual(23, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(24, context.GetState(BattleSide.Opponent).EnergyCoins);
                Assert.AreEqual(ShopOperationResult.WrongBattle,
                    context.Rewards.TryGrantEnemyKill(context.BattleId + 1, 8, BattleSide.Player, 50));
                Assert.AreEqual(ShopOperationResult.InvalidArgument,
                    context.Rewards.TryGrantGlassBreak(context.BattleId, "", BattleSide.Player, 3));
                Assert.AreEqual(ShopOperationResult.InvalidArgument,
                    context.Rewards.TryGrantEnemyKill(context.BattleId, 8, BattleSide.Player, 0));
                Assert.AreEqual(ShopOperationResult.Success,
                    context.Rewards.TryGrantEnemyKill(context.BattleId, 8, BattleSide.Player, 1));
            }
        }

        [Test]
        public void BusyAndOverflowRewardsCanBeRetriedWithoutBurningDeduplicationKey()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = ShopTestData.Purchase(context);
                var duringNotification = ShopOperationResult.Success;
                Action<int> listener = side => duringNotification =
                    context.Rewards.TryGrantEnemyKill(context.BattleId, 1, BattleSide.Player, 3);
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, listener);
                try { context.TryBeginInteraction(BattleSide.Player, robot.InstanceId); }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, listener); }
                Assert.AreEqual(ShopOperationResult.Busy, duringNotification);
                context.CancelInteraction();
                Assert.AreEqual(ShopOperationResult.Success,
                    context.Rewards.TryGrantEnemyKill(context.BattleId, 1, BattleSide.Player, 3));
                Assert.AreEqual(ShopOperationResult.CoinOverflow,
                    context.Rewards.TryGrantEnemyKill(context.BattleId, 2, BattleSide.Player, int.MaxValue));
                Assert.AreEqual(ShopOperationResult.Success,
                    context.Rewards.TryGrantEnemyKill(context.BattleId, 2, BattleSide.Player, 1));
                Assert.AreEqual(18, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void EndBattleAndDisposeAreIdempotentClearOwnedDataAndStopAllRewards()
        {
            var context = ShopTestData.Context();
            var boardRobot = ShopTestData.Purchase(context);
            var storageRobot = ShopTestData.Purchase(context, 1);
            var slotRobot = ShopTestData.Purchase(context, 2);
            var board = new BoardModel(4, 2);
            context.TryDeploy(BattleSide.Player, boardRobot.InstanceId, BattleSide.Player, board, new BoardCoordinate(0, 0));
            context.TryStoreOrSwap(BattleSide.Player, storageRobot.InstanceId);
            context.TryBeginInteraction(BattleSide.Player, slotRobot.InstanceId);
            context.EndBattle();
            context.EndBattle();
            context.Dispose();
            Assert.IsTrue(context.IsEnded);
            Assert.IsFalse(context.IsBusy);
            Assert.IsFalse(slotRobot.IsDragging);
            Assert.IsTrue(boardRobot.IsRemoved);
            Assert.IsTrue(storageRobot.IsRemoved);
            Assert.IsTrue(slotRobot.IsRemoved);
            Assert.IsNull(board.GetOccupant(new BoardCoordinate(0, 0)));
            foreach (var state in new[] { context.GetState(BattleSide.Player), context.GetState(BattleSide.Opponent) })
            {
                Assert.IsNull(state.StorageInstanceId);
                Assert.IsTrue(state.Slots.All(s => s.Offer == null && !s.InstanceId.HasValue));
                Assert.AreEqual(state.Side == BattleSide.Player ? 2 : 20, state.EnergyCoins);
            }
            Assert.AreEqual(ShopOperationResult.BattleEnded, context.TryRefresh(BattleSide.Player));
            Assert.AreEqual(ShopOperationResult.BattleEnded, context.Rewards.TryGrantEnemyKill(context.BattleId, 1, BattleSide.Player, 99));
            Assert.AreEqual(ShopOperationResult.BattleEnded, context.Rewards.TryGrantGlassBreak(context.BattleId, "late", BattleSide.Opponent, 99));
            Assert.IsFalse(context.TryBeginInteraction(BattleSide.Player, slotRobot.InstanceId));
        }

        [Test]
        public void ChangedEventCarriesIntSideAfterCommittedStateAndRejectsReentrantMutation()
        {
            using (var context = ShopTestData.Context())
            {
                var sides = new List<int>();
                var sawPurchased = false;
                var reentrantResult = ShopOperationResult.Success;
                Action<int> listener = side =>
                {
                    sides.Add(side);
                    sawPurchased = context.GetState((BattleSide)side).Slots[0].IsPurchased;
                    reentrantResult = context.TryRefresh((BattleSide)side);
                };
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, listener);
                try
                {
                    ShopTestData.Purchase(context, side: BattleSide.Opponent);
                    CollectionAssert.AreEqual(new[] { (int)BattleSide.Opponent }, sides);
                    Assert.IsTrue(sawPurchased);
                    Assert.AreEqual(ShopOperationResult.Busy, reentrantResult);
                }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, listener); }
            }
        }

        [Test]
        public void InvalidCandidatesAndWeightsFailBeforeCreatingUsableContext()
        {
            Assert.Throws<ArgumentException>(() => ShopTestData.Context(ShopTestData.Rules(0, 0, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => ShopTestData.Rules(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ShopTestData.Rules(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => ShopTestData.Rules(double.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => new BattleShopContext(ShopTestData.Rules(),
                new[] { 1001, 1001, 1003 }, ShopTestData.Candidates, id => ShopTestData.Robot(id)));
            Assert.Throws<ArgumentException>(() => new BattleShopContext(ShopTestData.Rules(),
                new[] { 1001, 1002 }, ShopTestData.Candidates, id => ShopTestData.Robot(id)));
        }

        [Test]
        public void CandidatesAndWeightsAreCopiedSoCallerCannotChangeFutureDraws()
        {
            var weights = new Dictionary<int, double> { { 1001, 1 }, { 1002, 0 }, { 1003, 0 } };
            var ids = new[] { 1001, 1002, 1003 };
            using (var context = new BattleShopContext(new ShopRules(weights), ids, ids, id => ShopTestData.Robot(id), new ShopSequenceRandom()))
            {
                weights[1001] = 0;
                weights[1002] = 100;
                ids[0] = 9999;
                context.TryRefresh(BattleSide.Player);
                CollectionAssert.AreEqual(new[] { 1001, 1001, 1001 }, context.GetState(BattleSide.Player).LastBatchRobotIds);
            }
        }
    }
}
