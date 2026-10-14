using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class OpponentAiControllerTests
    {
        private sealed class MinimumRandom : IOpponentAiRandom
        {
            public float Range(float min, float max) => min;
        }

        private static OpponentAiSnapshot Snapshot(long battleId = 123)
        {
            var profile = new OpponentAiRobotProfile(1001, 1, 10, 1f, new PlaceableDefinition("p", 1, 1));
            return new OpponentAiSnapshot(BattleSide.Opponent, 100, new BoardModel(5, 5),
                new[] { new OpponentAiOfferSnapshot(0, 11, profile, 1, 6, false),
                    new OpponentAiOfferSnapshot(1, 12, profile, 1, 6, false) },
                new OpponentAiRobotSnapshot[0], new[] { profile }, null, battleId: battleId);
        }

        [Test]
        public void InitialDelayAndPauseFreezePreventCatchupBursts()
        {
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); }, new MinimumRandom()))
            {
                ai.Advance(0.5f, true);
                ai.Advance(100f, false);
                Assert.AreEqual(0, calls);
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
        public void TwoPurchasesPerRoundAndRoundDelayAreEnforced()
        {
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); }, new MinimumRandom()))
            {
                ai.Advance(0.81f, true);
                ai.Advance(0.81f, true);
                ai.Advance(0.81f, true);
                Assert.AreEqual(2, calls);
                ai.Advance(1.9f, true);
                Assert.AreEqual(2, calls);
                ai.Advance(0.11f, true);
                Assert.AreEqual(3, calls);
            }
        }

        [Test]
        public void BusyRetryDoesNotConsumeBudgetAndNotificationsDoNotReenter()
        {
            var calls = 0;
            OpponentAiController ai = null;
            ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(), (action, token) => {
                calls++;
                ai.MarkDirty();
                Assert.AreEqual(1, calls);
                return UniTask.FromResult(ShopOperationResult.Busy);
            }, new MinimumRandom());
            using (ai)
            {
                ai.Advance(0.81f, true);
                ai.Advance(0.49f, true);
                Assert.AreEqual(1, calls);
            }
            calls = 0;
            using (var retry = new OpponentAiController(new OpponentAiSettings { ActionsPerRound = 1 }, () => Snapshot(),
                (action, token) => UniTask.FromResult(++calls == 1 ? ShopOperationResult.Busy : ShopOperationResult.Success), new MinimumRandom()))
            {
                retry.Advance(0.81f, true);
                retry.Advance(0.51f, true);
                Assert.AreEqual(2, calls);
                retry.Advance(0.51f, true);
                Assert.AreEqual(2, calls);
            }
        }

        [Test]
        public void ResourceFailureSkipsFailedOfferForFiveActiveSeconds()
        {
            var ids = new List<long>();
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(), (action, token) => {
                if (action.OfferId != 0) ids.Add(action.OfferId);
                return UniTask.FromResult(ShopOperationResult.ResourceFailed);
            }, new MinimumRandom()))
            {
                ai.Advance(0.81f, true);
                ai.Advance(0.51f, true);
                Assert.AreEqual(new long[] { 11, 12 }, ids);
                ai.Advance(10f, false);
                ai.Advance(0.51f, true);
                Assert.AreEqual(2, ids.Count, "黑名单期间可刷新，但不能重复购买失败商品");
                ai.Advance(5.1f, true);
                Assert.AreEqual(11, ids[ids.Count - 1]);
            }
        }

        [Test]
        public void OnlyOneAsyncTaskAndDisposeCancelsLateCompletion()
        {
            var completion = new UniTaskCompletionSource<ShopOperationResult>();
            var token = default(CancellationToken);
            var calls = 0;
            var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(), (action, cancellation) => {
                calls++; token = cancellation; return completion.Task;
            }, new MinimumRandom());
            ai.Advance(0.81f, true);
            Assert.IsTrue(ai.IsExecuting);
            ai.Advance(100f, true);
            Assert.AreEqual(1, calls);
            ai.Dispose();
            Assert.IsTrue(token.IsCancellationRequested);
            completion.TrySetResult(ShopOperationResult.Success);
            ai.Advance(100f, true);
            Assert.AreEqual(1, calls);
            Assert.IsFalse(ai.IsExecuting);
        }

        [Test]
        public void ThreeSuccessesEndRoundEvenWhenMoreLegalActionsExist()
        {
            var profile = new OpponentAiRobotProfile(1001, 1, 10, 1f, new PlaceableDefinition("p", 1, 1), 20);
            var board = new BoardModel(5, 5);
            var robots = new List<OpponentAiRobotSnapshot>();
            foreach (var anchor in new[] { new BoardCoordinate(1, 1), new BoardCoordinate(3, 1),
                new BoardCoordinate(1, 3), new BoardCoordinate(3, 3) })
            {
                var id = robots.Count + 1;
                var definition = new PlaceableDefinition("r" + id, 1, 1);
                board.TryPlace(anchor, definition);
                robots.Add(new OpponentAiRobotSnapshot(id, profile, 1, 5, 10, 1f, RobotLocation.Board, anchor, definition));
            }
            var snapshot = new OpponentAiSnapshot(BattleSide.Opponent, 100, board,
                new[] { new OpponentAiOfferSnapshot(0, 11, profile, 1, 6, false) }, robots, new[] { profile }, null);
            var actions = new List<OpponentAiActionKind>();
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => snapshot, (action, token) => {
                actions.Add(action.Kind); return UniTask.FromResult(ShopOperationResult.Success);
            }, new MinimumRandom()))
            {
                ai.Advance(0.81f, true);
                ai.Advance(0.81f, true);
                ai.Advance(0.81f, true);
                Assert.AreEqual(new[] { OpponentAiActionKind.PurchaseAndMerge,
                    OpponentAiActionKind.PurchaseAndMerge, OpponentAiActionKind.Merge }, actions);
                ai.Advance(1.9f, true);
                Assert.AreEqual(3, actions.Count);
                ai.Advance(0.11f, true);
                Assert.AreEqual(4, actions.Count);
            }
        }

        [TestCase(OpponentAiActionKind.Refresh, 8f)]
        [TestCase(OpponentAiActionKind.Move, 10f)]
        public void CooldownsSurviveRoundBoundariesAndFreezeDuringPause(OpponentAiActionKind kind, float cooldown)
        {
            var profile = new OpponentAiRobotProfile(1001, 1, 10, 1f, new PlaceableDefinition("p", 1, 1));
            var board = new BoardModel(5, 5);
            var robots = new List<OpponentAiRobotSnapshot>();
            if (kind == OpponentAiActionKind.Move)
            {
                var definition = new PlaceableDefinition("r1", 1, 1);
                var anchor = new BoardCoordinate(0, 0);
                board.TryPlace(anchor, definition);
                robots.Add(new OpponentAiRobotSnapshot(1, profile, 1, 5, 10, 1f, RobotLocation.Board, anchor, definition));
            }
            var snapshot = new OpponentAiSnapshot(BattleSide.Opponent, kind == OpponentAiActionKind.Refresh ? 100 : 0,
                board, new OpponentAiOfferSnapshot[0], robots, new[] { profile }, null);
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => snapshot, (action, token) => {
                Assert.AreEqual(kind, action.Kind); calls++; return UniTask.FromResult(ShopOperationResult.Success);
            }, new MinimumRandom()))
            {
                ai.Advance(0.81f, true);
                ai.Advance(0.81f, true);
                ai.Advance(100f, false);
                var activeElapsed = 0.81f;
                while (activeElapsed + 2.01f < cooldown)
                {
                    ai.Advance(2.01f, true);
                    activeElapsed += 2.01f;
                    Assert.AreEqual(1, calls);
                }
                ai.Advance(2.01f, true);
                Assert.AreEqual(2, calls);
            }
        }

        [Test]
        public void SnapshotFromNewBattleDisposesOldController()
        {
            var battle = 123L;
            var calls = 0;
            using (var ai = new OpponentAiController(new OpponentAiSettings(), () => Snapshot(battle),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); }, new MinimumRandom()))
            {
                ai.Advance(0.81f, true);
                battle++;
                ai.MarkDirty();
                ai.Advance(0.81f, true);
                Assert.AreEqual(1, calls);
            }
        }
    }
}
