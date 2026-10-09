using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TEngine;

namespace GameLogic.Tests
{
    public class ShopTransactionTests
    {
        [Test]
        public void RewardsDuringRobotDragAreNotLostOrUnlockTheGesture()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = ShopTestData.Purchase(context);
                Assert.IsTrue(context.TryBeginInteraction(BattleSide.Player, robot.InstanceId));
                Assert.AreEqual(ShopOperationResult.Success,
                    context.Rewards.TryGrant(context.BattleId, BattleSide.Player, ShopRewardSource.NormalEnemy, 80001));
                Assert.AreEqual(18, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.IsTrue(context.IsBusy);
                Assert.IsTrue(robot.IsDragging);
            }
        }

        [Test]
        public void AdapterRetriesReentrantFactsOnceAndClearsPendingRewardsOnDispose()
        {
            using (var context = ShopTestData.Context())
            using (var adapter = new BattleRewardAdapter(context))
            {
                var fact = new BattleRewardEvent(context.BattleId, BattleSide.Player, ShopRewardSource.NormalEnemy, 80002);
                var sent = false;
                Action<int> sendInTransaction = side =>
                {
                    if (sent) return;
                    sent = true;
                    GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, fact);
                    GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, fact);
                };
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, sendInTransaction);
                try { ShopTestData.Purchase(context); }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, sendInTransaction); }
                Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
                adapter.Advance();
                adapter.Advance();
                Assert.AreEqual(18, context.GetState(BattleSide.Player).EnergyCoins);
                sent = false;
                fact = new BattleRewardEvent(context.BattleId, BattleSide.Player, ShopRewardSource.NormalEnemy, 80003);
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, sendInTransaction);
                try { ShopTestData.Purchase(context, 1); }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, sendInTransaction); }
                adapter.Dispose();
                adapter.Advance();
                Assert.AreEqual(12, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void MergeDifferentLevelsOrNonBoardTargetLeavesSourceAndCoinsUntouched()
        {
            using (var context = ShopTestData.Context())
            {
                var target = ShopTestData.Purchase(context);
                var first = ShopTestData.Purchase(context, 1);
                var second = ShopTestData.Purchase(context, 2);
                var board = new BoardModel(4, 2);
                Assert.AreEqual(ShopOperationResult.InvalidLocation,
                    context.TryMerge(BattleSide.Player, first.InstanceId, target.InstanceId));
                context.TryStoreOrSwap(BattleSide.Player, target.InstanceId);
                Assert.AreEqual(ShopOperationResult.InvalidLocation,
                    context.TryMerge(BattleSide.Player, first.InstanceId, target.InstanceId));
                context.TryDeploy(BattleSide.Player, target.InstanceId, BattleSide.Player, board, new BoardCoordinate(0, 0));
                Assert.AreEqual(ShopOperationResult.Success,
                    context.TryMerge(BattleSide.Player, first.InstanceId, target.InstanceId));
                Assert.IsTrue(context.TryBeginInteraction(BattleSide.Player, second.InstanceId));
                Assert.AreEqual(ShopOperationResult.IncompatibleMerge,
                    context.TryMerge(BattleSide.Player, second.InstanceId, target.InstanceId));
                Assert.IsTrue(context.IsBusy);
                Assert.IsTrue(second.IsDragging);
                Assert.AreEqual(second.InstanceId, context.GetState(BattleSide.Player).Slots[2].InstanceId);
                Assert.AreEqual(2, target.Level);
                Assert.AreEqual(1, second.Level);
                Assert.AreEqual(2, context.GetState(BattleSide.Player).EnergyCoins);
                context.CancelInteraction();
                Assert.IsFalse(context.IsBusy);
            }
        }

        [Test]
        public void MergeLevelFiveRejectsAndPreservesBothRealEntities()
        {
            using (var context = ShopTestData.Context())
            {
                var target = ShopTestData.Purchase(context);
                var source = ShopTestData.Purchase(context, 1);
                var board = new BoardModel(4, 2);
                context.TryDeploy(BattleSide.Player, target.InstanceId, BattleSide.Player, board, new BoardCoordinate(0, 0));
                // 用现有真实实体升级方法布置Lv5边界，不向生产类添加测试入口。
                var upgrade = typeof(RobotEntity).GetMethod("TryUpgrade", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(upgrade);
                for (var level = 1; level < 5; level++)
                {
                    Assert.IsTrue((bool)upgrade.Invoke(target, null));
                    Assert.IsTrue((bool)upgrade.Invoke(source, null));
                }
                Assert.AreEqual(ShopOperationResult.MaxLevelReached,
                    context.TryMerge(BattleSide.Player, source.InstanceId, target.InstanceId));
                Assert.AreEqual(5, target.Level);
                Assert.AreEqual(5, source.Level);
                Assert.IsFalse(source.IsRemoved);
                Assert.AreEqual(8, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(target.InstanceId.ToString(), board.GetOccupant(new BoardCoordinate(0, 0)));
            }
        }

        [Test]
        public void InvalidRandomRefreshPreservesPaidSlotOffersBalanceAndOfferSequence()
        {
            var random = new ShopSequenceRandom();
            using (var context = ShopTestData.Context(random: random))
            {
                var robot = ShopTestData.Purchase(context);
                var offer = context.GetState(BattleSide.Player).Slots[1].Offer;
                var lastBatch = context.GetState(BattleSide.Player).LastBatchRobotIds;
                random.DefaultValue = double.NaN;
                Assert.AreEqual(ShopOperationResult.InvalidConfiguration, context.TryRefresh(BattleSide.Player));
                Assert.IsFalse(robot.IsRemoved);
                Assert.AreSame(offer, context.GetState(BattleSide.Player).Slots[1].Offer);
                Assert.AreSame(lastBatch, context.GetState(BattleSide.Player).LastBatchRobotIds);
                Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
                random.DefaultValue = 0.99;
                Assert.AreEqual(ShopOperationResult.Success, context.TryRefresh(BattleSide.Player));
                Assert.AreEqual(7L, context.GetState(BattleSide.Player).Slots[0].Offer.OfferId);
                Assert.AreEqual(9, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void InvalidConfigPurchaseDoesNotDeductOrAllocateAnEntity()
        {
            var config = ShopTestData.Robot(1001);
            var shape = config.ShapeId_Ref;
            using (var context = new BattleShopContext(ShopTestData.Rules(1, 0, 0), ShopTestData.Candidates,
                ShopTestData.Candidates, id => id == 1001 ? config : ShopTestData.Robot(id), new ShopSequenceRandom()))
            {
                var offer = context.GetState(BattleSide.Player).Slots[0].Offer;
                config.ShapeId_Ref = null;
                RobotEntity robot;
                Assert.AreEqual(ShopOperationResult.InvalidConfiguration,
                    context.TryPurchase(BattleSide.Player, 0, offer.OfferId, out robot));
                Assert.IsNull(robot);
                Assert.AreEqual(20, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreSame(offer, context.GetState(BattleSide.Player).Slots[0].Offer);
                config.ShapeId_Ref = shape;
                Assert.AreEqual(ShopOperationResult.Success,
                    context.TryPurchase(BattleSide.Player, 0, offer.OfferId, out robot));
                Assert.AreEqual(1, robot.InstanceId);
                Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void NewBattleHasIncreasingLongIdFreshStateAndIndependentRewardLedger()
        {
            using (var first = ShopTestData.Context())
            using (var second = ShopTestData.Context())
            {
                Assert.Greater(second.BattleId, first.BattleId);
                Assert.AreEqual(ShopOperationResult.Success,
                    first.Rewards.TryGrant(first.BattleId, BattleSide.Player, ShopRewardSource.Boss, 991));
                first.EndBattle();
                Assert.AreEqual(20, second.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(ShopOperationResult.Success,
                    second.Rewards.TryGrant(second.BattleId, BattleSide.Player, ShopRewardSource.Boss, 991));
                Assert.AreEqual(70, second.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(typeof(long), typeof(BattleShopContext).GetProperty("BattleId").PropertyType);
                Assert.AreEqual(typeof(long), typeof(ShopOffer).GetProperty("OfferId").PropertyType);
                Assert.AreNotEqual(ShopOperationResult.InvalidConfiguration, ShopOperationResult.ResourceFailed);
            }
        }

        [Test]
        public void WrongSideStorageDeployRetainsStorageAndFailedInteractionUntilCallerCancels()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = ShopTestData.Purchase(context);
                context.TryStoreOrSwap(BattleSide.Player, robot.InstanceId);
                context.TryBeginInteraction(BattleSide.Player, robot.InstanceId);
                var board = new BoardModel(4, 2);
                Assert.AreEqual(ShopOperationResult.WrongSide, context.TryDeploy(BattleSide.Player,
                    robot.InstanceId, BattleSide.Opponent, board, new BoardCoordinate(0, 0)));
                Assert.AreEqual(robot.InstanceId, context.GetState(BattleSide.Player).StorageInstanceId);
                Assert.IsTrue(context.IsBusy);
                Assert.IsFalse(board.IsOccupied(new BoardCoordinate(0, 0)));
                context.CancelInteraction();
                Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.IsFalse(robot.IsDragging);
            }
        }

        [Test]
        public void ABoardBoundToOneSideCannotBeReusedByOtherSideEvenWithForgedBoardSide()
        {
            using (var context = ShopTestData.Context())
            {
                var player = ShopTestData.Purchase(context);
                var opponent = ShopTestData.Purchase(context, side: BattleSide.Opponent);
                var board = new BoardModel(4, 2);
                context.TryDeploy(BattleSide.Player, player.InstanceId, BattleSide.Player, board, new BoardCoordinate(0, 0));
                Assert.AreEqual(ShopOperationResult.WrongSide, context.TryDeploy(BattleSide.Opponent,
                    opponent.InstanceId, BattleSide.Opponent, board, new BoardCoordinate(0, 1)));
                Assert.IsFalse(board.IsOccupied(new BoardCoordinate(0, 1)));
                Assert.AreEqual(RobotLocation.PurchasedShopSlot, opponent.Location);
            }
        }

        [Test]
        public void EndRequestedDuringChangedCallbackCleansUpAfterCurrentCommit()
        {
            using (var context = ShopTestData.Context())
            {
                Action<int> listener = side => context.EndBattle();
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, listener);
                try
                {
                    var robot = ShopTestData.Purchase(context);
                    Assert.IsTrue(context.IsEnded);
                    Assert.IsTrue(robot.IsRemoved);
                    Assert.IsTrue(context.GetState(BattleSide.Player).Slots.All(s => s.Offer == null && s.InstanceId == null));
                    Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
                }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, listener); }
            }
        }
    }
}
