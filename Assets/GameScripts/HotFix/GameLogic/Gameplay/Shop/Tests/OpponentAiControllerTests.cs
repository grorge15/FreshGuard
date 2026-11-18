using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class OpponentAiControllerTests
    {
        [TestCase(OpponentAiActionKind.PurchaseAndDeploy)]
        [TestCase(OpponentAiActionKind.Move)]
        public void SplitLanePendingExecutionDoesNotBlockOtherLane(OpponentAiActionKind pendingKind)
        {
            var pending = new UniTaskCompletionSource<ShopOperationResult>();
            var purchases = 0;
            var moves = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(),
                () => Snapshot(robotAnchor: new BoardCoordinate(0, 0)),
                (action, token) => {
                    if (action.Kind == OpponentAiActionKind.Move) moves++; else purchases++;
                    return action.Kind == pendingKind ? pending.Task : UniTask.FromResult(ShopOperationResult.Success);
                }, planning: (state, options) => Action(state, options.Lane == OpponentAiActionLane.Purchase
                    ? OpponentAiActionKind.PurchaseAndDeploy : OpponentAiActionKind.Move), splitScheduling: true))
            {
                ai.Advance(.81f, true);
                Assert.AreEqual(1, purchases);
                Assert.AreEqual(1, moves, "购买等待和移动等待不能互锁");
                ai.Advance(3.1f, true);
                Assert.AreEqual(pendingKind == OpponentAiActionKind.Move ? 2 : 1, purchases);
                Assert.AreEqual(pendingKind == OpponentAiActionKind.Move ? 1 : 2, moves);
                pending.TrySetResult(ShopOperationResult.Success);
            }
        }

        [Test]
        public void SplitPurchaseIgnoresDragCooldownAndBothPauseTogether()
        {
            var purchases = 0;
            var moves = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(robotAnchor: new BoardCoordinate(0, 0)),
                (action, token) => { if (action.Kind == OpponentAiActionKind.Move) moves++; else purchases++;
                    return UniTask.FromResult(ShopOperationResult.Success); },
                planning: (state, options) => options.Lane == OpponentAiActionLane.Purchase
                    ? options.AllowPurchase ? Action(state, OpponentAiActionKind.PurchaseAndDeploy) : OpponentAiAction.Wait()
                    : options.AllowMove ? Action(state, OpponentAiActionKind.Move) : OpponentAiAction.Wait(), splitScheduling: true))
            {
                ai.Advance(.81f, true);
                ai.Advance(.81f, true);
                Assert.AreEqual(2, purchases);
                Assert.AreEqual(1, moves);
                ai.Advance(100f, false);
                Assert.AreEqual(2, purchases);
                Assert.AreEqual(1, moves);
            }
        }

        [Test]
        public void ArmyEstablishmentSurvivesAdjustmentFromEightToSeven()
        {
            var state = ArmySnapshot(8);
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => state,
                (action, token) => UniTask.FromResult(ShopOperationResult.Success),
                planning: (snapshot, options) => OpponentAiAction.Wait(), splitScheduling: true))
            {
                ai.Advance(.81f, true);
                Assert.IsTrue(ai.ArmyEstablished);
                state = ArmySnapshot(7);
                ai.MarkDirty();
                ai.Advance(.51f, true);
                Assert.IsTrue(ai.ArmyEstablished);
            }
        }

        [UnityTest]
        public IEnumerator SplitBackgroundRecruitmentCommitsPurchaseBeforeDrag() => VerifySplitPriority(4, OpponentAiActionKind.PurchaseAndDeploy);

        [UnityTest]
        public IEnumerator SplitBackgroundEstablishedArmyCommitsDragBeforePurchase() => VerifySplitPriority(5, OpponentAiActionKind.Move);

        private static IEnumerator VerifySplitPriority(int extras, OpponentAiActionKind first)
        {
            var actions = new List<OpponentAiActionKind>();
            var state = OpponentAiV2Tests.ArmyRing(extras);
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => state,
                (action, token) => { actions.Add(action.Kind); return UniTask.FromResult(ShopOperationResult.Success); },
                runSearchInBackground: true, splitScheduling: true))
            {
                ai.Advance(.81f, true);
                var timeout = DateTime.UtcNow.AddSeconds(15);
                while ((!actions.Contains(OpponentAiActionKind.Move) || !actions.Contains(OpponentAiActionKind.PurchaseAndDeploy)) && DateTime.UtcNow < timeout)
                { ai.Advance(.016f, true); yield return null; }
                Assert.That(actions.Count, Is.GreaterThanOrEqualTo(2));
                Assert.AreEqual(first, actions[0], "后台结果完成顺序不能覆盖阶段提交优先级");
                CollectionAssert.Contains(actions, OpponentAiActionKind.Move, "公平工作器不能让拖拽通道饿死");
                CollectionAssert.Contains(actions, OpponentAiActionKind.PurchaseAndDeploy);
            }
        }

        [Test]
        public void SplitMoveRoutesPurchaseFollowUpToPurchaseLane()
        {
            var observed = false;
            var moves = 0;
            var purchases = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => ArmySnapshot(8),
                (action, token) => { if (action.Kind == OpponentAiActionKind.Move) moves++; else purchases++;
                    return UniTask.FromResult(ShopOperationResult.Success); },
                planning: (state, options) => {
                    if (options.Lane == OpponentAiActionLane.Purchase)
                    {
                        observed |= options.PendingFollowUp.HasValue;
                        return options.PendingFollowUp?.ToAction() ?? OpponentAiAction.Wait();
                    }
                    if (moves > 0) return OpponentAiAction.Wait();
                    var follow = new OpponentAiFollowUp(Action(state, OpponentAiActionKind.PurchaseAndDeploy), true);
                    return new OpponentAiAction(OpponentAiActionKind.Move, sourceInstanceId: 1,
                        anchor: new BoardCoordinate(1, 1), battleId: state.BattleId, followUp: follow);
                }, splitScheduling: true))
            {
                ai.Advance(.81f, true);
                ai.Advance(.51f, true);
                Assert.IsTrue(observed);
                Assert.AreEqual(1, moves);
                Assert.AreEqual(1, purchases);
            }
        }

        private static OpponentAiSnapshot ArmySnapshot(int count)
        {
            var original = Snapshot();
            var board = new BoardModel(12, 4);
            var robots = new List<OpponentAiRobotSnapshot>();
            var profile = original.Offers[0].Profile;
            for (var i = 0; i < count; i++)
            {
                var definition = new PlaceableDefinition("army:" + i, 1, 1);
                var cell = new BoardCoordinate(i, 0);
                board.TryPlace(cell, definition);
                robots.Add(new OpponentAiRobotSnapshot(i + 1, profile, 1, 5, 10, 1, RobotLocation.Board, cell, definition));
            }
            return new OpponentAiSnapshot(original.Side, original.EnergyCoins, board, original.Offers, robots,
                original.CandidateProfiles, battleId: original.BattleId);
        }

        [UnityTest]
        public IEnumerator BackgroundSearchKeepsCaptureAndCommitOnMainThreadAndFreezesSubmissionOnPause()
        {
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var calls = 0;
            var world = new OpponentAiWorldSnapshot(default(OpponentAiBallState), null);
            using (var ai = new OpponentAiController(new OpponentAiSettings(),
                () => { Assert.AreEqual(mainThread, Thread.CurrentThread.ManagedThreadId); return Snapshot(world: world); },
                (action, token) => {
                    Assert.AreEqual(mainThread, Thread.CurrentThread.ManagedThreadId);
                    calls++; return UniTask.FromResult(ShopOperationResult.Success);
                }, runSearchInBackground: true))
            {
                ai.Advance(.81f, true);
                Assert.IsTrue(ai.IsSearching);
                Assert.AreEqual(0, calls, "发起搜索的帧不能同步运行精检和提交");
                for (var frame = 0; frame < 10; frame++) { ai.Advance(.1f, false); yield return null; }
                Assert.AreEqual(0, calls, "后台完成也不能在暂停时提交");
                var timeout = DateTime.UtcNow.AddSeconds(10);
                while (calls == 0 && DateTime.UtcNow < timeout) { ai.Advance(.016f, true); yield return null; }
                Assert.AreEqual(1, calls);
            }
        }

        [UnityTest]
        public IEnumerator DisposingPendingBackgroundSearchCannotSubmitLater()
        {
            var calls = 0;
            var world = new OpponentAiWorldSnapshot(default(OpponentAiBallState), null);
            var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(world: world),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); },
                runSearchInBackground: true);
            ai.Advance(.81f, true);
            ai.Dispose();
            for (var frame = 0; frame < 10; frame++) { ai.Advance(1f, true); yield return null; }
            Assert.IsFalse(ai.IsSearching);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void PreparationRetainsFollowUpButCannotBypassMovementCooldown()
        {
            var calls = 0;
            var observed = false;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(robotAnchor: new BoardCoordinate(0, 0)),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); },
                planning: (snapshot, options) => {
                    if (calls == 0)
                    {
                        var follow = new OpponentAiFollowUp(new OpponentAiAction(OpponentAiActionKind.Move,
                            sourceInstanceId: 1, anchor: new BoardCoordinate(2, 0), battleId: snapshot.BattleId), true);
                        return new OpponentAiAction(OpponentAiActionKind.Move, sourceInstanceId: 1,
                            anchor: new BoardCoordinate(1, 0), battleId: snapshot.BattleId, followUp: follow);
                    }
                    Assert.IsTrue(options.PendingFollowUp.HasValue);
                    observed = true;
                    return options.AllowMove ? options.PendingFollowUp.Value.ToAction() : OpponentAiAction.Wait();
                }))
            {
                ai.Advance(.81f, true);
                ai.Advance(.81f, true);
                Assert.IsTrue(observed);
                Assert.AreEqual(1, calls);
                ai.Advance(100f, false);
                ai.Advance(2.2f, true);
                Assert.AreEqual(2, calls);
            }
        }

        private static OpponentAiSnapshot Snapshot(long battleId = 123, int coins = 100,
            BoardCoordinate? robotAnchor = null, OpponentAiWorldSnapshot world = null, bool blocked = false)
        {
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1f, new PlaceableDefinition("p", 1, 1));
            var board = new BoardModel(5, 5);
            if (blocked) board.TryPlace(new BoardCoordinate(0, 0), new PlaceableDefinition("wall", 5, 5));
            var robots = new List<OpponentAiRobotSnapshot>();
            if (robotAnchor.HasValue)
            {
                var definition = new PlaceableDefinition("r1", 1, 1);
                board.TryPlace(robotAnchor.Value, definition);
                robots.Add(new OpponentAiRobotSnapshot(1, profile, 1, 5, 10, 1f,
                    RobotLocation.Board, robotAnchor, definition));
            }
            return new OpponentAiSnapshot(BattleSide.Opponent, coins, board,
                new[] { new OpponentAiOfferSnapshot(0, 11, profile, 1, 6, false),
                    new OpponentAiOfferSnapshot(1, 12, profile, 1, 6, false) },
                robots, new[] { profile }, battleId: battleId, world: world);
        }

        // 隔离策略评分，仍通过真实控制器和事务完成路径观察调度行为。
        private static OpponentAiAction Action(OpponentAiSnapshot snapshot, OpponentAiActionKind kind,
            long offerId = 11, BoardCoordinate anchor = default(BoardCoordinate))
            => new OpponentAiAction(kind, slotId: 0, offerId: offerId, sourceInstanceId: 1,
                targetInstanceId: 2, anchor: anchor, battleId: snapshot.BattleId,
                decisionReason: "调度测试", scoreBefore: 0.1f, scoreAfter: 0.3f);

        private static Func<OpponentAiSnapshot, OpponentAiPlanningOptions, OpponentAiAction> Plan(
            OpponentAiActionKind kind)
            => (snapshot, options) =>
            {
                if ((kind == OpponentAiActionKind.Move && !options.AllowMove) ||
                    (kind == OpponentAiActionKind.Refresh && !options.AllowRefresh) ||
                    ((kind == OpponentAiActionKind.PurchaseAndDeploy || kind == OpponentAiActionKind.PurchaseAndMerge)
                        && !options.AllowPurchase)) return OpponentAiAction.Wait();
                return Action(snapshot, kind);
            };

        [Test]
        public void InitialDelayAndPauseFreezePreventCatchupBursts()
        {
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); },
                planning: Plan(OpponentAiActionKind.Merge)))
            {
                ai.Advance(0.5f, true);
                ai.Advance(100f, false);
                ai.Advance(0.29f, true);
                Assert.AreEqual(0, calls);
                ai.Advance(0.02f, true);
                Assert.AreEqual(1, calls);
                ai.Advance(100f, true);
                Assert.AreEqual(2, calls, "长帧最多发起一次动作");
                ai.Advance(0f, true);
                Assert.AreEqual(2, calls);
            }
        }

        [Test]
        public void WaitUsesHalfSecondScansAndFreshSnapshotsWithoutNotifications()
        {
            var coins = 100;
            var observed = new List<int>();
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(coins: coins),
                (action, token) => { Assert.Fail("等待不能执行事务"); return default(UniTask<ShopOperationResult>); },
                planning: (snapshot, options) => { observed.Add(snapshot.EnergyCoins); return OpponentAiAction.Wait(); }))
            {
                ai.Advance(0.81f, true);
                coins = 99;
                ai.Advance(0.49f, true);
                Assert.AreEqual(new[] { 100 }, observed);
                ai.Advance(0.02f, true);
                Assert.AreEqual(new[] { 100, 99 }, observed);
            }
        }

        [Test]
        public void DirtyUsesFastScanWithoutReenteringOrShorteningInitialDelay()
        {
            var scans = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => UniTask.FromResult(ShopOperationResult.Success),
                planning: (snapshot, options) => { scans++; return OpponentAiAction.Wait(); }))
            {
                ai.MarkDirty();
                ai.Advance(0.15f, true);
                Assert.AreEqual(0, scans);
                ai.Advance(0.66f, true);
                Assert.AreEqual(1, scans);
                ai.MarkDirty();
                ai.MarkDirty();
                Assert.AreEqual(1, scans, "通知不能直接重入规划");
                ai.Advance(0.14f, true);
                Assert.AreEqual(1, scans);
                ai.Advance(0.02f, true);
                Assert.AreEqual(2, scans);
                ai.Advance(0.49f, true);
                Assert.AreEqual(2, scans, "没有新通知时恢复普通扫描间隔");
                ai.Advance(0.02f, true);
                Assert.AreEqual(3, scans);
            }
        }

        [Test]
        public void ConsecutiveActionsUseFixedSuccessInterval()
        {
            var calls = 0;
            var settings = new OpponentAiSettings();
            using (var ai = new OpponentAiController(settings, () => Snapshot(),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); },
                planning: Plan(OpponentAiActionKind.Merge)))
            {
                for (var i = 0; i < 4; i++) ai.Advance(0.81f, true);
                Assert.AreEqual(4, calls, "不再存在三动作一轮或随机轮次等待");
            }
        }

        [TestCase(OpponentAiActionKind.PurchaseAndDeploy)]
        [TestCase(OpponentAiActionKind.PurchaseAndMerge)]
        public void PurchasesUseTwoTokensAndRefillEveryThreeActiveSeconds(OpponentAiActionKind kind)
        {
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); }, planning: Plan(kind)))
            {
                ai.Advance(0.81f, true);
                ai.Advance(0.81f, true);
                Assert.AreEqual(2, calls, "初始可用两枚购买额度");
                ai.Advance(100f, false);
                ai.Advance(0.81f, true);
                Assert.AreEqual(2, calls, "暂停不恢复购买额度");
                ai.Advance(0.60f, true);
                Assert.AreEqual(3, calls, "累计有效时间超过三秒恢复一枚");
                ai.Advance(0.81f, true);
                Assert.AreEqual(3, calls);
            }
        }

        [Test]
        public void ExhaustedPurchaseTokensDoNotBlockOtherActionKinds()
        {
            var actions = new List<OpponentAiActionKind>();
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => { actions.Add(action.Kind); return UniTask.FromResult(ShopOperationResult.Success); },
                planning: (snapshot, options) => Action(snapshot, options.AllowPurchase
                    ? OpponentAiActionKind.PurchaseAndDeploy : OpponentAiActionKind.Merge)))
            {
                for (var i = 0; i < 4; i++) ai.Advance(0.81f, true);
                Assert.AreEqual(new[] { OpponentAiActionKind.PurchaseAndDeploy, OpponentAiActionKind.PurchaseAndDeploy,
                    OpponentAiActionKind.Merge, OpponentAiActionKind.PurchaseAndDeploy }, actions);
            }
        }

        [TestCase(ShopOperationResult.Busy)]
        [TestCase(ShopOperationResult.ResourceFailed)]
        [TestCase(ShopOperationResult.StaleOffer)]
        public void FailedPurchasesDoNotSpendTokensAndRetryUsesBackoff(ShopOperationResult failure)
        {
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => UniTask.FromResult(++calls == 1 ? failure : ShopOperationResult.Success),
                planning: (snapshot, options) => options.AllowPurchase
                    ? Action(snapshot, OpponentAiActionKind.PurchaseAndDeploy, calls == 0 ? 11 : 12)
                    : OpponentAiAction.Wait()))
            {
                ai.Advance(0.81f, true);
                ai.MarkDirty();
                ai.Advance(0.49f, true);
                Assert.AreEqual(1, calls, "脏通知不绕过失败退避");
                ai.Advance(0.02f, true);
                Assert.AreEqual(2, calls);
                ai.Advance(0.81f, true);
                Assert.AreEqual(3, calls, "失败后仍有两枚可成功购买的额度");
                ai.Advance(0.81f, true);
                Assert.AreEqual(3, calls);
            }
        }

        [Test]
        public void TokenRecoveryIsCappedAndLongFramesNeverDispatchABatch()
        {
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); },
                planning: Plan(OpponentAiActionKind.PurchaseAndDeploy)))
            {
                ai.Advance(0.81f, true);
                ai.Advance(0.81f, true);
                ai.Advance(100.5f, true);
                Assert.AreEqual(3, calls);
                ai.Advance(0f, true);
                Assert.AreEqual(3, calls);
                ai.Advance(0.81f, true);
                Assert.AreEqual(4, calls);
                ai.Advance(0.81f, true);
                Assert.AreEqual(4, calls, "容量最多两枚，不能积攒整段长帧的所有额度");
            }
        }

        [Test]
        public void GlobalSuccessIntervalStartsAtCompletionAndFreezesDuringPause()
        {
            var completion = new UniTaskCompletionSource<ShopOperationResult>();
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings { MinimumActionInterval = 0.01f }, () => Snapshot(),
                (action, token) => ++calls == 1 ? completion.Task : UniTask.FromResult(ShopOperationResult.Success),
                planning: Plan(OpponentAiActionKind.Merge)))
            {
                ai.Advance(0.81f, true);
                ai.Advance(100f, true);
                Assert.AreEqual(1, calls);
                completion.TrySetResult(ShopOperationResult.Success);
                ai.Advance(0.79f, true);
                ai.Advance(100f, false);
                Assert.AreEqual(1, calls, "加载时间不能预先抵扣成功后的 .8 秒");
                ai.Advance(0.02f, true);
                Assert.AreEqual(2, calls);
            }
        }

        [TestCase(OpponentAiActionKind.Move, 3f)]
        [TestCase(OpponentAiActionKind.Refresh, 8f)]
        public void IndependentCooldownsFreezeDuringPause(OpponentAiActionKind kind, float cooldown)
        {
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); }, planning: Plan(kind)))
            {
                ai.Advance(0.81f, true);
                ai.Advance(cooldown - 0.5f, true);
                Assert.AreEqual(1, calls);
                ai.Advance(100f, false);
                ai.MarkDirty();
                ai.Advance(0.51f, true);
                Assert.AreEqual(2, calls, "V2 移动读新字段，刷新独立保留八秒间隔");
            }
        }

        [TestCase(OpponentAiActionKind.Move)]
        [TestCase(OpponentAiActionKind.Refresh)]
        public void FailedActionsDoNotStartSuccessCooldowns(OpponentAiActionKind kind)
        {
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => UniTask.FromResult(++calls == 1 ? ShopOperationResult.Busy : ShopOperationResult.Success),
                planning: Plan(kind)))
            {
                ai.Advance(0.81f, true);
                ai.Advance(0.51f, true);
                Assert.AreEqual(2, calls);
            }
        }

        [Test]
        public void ResourceFailureSkipsFailedOfferForFiveActiveSeconds()
        {
            var ids = new List<long>();
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => { ids.Add(action.OfferId); return UniTask.FromResult(ShopOperationResult.ResourceFailed); },
                planning: (snapshot, options) =>
                {
                    foreach (var offer in snapshot.Offers)
                    {
                        var blocked = false;
                        foreach (var id in options.BlockedOfferIds) if (id == offer.OfferId) blocked = true;
                        if (!blocked) return Action(snapshot, OpponentAiActionKind.PurchaseAndDeploy, offer.OfferId);
                    }
                    return OpponentAiAction.Wait();
                }))
            {
                ai.Advance(0.81f, true);
                ai.Advance(0.51f, true);
                Assert.AreEqual(new long[] { 11, 12 }, ids);
                ai.Advance(100f, false);
                ai.Advance(0.51f, true);
                Assert.AreEqual(2, ids.Count);
                ai.Advance(4.01f, true);
                Assert.AreEqual(new long[] { 11, 12, 11 }, ids);
            }
        }

        [Test]
        public void BusyNotificationDoesNotReenterTheOnlyAsyncTask()
        {
            var completion = new UniTaskCompletionSource<ShopOperationResult>();
            var calls = 0;
            OpponentAiController ai = null;
            ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(), (action, token) =>
            {
                calls++;
                ai.MarkDirty();
                return completion.Task;
            }, planning: Plan(OpponentAiActionKind.Merge));
            using (ai)
            {
                ai.Advance(0.81f, true);
                ai.MarkDirty();
                ai.Advance(100f, true);
                Assert.AreEqual(1, calls);
                Assert.IsTrue(ai.IsExecuting);
                completion.TrySetResult(ShopOperationResult.Busy);
                ai.Advance(0.49f, true);
                Assert.AreEqual(1, calls);
            }
        }

        [Test]
        public void DisposeCancelsTheOnlyTaskAndIgnoresLateSuccess()
        {
            var completion = new UniTaskCompletionSource<ShopOperationResult>();
            var token = default(CancellationToken);
            var calls = 0;
            var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(robotAnchor: new BoardCoordinate(1, 1)),
                (action, cancellation) => { calls++; token = cancellation; return completion.Task; },
                planning: Plan(OpponentAiActionKind.Move));
            try
            {
                ai.Advance(0.81f, true);
                ai.Dispose();
                Assert.IsTrue(token.IsCancellationRequested);
                completion.TrySetResult(ShopOperationResult.Success);
                ai.Advance(100f, true);
                ai.MarkDirty();
                Assert.AreEqual(1, calls);
                Assert.AreEqual(0, ai.RecentMoves.Count);
                Assert.IsFalse(ai.IsExecuting);
            }
            finally { ai.Dispose(); }
        }

        [Test]
        public void SnapshotFromNewBattleDisposesOldControllerWithoutNotification()
        {
            var battle = 123L;
            var calls = 0;
            var cancellation = default(CancellationToken);
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(battle),
                (action, token) => { calls++; cancellation = token; return UniTask.FromResult(ShopOperationResult.Success); },
                planning: Plan(OpponentAiActionKind.Merge)))
            {
                ai.Advance(0.81f, true);
                battle++;
                ai.Advance(0.81f, true);
                Assert.IsTrue(cancellation.IsCancellationRequested);
                battle--;
                ai.Advance(100f, true);
                Assert.AreEqual(1, calls);
            }
        }

        [TestCase(ShopOperationResult.BattleEnded)]
        [TestCase(ShopOperationResult.WrongBattle)]
        public void TerminalTransactionResultsDisposeController(ShopOperationResult result)
        {
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => { calls++; return UniTask.FromResult(result); }, planning: Plan(OpponentAiActionKind.Merge)))
            {
                ai.Advance(0.81f, true);
                ai.Advance(100f, true);
                Assert.AreEqual(1, calls);
            }
        }

        [Test]
        public void SuccessfulMovesRememberOriginalAnchorAndActiveCompletionTime()
        {
            var anchor = new BoardCoordinate(1, 1);
            var destination = new BoardCoordinate(3, 3);
            var attempts = 0;
            var seenMemories = new List<int>();
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(robotAnchor: anchor),
                (action, token) =>
                {
                    if (++attempts == 1) return UniTask.FromResult(ShopOperationResult.Busy);
                    anchor = destination;
                    return UniTask.FromResult(ShopOperationResult.Success);
                }, planning: (snapshot, options) =>
                {
                    seenMemories.Add(options.RecentMoves.Count);
                    Assert.GreaterOrEqual(options.ActiveTime, 0.8f);
                    return options.AllowMove ? Action(snapshot, OpponentAiActionKind.Move, anchor: destination)
                        : OpponentAiAction.Wait();
                }))
            {
                ai.Advance(0.81f, true);
                Assert.AreEqual(0, ai.RecentMoves.Count, "失败移动不进入防抖记忆");
                ai.Advance(0.51f, true);
                Assert.AreEqual(1, ai.RecentMoves.Count);
                var memory = ai.RecentMoves[0];
                Assert.AreEqual(1, memory.InstanceId);
                Assert.AreEqual(new BoardCoordinate(1, 1), memory.From, "原位置来自执行前快照");
                Assert.AreEqual(destination, memory.To);
                Assert.AreEqual(1.32f, memory.ActiveTime, 0.001f);
                Assert.AreEqual("调度测试", ai.LastAction.DecisionReason);
                Assert.AreEqual(0.3f, ai.LastAction.ScoreAfter, 0.001f);
                ai.Advance(100f, false);
                ai.Advance(0.81f, true);
                Assert.AreEqual(new[] { 0, 0, 1 }, seenMemories);
            }
        }

        [Test]
        public void MoveHistoryKeepsOnlyLastTwoSuccessfulMoves()
        {
            var anchor = new BoardCoordinate(0, 0);
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(robotAnchor: anchor),
                (action, token) => { anchor = action.Anchor; return UniTask.FromResult(ShopOperationResult.Success); },
                planning: (snapshot, options) => Action(snapshot, OpponentAiActionKind.Move,
                    anchor: new BoardCoordinate(anchor.Column + 1, 0))))
            {
                ai.Advance(0.81f, true);
                ai.Advance(3.01f, true);
                ai.Advance(3.01f, true);
                Assert.AreEqual(2, ai.RecentMoves.Count);
                Assert.AreEqual(new BoardCoordinate(1, 0), ai.RecentMoves[0].From);
                Assert.AreEqual(new BoardCoordinate(2, 0), ai.RecentMoves[1].From);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WorldSearchKeepsProgressPastWindowAndFreezesDuringPause(bool launched)
        {
            var reads = 0;
            var calls = 0;
            // 满盘商品仍产生五十个粗筛锚点，但没有可提交动作。
            var world = new OpponentAiWorldSnapshot(new OpponentAiBallState(
                new Vector2(2.5f, 2.5f), Vector2.right, 1f, 0.1f, isLaunched: launched), null);
            using (var ai = new OpponentAiController(new OpponentAiSettings {
                    SearchCandidatesPerFrame = 1, SearchWindow = 0.05f },
                () => { reads++; return Snapshot(world: world, blocked: true); },
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); }))
            {
                ai.Advance(0.81f, true);
                Assert.IsTrue(ai.IsSearching, "首次只推进一个步骤，不能同步枚举整盘");
                ai.Advance(0.6f, true);
                Assert.IsTrue(ai.IsSearching, "窗口到期而无精检动作时保留候选");
                var beforePause = reads;
                ai.Advance(100f, false);
                Assert.AreEqual(beforePause, reads);
                for (var frame = 0; frame < 80 && ai.IsSearching; frame++) ai.Advance(0.01f, true);
                Assert.IsFalse(ai.IsSearching, "若每次窗口到期都重建会话，搜索永远不能完成");
                Assert.AreEqual(0, calls);
            }
        }

        [Test]
        public void SearchRestartsOnStructuralRevisionAndKeepsProgressWhenOnlyBallMoves()
        {
            var revision = 1;
            var position = 2f;
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings { SearchCandidatesPerFrame = 1 },
                () => Snapshot(world: new OpponentAiWorldSnapshot(new OpponentAiBallState(
                    new Vector2(position, 2.5f), Vector2.right, 1f, 0.1f), null, boardRevision: revision), blocked: true),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); }))
            {
                ai.Advance(0.81f, true);
                for (var frame = 0; frame < 9; frame++) ai.Advance(0.001f, true);
                revision++;
                ai.Advance(0.001f, true);
                for (var frame = 0; frame < 42; frame++)
                {
                    position += 0.001f;
                    ai.Advance(0.001f, true);
                }
                Assert.IsTrue(ai.IsSearching, "新结构的五十个锚点必须重新筛选");
                for (var frame = 0; frame < 20 && ai.IsSearching; frame++)
                {
                    position += 0.001f;
                    ai.Advance(0.001f, true);
                }
                Assert.IsFalse(ai.IsSearching, "仅球移动不应丢弃同版本结构的搜索进度");
                Assert.AreEqual(0, calls);
            }
        }

        [Test]
        public void InvalidDeltaTimeDoesNotAdvanceScheduling()
        {
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); },
                planning: Plan(OpponentAiActionKind.Merge)))
            {
                ai.Advance(float.NaN, true);
                ai.Advance(float.PositiveInfinity, true);
                ai.Advance(-1f, true);
                ai.Advance(0.79f, true);
                Assert.AreEqual(0, calls);
                ai.Advance(0.02f, true);
                Assert.AreEqual(1, calls);
            }
        }
    }
}
