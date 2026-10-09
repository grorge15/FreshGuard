using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameConfig.robot;
using NUnit.Framework;
using TEngine;

namespace GameLogic.Tests
{
    public class RobotDragTransactionTests
    {
        private static RobotDragSource Offer(BattleShopContext context, int slot = 1,
            BattleSide side = BattleSide.Player) => RobotDragSource.Offer(slot, context.GetState(side).Slots[slot].Offer.OfferId);

        private static RobotDragSession Begin(BattleShopContext context, RobotDragSource source,
            BattleSide side = BattleSide.Player)
        {
            RobotDragSession session;
            Assert.AreEqual(ShopOperationResult.Success, context.TryBeginDrag(side, source, out session));
            Assert.AreSame(session, context.ActiveInteraction);
            Assert.IsTrue(context.IsCurrentInteraction(session.InteractionId));
            return session;
        }

        private static void Deploy(BattleShopContext context, RobotEntity robot, BoardModel board, int column = 0)
        {
            Assert.AreEqual(ShopOperationResult.Success, context.TryDeploy(robot.Side, robot.InstanceId,
                robot.Side, board, new BoardCoordinate(column, 0)));
        }

        private static void Upgrade(RobotEntity robot)
        {
            var method = typeof(RobotEntity).GetMethod("TryUpgrade", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsTrue((bool)method.Invoke(robot, null));
        }

        private static void Protect(RobotEntity robot, float seconds)
        {
            var method = typeof(RobotEntity).GetMethod("TryConsumeAttackTrigger", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsTrue((bool)method.Invoke(robot, new object[] { seconds }));
        }

        private static void SetCoinsForCommitRevalidation(BattleShopContext context, int coins)
        {
            // 模拟外部余额事实在拿起后变化，验证提交仍重新校验；不为测试添加生产入口。
            typeof(BattleShopState).GetProperty("EnergyCoins").SetValue(context.GetState(BattleSide.Player), coins);
        }

        [Test]
        public void CancelDuringRewardNotificationRestoresLiftedBoardSourceAfterTransaction()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = ShopTestData.Purchase(context);
                var board = new BoardModel(4, 2);
                Deploy(context, robot, board);
                var session = Begin(context, RobotDragSource.Owned(robot.InstanceId));
                var notified = false;
                Action<int> cancel = side =>
                {
                    if (notified) return;
                    notified = true;
                    context.CancelInteraction(session.InteractionId);
                };
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, cancel);
                try
                {
                    Assert.AreEqual(ShopOperationResult.Success,
                        context.Rewards.TryGrantGlassBreak(context.BattleId, "cancel-in-reward", BattleSide.Player, 1));
                    Assert.IsFalse(context.IsBusy);
                    Assert.IsNull(context.ActiveInteraction);
                    Assert.IsFalse(robot.IsDragging);
                    Assert.AreEqual(robot.Definition.Id, board.GetOccupant(new BoardCoordinate(0, 0)));
                    var replacement = Begin(context, RobotDragSource.Owned(robot.InstanceId));
                    context.CancelInteraction(session.InteractionId);
                    Assert.AreSame(replacement, context.ActiveInteraction);
                    context.CancelInteraction(replacement.InteractionId);
                }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, cancel); }
            }
        }

        [Test]
        public void OfferBeginDragRechecksFundsAfterPreviewAndRejectsWithoutLockOrPurchase()
        {
            using (var context = ShopTestData.Context(quality: 6))
            {
                var source = Offer(context);
                var offer = context.GetState(BattleSide.Player).Slots[1].Offer;
                Robot config;
                int level;
                PlaceableDefinition definition;
                Assert.AreEqual(ShopOperationResult.Success, context.GetDragContent(BattleSide.Player, source,
                    out config, out level, out definition));
                Assert.AreEqual(20, context.GetState(BattleSide.Player).EnergyCoins);
                var owned = ShopTestData.Purchase(context);
                RobotDragSession session;
                Assert.AreEqual(ShopOperationResult.InsufficientCoins, context.TryBeginDrag(BattleSide.Player, source, out session));
                Assert.IsNull(session);
                Assert.IsNull(context.ActiveInteraction);
                Assert.IsFalse(context.IsBusy);
                Assert.AreSame(offer, context.GetState(BattleSide.Player).Slots[1].Offer);
                Assert.IsNull(context.GetState(BattleSide.Player).Slots[1].InstanceId);
                Assert.AreEqual(0, context.GetState(BattleSide.Player).EnergyCoins);
                session = Begin(context, RobotDragSource.Owned(owned.InstanceId));
                context.CancelInteraction(session.InteractionId);
                Assert.AreEqual(ShopOperationResult.Success,
                    context.Rewards.TryGrantGlassBreak(context.BattleId, "begin-funds", BattleSide.Player, 20));
                session = Begin(context, source);
                Assert.AreEqual(20, context.GetState(BattleSide.Player).EnergyCoins);
                context.CancelInteraction(session.InteractionId);
                Assert.AreEqual(2, ShopTestData.Purchase(context, 2).InstanceId,
                    "失败拿起和取消未购商品都不分配实体ID。");
            }
        }

        [Test]
        public void OfferPreviewReusesDefinitionAcrossCandidateChecksAndRefreshInvalidatesOldOffer()
        {
            using (var context = ShopTestData.Context())
            {
                var target = ShopTestData.Purchase(context);
                Deploy(context, target, new BoardModel(4, 2));
                var source = Offer(context);
                Robot config;
                int level;
                PlaceableDefinition first;
                PlaceableDefinition repeated;
                Assert.AreEqual(ShopOperationResult.Success, context.GetDragContent(BattleSide.Player, source,
                    out config, out level, out first));
                for (var index = 0; index < 10; index++)
                {
                    Assert.AreEqual(ShopOperationResult.Success, context.CheckMerge(BattleSide.Player, source, target.InstanceId));
                    Assert.AreEqual(ShopOperationResult.Success, context.GetDragContent(BattleSide.Player, source,
                        out config, out level, out repeated));
                    Assert.AreSame(first, repeated, "每帧预览和候选检查应复用Offer定义。");
                }
                Assert.AreEqual(ShopOperationResult.Success, context.TryRefresh(BattleSide.Player));
                Assert.AreEqual(ShopOperationResult.StaleOffer, context.GetDragContent(BattleSide.Player, source,
                    out config, out level, out repeated));
                Assert.AreEqual(ShopOperationResult.Success, context.GetDragContent(BattleSide.Player, Offer(context),
                    out config, out level, out repeated));
                Assert.AreNotSame(first, repeated);
                Assert.AreEqual(9, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(1, target.Level);
            }
        }

        [TestCase("Board")]
        [TestCase("PurchasedShopSlot")]
        [TestCase("Storage")]
        [TestCase("ShopOffer")]
        public void FourSourcesMergePreservesTargetIdentityAnchorQualityAndNotifiesExactlyOnce(string kind)
        {
            using (var context = ShopTestData.Context())
            {
                var target = ShopTestData.Purchase(context);
                var board = new BoardModel(8, 2);
                Deploy(context, target, board);
                RobotActionToken targetToken;
                Assert.IsTrue(context.TryCreateActionToken(target.InstanceId, out targetToken));
                Protect(target, 3f);
                RobotEntity sourceRobot = null;
                RobotActionToken sourceToken = default(RobotActionToken);
                RobotDragSource source;
                if (kind == "ShopOffer") source = Offer(context);
                else
                {
                    sourceRobot = ShopTestData.Purchase(context, 1);
                    if (kind == "Board")
                    {
                        Deploy(context, sourceRobot, board, 3);
                        Assert.IsTrue(context.TryCreateActionToken(sourceRobot.InstanceId, out sourceToken));
                    }
                    if (kind == "Storage") Assert.AreEqual(ShopOperationResult.Success,
                        context.TryStoreOrSwap(BattleSide.Player, sourceRobot.InstanceId));
                    source = RobotDragSource.Owned(sourceRobot.InstanceId);
                }
                var session = Begin(context, source);
                Assert.AreEqual(ShopOperationResult.Success, context.CheckMerge(BattleSide.Player, source, target.InstanceId));
                var snapshots = new List<RobotMergeSnapshot>();
                var changed = 0;
                Action<RobotMergeSnapshot> merged = snapshot =>
                {
                    snapshots.Add(snapshot);
                    Assert.AreEqual(2, target.Level);
                    Assert.IsFalse(context.IsBusy);
                    Assert.AreEqual(8, context.GetState(BattleSide.Player).EnergyCoins);
                    Assert.IsFalse(context.IsActionTokenValid(targetToken));
                    Assert.AreEqual(ShopOperationResult.Busy, context.TryRefresh(BattleSide.Player));
                };
                Action<int> change = side => { Assert.AreEqual((int)BattleSide.Player, side); changed++; };
                GameEvent.AddEventListener<RobotMergeSnapshot>(BattleShopEvents.Merged, merged);
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, change);
                try
                {
                    Assert.AreEqual(ShopOperationResult.Success, context.TryCommitMerge(session.InteractionId, target.InstanceId));
                    Assert.AreEqual(ShopOperationResult.InvalidSource, context.TryCommitMerge(session.InteractionId, target.InstanceId));
                }
                finally
                {
                    GameEvent.RemoveEventListener<RobotMergeSnapshot>(BattleShopEvents.Merged, merged);
                    GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, change);
                }
                Assert.AreEqual(1, snapshots.Count);
                Assert.AreEqual(1, changed);
                var fact = snapshots[0];
                Assert.AreEqual(context.BattleId, fact.BattleId);
                Assert.AreEqual(session.InteractionId, fact.InteractionId);
                Assert.AreEqual(BattleSide.Player, fact.Side);
                Assert.AreEqual(source, fact.Source);
                Assert.AreEqual(target.InstanceId, fact.TargetInstanceId);
                Assert.AreEqual(1, fact.PreviousLevel);
                Assert.AreEqual(2, fact.Level);
                Entity registered;
                Assert.IsTrue(context.Registry.TryGet(target.InstanceId, out registered));
                Assert.AreSame(target, registered);
                Assert.AreEqual(2, target.QualityId);
                Assert.AreEqual(new BoardCoordinate(0, 0), target.Anchor);
                Assert.AreEqual(RobotLocation.Board, target.Location);
                Assert.AreEqual(target.Definition.Id, board.GetOccupant(new BoardCoordinate(0, 0)));
                Assert.AreEqual(target.Definition.Id, board.GetOccupant(new BoardCoordinate(1, 0)));
                Assert.AreEqual(0f, target.TriggerCooldownRemaining);
                Assert.IsNull(context.GetState(BattleSide.Player).StorageInstanceId);
                Assert.IsNull(context.GetState(BattleSide.Player).Slots[1].Offer);
                Assert.IsNull(context.GetState(BattleSide.Player).Slots[1].InstanceId);
                Assert.IsNull(board.GetOccupant(new BoardCoordinate(3, 0)));
                if (sourceRobot != null)
                {
                    Assert.IsTrue(sourceRobot.IsRemoved);
                    Assert.AreEqual(EntityRemovalReason.Consumed, sourceRobot.RemovalReason);
                    if (kind == "Board") Assert.IsFalse(context.IsActionTokenValid(sourceToken));
                }
                RobotActionToken upgraded;
                Assert.IsTrue(context.TryCreateActionToken(target.InstanceId, out upgraded));
                Assert.AreEqual(2, upgraded.Level);
                Assert.Greater(upgraded.ActionVersion, targetToken.ActionVersion);
                var next = ShopTestData.Purchase(context, 2);
                Assert.AreEqual(kind == "ShopOffer" ? 2 : 3, next.InstanceId,
                    "未购商品直接合成不应分配来源实体ID。");
            }
        }

        [Test]
        public void OfferPreviewAndCanceledOrFailedDeploymentNeverPurchaseOrAllocate()
        {
            using (var context = ShopTestData.Context())
            {
                var source = Offer(context, 0);
                Robot config;
                int level;
                PlaceableDefinition definition;
                Assert.AreEqual(ShopOperationResult.Success, context.GetDragContent(BattleSide.Player, source,
                    out config, out level, out definition));
                Assert.AreEqual(1001, config.Id);
                Assert.AreEqual(1, level);
                CollectionAssert.AreEqual(new[] { new BoardCoordinate(0, 0), new BoardCoordinate(1, 0) }, definition.Cells);
                var before = context.GetState(BattleSide.Player).Slots[0].Offer;
                var session = Begin(context, source);
                RobotEntity robot;
                Assert.AreEqual(ShopOperationResult.InvalidPlacement, context.TryCommitDeploy(session.InteractionId,
                    BattleSide.Player, new BoardModel(1, 1), new BoardCoordinate(0, 0), out robot));
                Assert.IsNull(robot);
                Assert.IsTrue(context.IsBusy);
                Assert.AreEqual(20, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreSame(before, context.GetState(BattleSide.Player).Slots[0].Offer);
                context.CancelInteraction(session.InteractionId);
                Assert.IsFalse(context.IsBusy);
                robot = ShopTestData.Purchase(context, 2);
                Assert.AreEqual(1, robot.InstanceId);
                Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void OfferDeploymentPurchasesOnlyOnSuccessAndCannotSubmitTwice()
        {
            using (var context = ShopTestData.Context())
            {
                var session = Begin(context, Offer(context, 0));
                var board = new BoardModel(4, 2);
                RobotEntity robot;
                Assert.AreEqual(ShopOperationResult.WrongSide, context.TryCommitDeploy(session.InteractionId,
                    BattleSide.Opponent, board, new BoardCoordinate(0, 0), out robot));
                Assert.IsNull(robot);
                Assert.AreEqual(ShopOperationResult.Success, context.TryCommitDeploy(session.InteractionId,
                    BattleSide.Player, board, new BoardCoordinate(1, 0), out robot));
                Assert.AreEqual(1, robot.InstanceId);
                Assert.AreEqual(RobotLocation.Board, robot.Location);
                Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.IsNull(context.GetState(BattleSide.Player).Slots[0].Offer);
                Assert.IsNull(context.GetState(BattleSide.Player).Slots[0].InstanceId);
                var deployed = robot;
                Assert.AreEqual(ShopOperationResult.InvalidSource, context.TryCommitDeploy(session.InteractionId,
                    BattleSide.Player, board, new BoardCoordinate(0, 0), out robot));
                Assert.IsNull(robot);
                Assert.AreEqual(deployed.Definition.Id, board.GetOccupant(new BoardCoordinate(1, 0)));
                Assert.AreEqual(deployed.Definition.Id, board.GetOccupant(new BoardCoordinate(2, 0)));
                Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void OverlappingBoardMoveKeepsNewFootprintAndCancelOfOldSessionCannotAffectNewDrag()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = ShopTestData.Purchase(context);
                var board = new BoardModel(5, 2);
                Deploy(context, robot, board);
                RobotActionToken token;
                Assert.IsTrue(context.TryCreateActionToken(robot.InstanceId, out token));
                var first = Begin(context, RobotDragSource.Owned(robot.InstanceId));
                Assert.AreEqual(RobotLocation.Board, first.OriginalLocation);
                Assert.AreSame(board, first.OriginalBoard);
                Assert.AreEqual(new BoardCoordinate(0, 0), first.OriginalAnchor);
                Assert.IsTrue(context.IsActionTokenValid(token), "拿起不能取消此前已经产生的行动。");
                RobotActionToken ignored;
                Assert.IsFalse(context.TryCreateActionToken(robot.InstanceId, out ignored));
                RobotEntity moved;
                Assert.AreEqual(ShopOperationResult.Success, context.TryCommitDeploy(first.InteractionId,
                    BattleSide.Player, board, new BoardCoordinate(1, 0), out moved));
                Assert.AreSame(robot, moved);
                Assert.IsNull(board.GetOccupant(new BoardCoordinate(0, 0)));
                Assert.AreEqual(robot.Definition.Id, board.GetOccupant(new BoardCoordinate(1, 0)));
                Assert.AreEqual(robot.Definition.Id, board.GetOccupant(new BoardCoordinate(2, 0)));
                Assert.IsTrue(context.IsActionTokenValid(token));
                var second = Begin(context, RobotDragSource.Owned(robot.InstanceId));
                context.CancelInteraction(first.InteractionId);
                Assert.AreSame(second, context.ActiveInteraction);
                Assert.AreEqual(ShopOperationResult.InvalidSource, context.TryCommitStorage(first.InteractionId, out moved));
                Assert.IsNull(board.GetOccupant(new BoardCoordinate(1, 0)));
                context.CancelInteraction(second.InteractionId);
                Assert.AreEqual(new BoardCoordinate(1, 0), robot.Anchor);
                Assert.AreEqual(robot.Definition.Id, board.GetOccupant(new BoardCoordinate(1, 0)));
                Assert.AreEqual(robot.Definition.Id, board.GetOccupant(new BoardCoordinate(2, 0)));
                Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void BoardMoveAcrossBoardsRetainsOriginalInstanceAndFailedDropCanRestore()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = ShopTestData.Purchase(context);
                var original = new BoardModel(4, 2);
                var destination = new BoardModel(4, 2);
                Deploy(context, robot, original);
                var session = Begin(context, RobotDragSource.Owned(robot.InstanceId));
                RobotEntity moved;
                Assert.AreEqual(ShopOperationResult.InvalidPlacement, context.TryCommitDeploy(session.InteractionId,
                    BattleSide.Player, destination, new BoardCoordinate(3, 0), out moved));
                Assert.IsTrue(context.IsCurrentInteraction(session.InteractionId));
                context.CancelInteraction(session.InteractionId);
                Assert.AreEqual(robot.Definition.Id, original.GetOccupant(new BoardCoordinate(0, 0)));
                session = Begin(context, RobotDragSource.Owned(robot.InstanceId));
                Assert.AreEqual(ShopOperationResult.Success, context.TryCommitDeploy(session.InteractionId,
                    BattleSide.Player, destination, new BoardCoordinate(2, 0), out moved));
                Assert.AreSame(robot, moved);
                Assert.IsNull(original.GetOccupant(new BoardCoordinate(0, 0)));
                Assert.AreEqual(robot.Definition.Id, destination.GetOccupant(new BoardCoordinate(2, 0)));
                Assert.AreEqual(robot.Definition.Id, destination.GetOccupant(new BoardCoordinate(3, 0)));
            }
        }

        [Test]
        public void BoardRobotStoresInEmptyStorageAndFullStorageReturnsItToOriginalAnchor()
        {
            using (var context = ShopTestData.Context())
            {
                var first = ShopTestData.Purchase(context);
                var second = ShopTestData.Purchase(context, 1);
                var board = new BoardModel(6, 2);
                Deploy(context, first, board);
                Deploy(context, second, board, 3);
                var session = Begin(context, RobotDragSource.Owned(first.InstanceId));
                RobotEntity stored;
                Assert.AreEqual(ShopOperationResult.Success, context.TryCommitStorage(session.InteractionId, out stored));
                Assert.AreSame(first, stored);
                Assert.AreEqual(first.InstanceId, context.GetState(BattleSide.Player).StorageInstanceId);
                Assert.AreEqual(RobotLocation.Storage, first.Location);
                Assert.IsNull(board.GetOccupant(new BoardCoordinate(0, 0)));
                session = Begin(context, RobotDragSource.Owned(second.InstanceId));
                Assert.AreEqual(ShopOperationResult.StorageFull, context.TryCommitStorage(session.InteractionId, out stored));
                Assert.IsNull(stored);
                Assert.IsFalse(context.IsBusy);
                Assert.IsFalse(second.IsDragging);
                Assert.AreEqual(new BoardCoordinate(3, 0), second.Anchor);
                Assert.AreEqual(second.Definition.Id, board.GetOccupant(new BoardCoordinate(3, 0)));
                Assert.AreEqual(second.Definition.Id, board.GetOccupant(new BoardCoordinate(4, 0)));
                Assert.AreEqual(first.InstanceId, context.GetState(BattleSide.Player).StorageInstanceId);
                Assert.AreEqual(8, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PurchasedAndUnpurchasedShopSourcesSwapStorageIntoTheirSourceSlot(bool purchased)
        {
            using (var context = ShopTestData.Context())
            {
                var oldStored = ShopTestData.Purchase(context);
                context.TryStoreOrSwap(BattleSide.Player, oldStored.InstanceId);
                var sourceRobot = purchased ? ShopTestData.Purchase(context, 1) : null;
                var source = purchased ? RobotDragSource.Owned(sourceRobot.InstanceId) : Offer(context);
                var session = Begin(context, source);
                RobotEntity robot;
                Assert.AreEqual(ShopOperationResult.Success, context.TryCommitStorage(session.InteractionId, out robot));
                if (purchased) Assert.AreSame(sourceRobot, robot);
                Assert.AreEqual(2, robot.InstanceId);
                Assert.AreEqual(RobotLocation.Storage, robot.Location);
                Assert.AreEqual(robot.InstanceId, context.GetState(BattleSide.Player).StorageInstanceId);
                Assert.AreEqual(oldStored.InstanceId, context.GetState(BattleSide.Player).Slots[1].InstanceId);
                Assert.IsNull(context.GetState(BattleSide.Player).Slots[1].Offer);
                Assert.AreEqual(RobotLocation.PurchasedShopSlot, oldStored.Location);
                Assert.AreEqual(8, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(ShopOperationResult.InvalidSource, context.TryCommitStorage(session.InteractionId, out robot));
                Assert.IsNull(robot);
                Assert.AreEqual(8, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void ReadOnlyMergeDoesNotNeedSessionAndChecksUnpurchasedOfferWithoutCharging()
        {
            using (var context = ShopTestData.Context())
            {
                var target = ShopTestData.Purchase(context);
                Deploy(context, target, new BoardModel(4, 2));
                var source = Offer(context);
                var offer = context.GetState(BattleSide.Player).Slots[1].Offer;
                var version = target.ActionVersion;
                Assert.AreEqual(ShopOperationResult.Success, context.CheckMerge(BattleSide.Player, source, target.InstanceId));
                Assert.IsFalse(context.IsBusy);
                Assert.AreEqual(1, target.Level);
                Assert.AreEqual(version, target.ActionVersion);
                Assert.AreSame(offer, context.GetState(BattleSide.Player).Slots[1].Offer);
                Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(ShopOperationResult.InvalidSource, context.TryCommitMerge(0, target.InstanceId));
                RobotEntity robot;
                Assert.AreEqual(ShopOperationResult.InvalidSource, context.TryCommitStorage(0, out robot));
                Assert.AreEqual(ShopOperationResult.InvalidSource, context.TryCommitDeploy(0, BattleSide.Player,
                    new BoardModel(4, 2), new BoardCoordinate(0, 0), out robot));
            }
        }

        [Test]
        public void InsufficientFundsForOfferMergePreservesTargetActionProtectionAndCanRetryAfterReward()
        {
            using (var context = ShopTestData.Context(quality: 6))
            {
                var target = ShopTestData.Purchase(context);
                var board = new BoardModel(4, 2);
                Deploy(context, target, board);
                Protect(target, 4f);
                RobotActionToken token;
                Assert.IsTrue(context.TryCreateActionToken(target.InstanceId, out token));
                var source = Offer(context);
                var offer = context.GetState(BattleSide.Player).Slots[1].Offer;
                context.Rewards.TryGrantGlassBreak(context.BattleId, "initial-offer-funds", BattleSide.Player, 20);
                var session = Begin(context, source);
                SetCoinsForCommitRevalidation(context, 0);
                Assert.AreEqual(ShopOperationResult.Success, context.CheckMerge(BattleSide.Player, source, target.InstanceId));
                Assert.AreEqual(ShopOperationResult.InsufficientCoins, context.TryCommitMerge(session.InteractionId, target.InstanceId));
                Assert.AreEqual(1, target.Level);
                Assert.AreEqual(4f, target.TriggerCooldownRemaining);
                Assert.IsTrue(context.IsActionTokenValid(token));
                Assert.AreSame(offer, context.GetState(BattleSide.Player).Slots[1].Offer);
                Assert.AreEqual(0, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(ShopOperationResult.Success,
                    context.Rewards.TryGrantGlassBreak(context.BattleId, "during-offer-drag", BattleSide.Player, 20));
                Assert.IsTrue(context.IsCurrentInteraction(session.InteractionId));
                Assert.AreEqual(ShopOperationResult.Success, context.TryCommitMerge(session.InteractionId, target.InstanceId));
                Assert.AreEqual(0, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(2, target.Level);
                Assert.AreEqual(0f, target.TriggerCooldownRemaining);
                Assert.IsFalse(context.IsActionTokenValid(token));
            }
        }

        [Test]
        public void MergeRejectsSelfMismatchedIdsLevelsSidesAndMissingOrNonBoardTargetAtomically()
        {
            var random = new ShopSequenceRandom();
            random.Enqueue(0, 0.4, 0, 0, 0, 0);
            using (var context = ShopTestData.Context(ShopTestData.Rules(), random))
            {
                var target = ShopTestData.Purchase(context);
                var different = ShopTestData.Purchase(context, 1);
                var same = ShopTestData.Purchase(context, 2);
                var enemy = ShopTestData.Purchase(context, side: BattleSide.Opponent);
                var board = new BoardModel(6, 2);
                Assert.AreEqual(ShopOperationResult.InvalidLocation, context.CheckMerge(BattleSide.Player,
                    RobotDragSource.Owned(same.InstanceId), target.InstanceId));
                Deploy(context, target, board);
                Protect(target, 2f);
                RobotActionToken token;
                Assert.IsTrue(context.TryCreateActionToken(target.InstanceId, out token));
                Assert.AreEqual(ShopOperationResult.IncompatibleMerge, context.CheckMerge(BattleSide.Player,
                    RobotDragSource.Owned(target.InstanceId), target.InstanceId));
                Assert.AreEqual(ShopOperationResult.WrongSide, context.CheckMerge(BattleSide.Opponent,
                    RobotDragSource.Owned(same.InstanceId), enemy.InstanceId));
                var source = Begin(context, RobotDragSource.Owned(different.InstanceId));
                Assert.AreEqual(ShopOperationResult.IncompatibleMerge, context.TryCommitMerge(source.InteractionId, target.InstanceId));
                Assert.AreEqual(ShopOperationResult.WrongSide, context.TryCommitMerge(source.InteractionId, enemy.InstanceId));
                Assert.AreEqual(ShopOperationResult.EntityNotFound, context.TryCommitMerge(source.InteractionId, 99999));
                Assert.AreEqual(1, target.Level);
                Assert.AreEqual(2f, target.TriggerCooldownRemaining);
                Assert.IsTrue(context.IsActionTokenValid(token));
                Assert.AreEqual(2, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.IsFalse(different.IsRemoved);
                context.CancelInteraction(source.InteractionId);
                Upgrade(same);
                source = Begin(context, RobotDragSource.Owned(same.InstanceId));
                Assert.AreEqual(ShopOperationResult.IncompatibleMerge, context.TryCommitMerge(source.InteractionId, target.InstanceId));
                Assert.AreEqual(1, target.Level);
                Assert.IsTrue(context.IsActionTokenValid(token));
            }
        }

        [Test]
        public void LevelFiveMergeLeavesBothSourcesAndTargetActionVersionUntouched()
        {
            using (var context = ShopTestData.Context())
            {
                var target = ShopTestData.Purchase(context);
                var source = ShopTestData.Purchase(context, 1);
                var board = new BoardModel(6, 2);
                Deploy(context, target, board);
                Deploy(context, source, board, 3);
                for (var count = 0; count < 4; count++) { Upgrade(target); Upgrade(source); }
                Protect(target, 2f);
                RobotActionToken token;
                Assert.IsTrue(context.TryCreateActionToken(target.InstanceId, out token));
                var sourceVersion = source.ActionVersion;
                var session = Begin(context, RobotDragSource.Owned(source.InstanceId));
                Assert.AreEqual(ShopOperationResult.MaxLevelReached, context.TryCommitMerge(session.InteractionId, target.InstanceId));
                Assert.IsTrue(context.IsActionTokenValid(token));
                Assert.AreEqual(2f, target.TriggerCooldownRemaining);
                Assert.AreEqual(sourceVersion, source.ActionVersion);
                Assert.IsFalse(source.IsRemoved);
                context.CancelInteraction(session.InteractionId);
                Assert.AreEqual(source.Definition.Id, board.GetOccupant(new BoardCoordinate(3, 0)));
            }
        }

        [Test]
        public void LevelFourMergesToFiveWithoutChangingTargetFootprintOrQuality()
        {
            using (var context = ShopTestData.Context())
            {
                var target = ShopTestData.Purchase(context);
                var source = ShopTestData.Purchase(context, 1);
                var board = new BoardModel(6, 2);
                Deploy(context, target, board);
                for (var count = 0; count < 3; count++) { Upgrade(target); Upgrade(source); }
                var definition = target.Definition;
                var session = Begin(context, RobotDragSource.Owned(source.InstanceId));
                Assert.AreEqual(ShopOperationResult.Success, context.TryCommitMerge(session.InteractionId, target.InstanceId));
                Assert.AreEqual(5, target.Level);
                Assert.AreSame(definition, target.Definition);
                Assert.AreEqual(2, target.QualityId);
                Assert.AreEqual(new BoardCoordinate(0, 0), target.Anchor);
                Assert.AreEqual(target.Definition.Id, board.GetOccupant(new BoardCoordinate(1, 0)));
                Assert.AreEqual(8, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void TwoStarOfferMergeUsesCurrentOfferPriceOnceAndDoesNotAllocateSource()
        {
            using (var context = ShopTestData.Context())
            {
                var target = ShopTestData.Purchase(context);
                Deploy(context, target, new BoardModel(4, 2));
                Upgrade(target);
                context.Rewards.TryGrantGlassBreak(context.BattleId, "two-star-funds", BattleSide.Player, 20);
                Assert.AreEqual(ShopOperationResult.Success, context.TryRefresh(BattleSide.Player));
                var offer = context.GetState(BattleSide.Player).Slots[1].Offer;
                Assert.AreEqual(2, offer.Level);
                Assert.AreEqual(12, offer.Price);
                var session = Begin(context, Offer(context));
                Assert.AreEqual(ShopOperationResult.Success, context.TryCommitMerge(session.InteractionId, target.InstanceId));
                Assert.AreEqual(3, target.Level);
                Assert.AreEqual(17, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(ShopOperationResult.InvalidSource, context.TryCommitMerge(session.InteractionId, target.InstanceId));
                Assert.AreEqual(17, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(2, ShopTestData.Purchase(context, 2).InstanceId);
            }
        }

        [Test]
        public void InsufficientOfferStorageSwapPreservesStoredRobotSlotAndBalanceUntilSuccessfulRetry()
        {
            using (var context = ShopTestData.Context(quality: 6))
            {
                var stored = ShopTestData.Purchase(context);
                context.TryStoreOrSwap(BattleSide.Player, stored.InstanceId);
                var original = context.GetState(BattleSide.Player).Slots[1].Offer;
                context.Rewards.TryGrantGlassBreak(context.BattleId, "initial-swap-funds", BattleSide.Player, 20);
                var session = Begin(context, Offer(context));
                SetCoinsForCommitRevalidation(context, 0);
                RobotEntity robot;
                Assert.AreEqual(ShopOperationResult.InsufficientCoins, context.TryCommitStorage(session.InteractionId, out robot));
                Assert.IsNull(robot);
                Assert.AreSame(original, context.GetState(BattleSide.Player).Slots[1].Offer);
                Assert.IsNull(context.GetState(BattleSide.Player).Slots[1].InstanceId);
                Assert.AreEqual(stored.InstanceId, context.GetState(BattleSide.Player).StorageInstanceId);
                Assert.AreEqual(RobotLocation.Storage, stored.Location);
                Assert.AreEqual(0, context.GetState(BattleSide.Player).EnergyCoins);
                context.Rewards.TryGrantGlassBreak(context.BattleId, "swap-funds", BattleSide.Player, 20);
                Assert.AreEqual(ShopOperationResult.Success, context.TryCommitStorage(session.InteractionId, out robot));
                Assert.AreEqual(2, robot.InstanceId, "失败交换不能分配实体ID。");
                Assert.AreEqual(stored.InstanceId, context.GetState(BattleSide.Player).Slots[1].InstanceId);
                Assert.AreEqual(robot.InstanceId, context.GetState(BattleSide.Player).StorageInstanceId);
                Assert.AreEqual(0, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void MissingTargetFootprintAndUnownedSourceCannotMergeOrInvalidateAction()
        {
            using (var context = ShopTestData.Context())
            {
                var target = ShopTestData.Purchase(context);
                var source = ShopTestData.Purchase(context, 1);
                var board = new BoardModel(4, 2);
                Deploy(context, target, board);
                RobotActionToken token;
                Assert.IsTrue(context.TryCreateActionToken(target.InstanceId, out token));
                var unowned = context.Registry.CreateRobotFromConfig(ShopTestData.Robot(1001), BattleSide.Player);
                Assert.AreEqual(ShopOperationResult.EntityNotFound, context.CheckMerge(BattleSide.Player,
                    RobotDragSource.Owned(unowned.InstanceId), target.InstanceId));
                var session = Begin(context, RobotDragSource.Owned(source.InstanceId));
                board.Release(target.Definition.Id);
                board.TryPlace(new BoardCoordinate(0, 0), new PlaceableDefinition(target.Definition.Id, 1, 1));
                Assert.AreEqual(ShopOperationResult.InvalidPlacement, context.TryCommitMerge(session.InteractionId, target.InstanceId));
                Assert.AreEqual(1, target.Level);
                Assert.IsFalse(source.IsRemoved);
                Assert.IsTrue(context.IsActionTokenValid(token));
                Assert.AreEqual(8, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void GlobalInteractionLockBlocksBothSidesButAllowsRewardsAndReadOnlyPreview()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = ShopTestData.Purchase(context);
                var session = Begin(context, Offer(context));
                RobotDragSession other;
                Assert.AreEqual(ShopOperationResult.Busy, context.TryBeginDrag(BattleSide.Player,
                    RobotDragSource.Owned(robot.InstanceId), out other));
                Assert.AreEqual(ShopOperationResult.Busy, context.TryBeginDrag(BattleSide.Opponent,
                    Offer(context, 0, BattleSide.Opponent), out other));
                Assert.IsNull(other);
                Assert.AreEqual(ShopOperationResult.Busy, context.TryRefresh(BattleSide.Opponent));
                Assert.AreEqual(ShopOperationResult.Busy, context.TryStoreOrSwap(BattleSide.Player, robot.InstanceId));
                Assert.AreEqual(ShopOperationResult.Success, context.Rewards.TryGrantEnemyKill(context.BattleId,
                    100001, BattleSide.Opponent, 4));
                Robot config;
                int level;
                PlaceableDefinition definition;
                Assert.AreEqual(ShopOperationResult.Success, context.GetDragContent(BattleSide.Opponent,
                    Offer(context, 0, BattleSide.Opponent), out config, out level, out definition));
                Assert.AreSame(session, context.ActiveInteraction);
                Assert.AreEqual(24, context.GetState(BattleSide.Opponent).EnergyCoins);
            }
        }

        [Test]
        public void SessionsCannotCrossBattlesAndCanceledSessionCannotCommitOrCancelReplacement()
        {
            using (var first = ShopTestData.Context())
            using (var second = ShopTestData.Context())
            {
                var a = Begin(first, Offer(first, 0));
                var b = Begin(second, Offer(second, 0));
                Assert.AreNotEqual(a.InteractionId, b.InteractionId);
                RobotEntity robot;
                Assert.AreEqual(ShopOperationResult.InvalidSource, second.TryCommitStorage(a.InteractionId, out robot));
                second.CancelInteraction(a.InteractionId);
                Assert.AreSame(b, second.ActiveInteraction);
                first.CancelInteraction(a.InteractionId);
                var replacement = Begin(first, Offer(first, 0));
                first.CancelInteraction(a.InteractionId);
                Assert.AreEqual(ShopOperationResult.InvalidSource, first.TryCommitStorage(a.InteractionId, out robot));
                Assert.AreSame(replacement, first.ActiveInteraction);
                Assert.AreEqual(20, first.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(20, second.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void StaleOfferAndRemovedOwnedSourceFailWithoutMutationAndInvalidConfigDoesNotConsumeOffer()
        {
            var config = ShopTestData.Robot(1001);
            using (var context = new BattleShopContext(ShopTestData.Rules(1, 0, 0), ShopTestData.Candidates,
                ShopTestData.Candidates, id => id == 1001 ? config : ShopTestData.Robot(id), new ShopSequenceRandom()))
            {
                var stale = Offer(context, 0);
                context.TryRefresh(BattleSide.Player);
                RobotDragSession session;
                Assert.AreEqual(ShopOperationResult.StaleOffer, context.TryBeginDrag(BattleSide.Player, stale, out session));
                var current = Offer(context, 0);
                session = Begin(context, current);
                var shape = config.ShapeId_Ref;
                config.ShapeId_Ref = null;
                RobotEntity robot;
                Assert.AreEqual(ShopOperationResult.InvalidConfiguration, context.TryCommitStorage(session.InteractionId, out robot));
                Assert.IsNull(robot);
                Assert.AreEqual(15, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.IsNotNull(context.GetState(BattleSide.Player).Slots[0].Offer);
                config.ShapeId_Ref = shape;
                Assert.AreEqual(ShopOperationResult.Success, context.TryCommitStorage(session.InteractionId, out robot));
                Assert.AreEqual(1, robot.InstanceId);
                Assert.AreEqual(3, context.GetState(BattleSide.Player).EnergyCoins);
                session = Begin(context, RobotDragSource.Owned(robot.InstanceId));
                EntityRemovalSnapshot removal;
                context.Registry.Remove(robot.InstanceId, EntityRemovalReason.Manual, out removal);
                RobotEntity ignored;
                Assert.AreEqual(ShopOperationResult.EntityNotFound, context.TryCommitStorage(session.InteractionId, out ignored));
                Assert.IsNull(ignored);
            }
        }

        [Test]
        public void ActionTokensRejectWrongBattleLevelVersionAndRemovalButSurviveDragCancellation()
        {
            using (var context = ShopTestData.Context())
            using (var other = ShopTestData.Context())
            {
                var robot = ShopTestData.Purchase(context);
                RobotActionToken token;
                Assert.IsFalse(context.TryCreateActionToken(robot.InstanceId, out token));
                Deploy(context, robot, new BoardModel(4, 2));
                Assert.IsTrue(context.TryCreateActionToken(robot.InstanceId, out token));
                Assert.IsFalse(other.IsActionTokenValid(token));
                Assert.IsFalse(context.IsActionTokenValid(new RobotActionToken(context.BattleId + 1,
                    robot.InstanceId, token.ActionVersion, token.Level)));
                Assert.IsFalse(context.IsActionTokenValid(new RobotActionToken(context.BattleId,
                    robot.InstanceId, token.ActionVersion + 1, token.Level)));
                Assert.IsFalse(context.IsActionTokenValid(new RobotActionToken(context.BattleId,
                    robot.InstanceId, token.ActionVersion, token.Level + 1)));
                var session = Begin(context, RobotDragSource.Owned(robot.InstanceId));
                RobotActionToken ignored;
                Assert.IsFalse(context.TryCreateActionToken(robot.InstanceId, out ignored));
                Assert.IsTrue(context.IsActionTokenValid(token));
                context.CancelInteraction(session.InteractionId);
                Assert.IsTrue(context.IsActionTokenValid(token));
                Assert.IsTrue(context.TryCreateActionToken(robot.InstanceId, out ignored));
                EntityRemovalSnapshot removal;
                context.Registry.Remove(robot.InstanceId, EntityRemovalReason.Manual, out removal);
                Assert.IsFalse(context.IsActionTokenValid(token));
                Assert.IsFalse(context.TryCreateActionToken(robot.InstanceId, out ignored));
                Assert.Greater(robot.ActionVersion, token.ActionVersion);
            }
        }

        [Test]
        public void EndBattleCancelsLiftedBoardDragBeforeClearingOccupancyAndInvalidatesTokens()
        {
            using (var context = ShopTestData.Context())
            {
                var robot = ShopTestData.Purchase(context);
                var board = new BoardModel(4, 2);
                Deploy(context, robot, board);
                RobotActionToken token;
                Assert.IsTrue(context.TryCreateActionToken(robot.InstanceId, out token));
                var session = Begin(context, RobotDragSource.Owned(robot.InstanceId));
                context.EndBattle();
                context.EndBattle();
                Assert.IsTrue(context.IsEnded);
                Assert.IsNull(context.ActiveInteraction);
                Assert.IsFalse(robot.IsDragging);
                Assert.IsTrue(robot.IsRemoved);
                Assert.AreEqual(EntityRemovalReason.BattleEnded, robot.RemovalReason);
                Assert.IsNull(board.GetOccupant(new BoardCoordinate(0, 0)));
                Assert.IsNull(board.GetOccupant(new BoardCoordinate(1, 0)));
                Assert.IsFalse(context.IsActionTokenValid(token));
                RobotEntity ignored;
                Assert.AreEqual(ShopOperationResult.BattleEnded, context.TryCommitStorage(session.InteractionId, out ignored));
                Assert.AreEqual(ShopOperationResult.BattleEnded, context.TryCommitMerge(session.InteractionId, robot.InstanceId));
                Assert.AreEqual(14, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [TestCase("Merged")]
        [TestCase("Changed")]
        public void EndRequestedDuringMergeNotificationSeesFinalStateThenSafelyClearsBattle(string eventKind)
        {
            using (var context = ShopTestData.Context())
            {
                var target = ShopTestData.Purchase(context);
                var board = new BoardModel(4, 2);
                Deploy(context, target, board);
                var session = Begin(context, Offer(context));
                var seen = 0;
                Action requestEnd = () =>
                {
                    seen++;
                    Assert.AreEqual(2, target.Level);
                    Assert.AreEqual(8, context.GetState(BattleSide.Player).EnergyCoins);
                    Assert.IsNull(context.ActiveInteraction);
                    context.EndBattle();
                    Assert.AreEqual(ShopOperationResult.BattleEnded, context.TryRefresh(BattleSide.Player));
                };
                Action<RobotMergeSnapshot> merged = snapshot => requestEnd();
                Action<int> changed = side => { if (!context.IsEnded) requestEnd(); };
                if (eventKind == "Merged") GameEvent.AddEventListener<RobotMergeSnapshot>(BattleShopEvents.Merged, merged);
                else GameEvent.AddEventListener<int>(BattleShopEvents.Changed, changed);
                try { Assert.AreEqual(ShopOperationResult.Success, context.TryCommitMerge(session.InteractionId, target.InstanceId)); }
                finally
                {
                    if (eventKind == "Merged") GameEvent.RemoveEventListener<RobotMergeSnapshot>(BattleShopEvents.Merged, merged);
                    else GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, changed);
                }
                Assert.AreEqual(1, seen);
                Assert.IsTrue(context.IsEnded);
                Assert.IsTrue(target.IsRemoved);
                Assert.IsNull(board.GetOccupant(new BoardCoordinate(0, 0)));
                Assert.IsNull(board.GetOccupant(new BoardCoordinate(1, 0)));
                Assert.IsTrue(context.GetState(BattleSide.Player).Slots.All(slot => slot.Offer == null && slot.InstanceId == null));
                Assert.AreEqual(8, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }
    }
}
