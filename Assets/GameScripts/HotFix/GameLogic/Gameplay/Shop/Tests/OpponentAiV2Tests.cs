using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class OpponentAiV2Tests
    {
        [Test]
        public void RecruitmentBuysEighthRobotWithoutImmediateScoreGain()
        {
            var board = new BoardModel(12, 4);
            var profile = new OpponentAiRobotProfile(501, 1, 0, 1, new PlaceableDefinition("p", 1, 1));
            var robots = new List<OpponentAiRobotSnapshot>();
            for (var i = 0; i < 7; i++)
            {
                var shape = new PlaceableDefinition("army:" + i, 1, 1);
                var cell = new BoardCoordinate(i, 0);
                board.TryPlace(cell, shape);
                robots.Add(new OpponentAiRobotSnapshot(i + 1, profile, 1, 5, 0, 1,
                    RobotLocation.Board, cell, shape));
            }
            var state = new OpponentAiSnapshot(BattleSide.Opponent, 20, board,
                new[] { new OpponentAiOfferSnapshot(21, 11, profile, 1, 6, false) }, robots, new[] { profile });
            state = state.WithArmyState(false);
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions {
                AllowMove = false, AllowMerge = false, AllowRefresh = false, AllowTwoStep = false });
            Assert.AreEqual(OpponentAiActionKind.PurchaseAndDeploy, action.Kind,
                "尚未达到8个时，不能因即时评分不足而拒绝安全扩军");
            Assert.IsTrue(OpponentAiPlanner.IsActionUseful(state, action), "提交复核也必须认可扩军收益");
        }

        [Test]
        public void RecruitmentPurchasePrecedesAvailableOneMoveSeal()
        {
            var state = ArmyRing(4);
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions {
                AllowMerge = false, AllowRefresh = false, AllowTwoStep = false });
            Assert.AreEqual(OpponentAiActionKind.PurchaseAndDeploy, action.Kind);
            Assert.AreEqual(new BoardCoordinate(4, 3), action.Anchor);
        }

        [Test]
        public void EstablishedArmyDragPrecedesPurchaseAndBlockedPurchaseCanFallBackToDrag()
        {
            foreach (var count in new[] { 4, 5 })
            {
                var action = OpponentAiPlanner.Plan(ArmyRing(count), new OpponentAiPlanningOptions {
                    AllowPurchase = count == 5, AllowMerge = false, AllowRefresh = false, AllowTwoStep = false });
                Assert.AreEqual(OpponentAiActionKind.Move, action.Kind);
                Assert.AreEqual(new BoardCoordinate(4, 3), action.Anchor);
            }
        }

        [Test]
        public void PartialRecruitmentResultCannotPublishLowerPriorityDrag()
        {
            using (var search = OpponentAiPlanner.BeginSearch(ArmyRing(4), new OpponentAiPlanningOptions {
                AllowMerge = false, AllowRefresh = false, AllowTwoStep = false }))
            {
                for (var i = 0; i < 1000 && !search.IsComplete && search.BestAction.Kind == OpponentAiActionKind.Wait; i++)
                {
                    search.Advance(1, double.PositiveInfinity);
                    search.PublishBestPartial();
                }
                Assert.AreEqual(OpponentAiActionKind.PurchaseAndDeploy, search.BestAction.Kind);
            }
        }

        [Test]
        public void EstablishedArmyCanReturnEighthRobotButCannotRemoveOrMergeSeventh()
        {
            var state = ArmyLine(8, true);
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions {
                AllowPurchase = false, AllowMerge = false, AllowRefresh = false, AllowTwoStep = false });
            Assert.AreEqual(OpponentAiActionKind.ReturnToShop, action.Kind);
            Assert.IsTrue(OpponentAiPlanner.IsActionUseful(state, action));
            state = ArmyLine(7, true);
            Assert.IsFalse(OpponentAiPlanner.IsActionUseful(state, action));
            Assert.IsFalse(OpponentAiPlanner.IsActionUseful(state, new OpponentAiAction(OpponentAiActionKind.Merge,
                sourceInstanceId: 1, targetInstanceId: 2, decisionReason: "保留围挡升级")));
            Assert.AreEqual(OpponentAiActionKind.Wait, OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions {
                AllowPurchase = false, AllowMerge = false, AllowRefresh = false, AllowTwoStep = false }).Kind);
        }

        [Test]
        public void StoredRobotCanRefillEstablishedArmyWithoutPurchaseTokens()
        {
            var state = ArmyLine(6, true);
            var robots = new List<OpponentAiRobotSnapshot>(state.Robots);
            var profile = robots[0].Profile;
            robots.Add(new OpponentAiRobotSnapshot(99, profile, 1, 5, 10, 1,
                RobotLocation.PurchasedShopSlot, null, new PlaceableDefinition("stored", 1, 1)));
            state = new OpponentAiSnapshot(state.Side, 0, state.Board, state.Offers, robots,
                state.CandidateProfiles, world: state.World).WithArmyState(true);
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions {
                AllowPurchase = false, AllowMerge = false, AllowRefresh = false, AllowTwoStep = false });
            Assert.AreEqual(OpponentAiActionKind.DeployOwned, action.Kind);
            Assert.AreEqual(99, action.SourceInstanceId);
            Assert.IsTrue(OpponentAiPlanner.IsActionUseful(state, action));
            Assert.IsTrue(ArmyLine(7, true).WithWorld(state.World).Army.Value.Established);
            Assert.IsTrue(OpponentAiArmyRules.NeedsRecruitment(ArmyLine(7, false)));
            Assert.IsFalse(OpponentAiArmyRules.NeedsRecruitment(ArmyLine(7, true)));
        }

        [Test]
        public void RecruitmentStillBuysAfterBallLaunchAndCannotIgnoreBallOverlap()
        {
            var state = ArmyLine(7, false);
            state = state.WithWorld(new OpponentAiWorldSnapshot(new OpponentAiBallState(
                new Vector2(10.5f, 2.5f), Vector2.up, 0, .2f), null, settings: state.World.Settings));
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions {
                AllowMove = false, AllowMerge = false, AllowRefresh = false, AllowTwoStep = false });
            Assert.AreEqual(OpponentAiActionKind.PurchaseAndDeploy, action.Kind);
            Assert.IsTrue(new OpponentAiRegionEvaluator().IsActionSafe(state, action));
            Assert.IsTrue(OpponentAiPlanner.IsActionUseful(state, action));
            var overlapping = new OpponentAiAction(OpponentAiActionKind.PurchaseAndDeploy, 21, 11,
                anchor: new BoardCoordinate(10, 2), decisionReason: "扩军补足机器人");
            Assert.IsNull(OpponentAiActionProof.Create(state, overlapping), "扩军不能绕过压球校验");
        }

        private static OpponentAiSnapshot ArmyLine(int count, bool established)
        {
            var board = new BoardModel(12, 4);
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1, new PlaceableDefinition("profile", 1, 1), 5);
            var robots = new List<OpponentAiRobotSnapshot>();
            for (var i = 0; i < count; i++)
            {
                var shape = new PlaceableDefinition("line:" + i, 1, 1);
                var cell = new BoardCoordinate(i, 0);
                board.TryPlace(cell, shape);
                robots.Add(new OpponentAiRobotSnapshot(i + 1, profile, 1, 5, 10, 1, RobotLocation.Board, cell, shape));
            }
            var world = new OpponentAiWorldSnapshot(default(OpponentAiBallState), null,
                settings: new OpponentAiStrategySettings { GlassWeights = new Vector4(0, 0, 0, 1), MinimumScoreGain = .001f });
            return new OpponentAiSnapshot(BattleSide.Opponent, 20, board,
                new[] { new OpponentAiOfferSnapshot(21, 11, profile, 1, 6, false) }, robots, new[] { profile },
                world: world, returnSlotIds: new[] { 99 }).WithArmyState(established);
        }

        [Test]
        public void DragLaneCanPlanMoveThenPurchaseAcrossLanes()
        {
            var original = TwoStepState();
            var board = original.Board;
            board.Release(original.Robots[3].Definition.Id);
            var robots = new List<OpponentAiRobotSnapshot>(original.Robots);
            robots.RemoveAt(3);
            var state = new OpponentAiSnapshot(original.Side, 20, board,
                new[] { new OpponentAiOfferSnapshot(21, 11, robots[0].Profile, 1, 6, false) },
                robots, original.CandidateProfiles, battleId: original.BattleId, world: original.World);
            var options = new OpponentAiPlanningOptions {
                Lane = OpponentAiActionLane.Drag, AllowPurchase = false, AllowMerge = false, AllowRefresh = false,
                FollowUpPurchase = true };
            var action = OpponentAiPlanner.Plan(state, options);
            Assert.AreEqual(OpponentAiActionKind.Move, action.Kind);
            Assert.IsTrue(action.FollowUp.HasValue);
            Assert.AreEqual(OpponentAiActionKind.PurchaseAndDeploy, action.FollowUp.Value.Kind);
        }

        internal static OpponentAiSnapshot ArmyRing(int extras)
        {
            var state = RingState(3, true);
            var board = state.Board;
            var robots = new List<OpponentAiRobotSnapshot>(state.Robots);
            for (var i = 0; i < extras; i++)
            {
                var shape = new PlaceableDefinition("extra:" + i, 1, 1);
                var cell = new BoardCoordinate(i, 0);
                board.TryPlace(cell, shape);
                robots.Add(new OpponentAiRobotSnapshot(i + 4, state.Robots[0].Profile, 1, 5, 10, 1,
                    RobotLocation.Board, cell, shape));
            }
            return new OpponentAiSnapshot(state.Side, state.EnergyCoins, board, state.Offers, robots,
                state.CandidateProfiles, world: state.World).WithArmyState(false);
        }

        [Test]
        public void IndependentRobotSealOutranksHighDamageGlassAssistedCandidate()
        {
            var state = GlassAssistedState();
            var evaluator = new OpponentAiRegionEvaluator();
            Assert.AreEqual(OpponentAiEnclosureKind.Glass,
                evaluator.Evaluate(state, state.Board, state.Robots, true).Enclosure);
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions {
                AllowMove = false, AllowMerge = false, AllowRefresh = false, AllowTwoStep = false });
            Assert.AreEqual(OpponentAiActionKind.PurchaseAndDeploy, action.Kind);
            Assert.AreEqual(11, action.OfferId, "独立机器人封口应优先于仍依赖玻璃的高伤害候选");
            Assert.AreEqual(new BoardCoordinate(4, 3), action.Anchor);
            Assert.AreEqual(OpponentAiEnclosureKind.Robots, action.MinimumEnclosure);
            Assert.IsTrue(OpponentAiPlanner.IsActionUseful(state, action));
        }

        [Test]
        public void RobotSealUpgradeDoesNotRequireAnIncreaseInNumericControl()
        {
            var state = RingState(3, true);
            var action = new OpponentAiAction(OpponentAiActionKind.PurchaseAndDeploy, 21, 11,
                anchor: new BoardCoordinate(4, 3), decisionReason: "一步封口",
                minimumEnclosure: OpponentAiEnclosureKind.Robots);
            var before = new OpponentAiRegionEvaluation { Valid = true, Enclosure = OpponentAiEnclosureKind.Glass, Control = .9f };
            var after = new OpponentAiRegionEvaluation { Valid = true, Enclosure = OpponentAiEnclosureKind.Robots, Control = .1f };
            Assert.IsTrue(OpponentAiPlanner.IsActionUseful(state, action, null, before, after));
            after.Enclosure = OpponentAiEnclosureKind.Glass;
            after.Control = 1f;
            Assert.IsFalse(OpponentAiPlanner.IsActionUseful(state, action, null, before, after));
        }

        [Test]
        public void FollowUpRetainsRobotOnlyRequirementAndRejectsGlassDowngrade()
        {
            var state = GlassAssistedState();
            var intended = new OpponentAiAction(OpponentAiActionKind.PurchaseAndDeploy, 22, 12,
                anchor: new BoardCoordinate(4, 3), minimumEnclosure: OpponentAiEnclosureKind.Robots);
            var follow = new OpponentAiFollowUp(intended, true);
            Assert.AreEqual(OpponentAiEnclosureKind.Robots, follow.MinimumEnclosure);
            var pending = follow.ToAction();
            Assert.IsFalse(OpponentAiPlanner.IsActionUseful(state, pending),
                "后续动作即使仍有玻璃封闭，也不能满足原定的独立机器人环目标");
        }

        [Test]
        public void FourRobotsDoNotPreventBuyingAFifthForAnIndependentSeal()
        {
            var original = RingState(3, true);
            var board = original.Board;
            var definition = new PlaceableDefinition("extra", 1, 1);
            var profile = original.Robots[0].Profile;
            board.TryPlace(new BoardCoordinate(0, 0), definition);
            var robots = new List<OpponentAiRobotSnapshot>(original.Robots) {
                new OpponentAiRobotSnapshot(4, profile, 1, 5, 10, 1, RobotLocation.Board,
                    new BoardCoordinate(0, 0), definition)
            };
            var state = new OpponentAiSnapshot(original.Side, original.EnergyCoins, board, original.Offers,
                robots, original.CandidateProfiles, world: original.World);
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions {
                AllowMove = false, AllowMerge = false, AllowRefresh = false });
            Assert.AreEqual(OpponentAiActionKind.PurchaseAndDeploy, action.Kind);
            Assert.AreEqual(new BoardCoordinate(4, 3), action.Anchor);
            Assert.AreEqual(OpponentAiEnclosureKind.Robots, action.MinimumEnclosure);
        }

        [Test]
        public void CoarseFallbackCannotRestoreEnclosureRewardForAProvenOpenPolygon()
        {
            var original = RingState(3, true);
            var board = new BoardModel(64, 64);
            foreach (var robot in original.Robots) board.TryPlace(robot.Anchor.Value, robot.Definition);
            var thin = new OpponentAiColliderSnapshot("ai:offer:11", new BoardCoordinate(4, 3), new[] {
                new[] { new Vector2(4.45f, 3.45f), new Vector2(4.55f, 3.45f),
                    new Vector2(4.55f, 3.55f), new Vector2(4.45f, 3.55f) }
            });
            var world = new OpponentAiWorldSnapshot(original.World.Ball, original.World.Glass, new[] { thin },
                settings: new OpponentAiStrategySettings { GlassWeights = new Vector4(0, 0, 1, 0) });
            var state = new OpponentAiSnapshot(original.Side, original.EnergyCoins, board, original.Offers,
                original.Robots, original.CandidateProfiles, world: world);
            var action = new OpponentAiAction(OpponentAiActionKind.PurchaseAndDeploy, 21, 11,
                anchor: new BoardCoordinate(4, 3), decisionReason: "改善玻璃接触",
                scoreReference: new OpponentAiScoreReference(Vector4.one, false));
            Assert.IsFalse(OpponentAiPlanner.IsActionUseful(state, action), "提交回退不能恢复被精检否定的围挡收益");
            Assert.AreEqual(OpponentAiActionKind.Wait, OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions {
                AllowMove = false, AllowMerge = false, AllowRefresh = false, AllowTwoStep = false }).Kind,
                "搜索回退也必须排除同一种虚假围挡收益");
        }

        private static OpponentAiSnapshot GlassAssistedState()
        {
            var board = new BoardModel(7, 7);
            var low = new OpponentAiRobotProfile(501, 1, 10, 1, new PlaceableDefinition("low", 1, 1));
            var high = new OpponentAiRobotProfile(502, 1, 10000, 1, new PlaceableDefinition("high", 1, 1));
            var robots = new[] {
                new OpponentAiRobotSnapshot(1, low, 1, 5, 10, 1, RobotLocation.Board,
                    new BoardCoordinate(2, 2), new PlaceableDefinition("left", 1, 3)),
                new OpponentAiRobotSnapshot(2, low, 1, 5, 10, 1, RobotLocation.Board,
                    new BoardCoordinate(3, 2), new PlaceableDefinition("bottom", 2, 1)),
                new OpponentAiRobotSnapshot(3, low, 1, 5, 10, 1, RobotLocation.Board,
                    new BoardCoordinate(3, 4), new PlaceableDefinition("top", 2, 1))
            };
            foreach (var robot in robots) board.TryPlace(robot.Anchor.Value, robot.Definition);
            var thin = new OpponentAiColliderSnapshot("ai:offer:12", new BoardCoordinate(4, 3), new[] {
                new[] { new Vector2(4.6f, 3.46f), new Vector2(4.7f, 3.46f),
                    new Vector2(4.7f, 3.54f), new Vector2(4.6f, 3.54f) }
            });
            var world = new OpponentAiWorldSnapshot(new OpponentAiBallState(new Vector2(3.5f, 3.5f), Vector2.up, 0, .2f),
                new[] { new OpponentAiGlassState(new BoardCoordinate(5, 3), 1, 1, 10000) }, new[] { thin },
                settings: new OpponentAiStrategySettings { GlassWeights = new Vector4(.65f, .3f, .04f, .01f) });
            return new OpponentAiSnapshot(BattleSide.Opponent, 20, board,
                new[] { new OpponentAiOfferSnapshot(21, 11, low, 1, 6, false),
                    new OpponentAiOfferSnapshot(22, 12, high, 1, 6, false) }, robots, new[] { low, high }, world: world);
        }

        [Test]
        public void BackgroundRegionProofRejectsBallLeavingSealAndChangedBoard()
        {
            var state = RingState(3, true);
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions { AllowMove = false, AllowMerge = false, AllowRefresh = false });
            var proof = OpponentAiActionProof.Create(state, action);
            Assert.IsNotNull(proof);
            Assert.IsTrue(proof.CanCommit(state));
            var moved = state.WithWorld(new OpponentAiWorldSnapshot(new OpponentAiBallState(
                new Vector2(5.5f, 5.5f), Vector2.up, 2, .2f), state.World.Glass, phase: state.StrategyPhase));
            Assert.IsFalse(proof.CanCommit(moved));
            var revised = state.WithWorld(new OpponentAiWorldSnapshot(state.World.Ball, state.World.Glass,
                boardRevision: state.BoardRevision + 1, phase: state.StrategyPhase));
            Assert.IsFalse(proof.CanCommit(revised));
        }

        [Test]
        public void BackgroundTwoStepProofRejectsBallLeavingFinalSeal()
        {
            var state = TwoStepState();
            var follow = new OpponentAiAction(OpponentAiActionKind.Move, sourceInstanceId: 4,
                anchor: new BoardCoordinate(4, 3), battleId: 123);
            var action = new OpponentAiAction(OpponentAiActionKind.Move, sourceInstanceId: 3,
                anchor: new BoardCoordinate(3, 4), battleId: 123, decisionReason: "两步准备",
                followUp: new OpponentAiFollowUp(follow, true));
            var proof = OpponentAiActionProof.Create(state, action);
            Assert.IsNotNull(proof);
            Assert.IsTrue(proof.CanCommit(state));
            var moved = state.WithWorld(new OpponentAiWorldSnapshot(new OpponentAiBallState(
                new Vector2(5.5f, 3.5f), Vector2.up, 2, .2f), state.World.Glass,
                boardRevision: state.BoardRevision, phase: state.StrategyPhase, settings: state.World.Settings));
            Assert.IsFalse(proof.CanCommit(moved), "球已从缺口离开最终围挡，不能再提交旧的两步准备动作");
        }

        [Test]
        public void CohortScoreReferenceKeepsGainDespiteHighOutputAlternative()
        {
            var before = new OpponentAiRegionEvaluation { GlassValue = 1, RobotValue = .1f, Control = 1, Mobility = 1 };
            var after = new OpponentAiRegionEvaluation { GlassValue = 1.2f, RobotValue = .08f, Control = 1, Mobility = 1 };
            var reference = new OpponentAiScoreReference(new Vector4(1.2f, 1, 1, 1), true);
            var action = new OpponentAiAction(OpponentAiActionKind.Move, scoreReference: reference);
            var weights = new OpponentAiStrategySettings().GlassWeights;
            var gain = action.ScoreReference.Value.Score(after, weights) - action.ScoreReference.Value.Score(before, weights);
            Assert.That(gain, Is.EqualTo(.071f).Within(.00001f));
            Assert.Greater(gain, .05f);
            var pair = OpponentAiScoreReference.ForPair(before, after, true);
            Assert.Less(pair.Score(after, weights) - pair.Score(before, weights), .05f,
                "提交不能擅自缩小归一化范围而拒绝已达标的动作");
        }

        [Test]
        public void BerserkBallOutsideCircleCannotMergeAwayOriginalWalls()
        {
            var original = RingState(4, false);
            var state = original.WithWorld(new OpponentAiWorldSnapshot(new OpponentAiBallState(
                new Vector2(5.5f, 5.5f), Vector2.up, 2, .2f, isBerserk: true), original.World.Glass,
                phase: OpponentAiStrategyPhase.ComboControl));
            var merge = new OpponentAiAction(OpponentAiActionKind.Merge, sourceInstanceId: 1, targetInstanceId: 2);
            Assert.IsFalse(new OpponentAiRegionEvaluator().IsActionSafe(state, merge));
            Assert.AreEqual(OpponentAiActionKind.Wait, OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions {
                AllowMove = false, AllowPurchase = false, AllowRefresh = false }).Kind);
        }

        [Test]
        public void ThinRealPolygonCannotEarnCoarseGridSealReward()
        {
            var original = RingState(3, true);
            var thin = new OpponentAiColliderSnapshot("ai:offer:11", new BoardCoordinate(4, 3), new[] {
                new[] { new Vector2(4.45f, 3.45f), new Vector2(4.55f, 3.45f), new Vector2(4.55f, 3.55f), new Vector2(4.45f, 3.55f) } });
            var state = original.WithWorld(new OpponentAiWorldSnapshot(original.World.Ball, original.World.Glass,
                new[] { thin }, phase: original.StrategyPhase));
            var action = new OpponentAiAction(OpponentAiActionKind.PurchaseAndDeploy, 21, 11,
                anchor: new BoardCoordinate(4, 3), decisionReason: "一步封口");
            Assert.IsFalse(OpponentAiPlanner.IsActionUseful(state, action));
        }

        [Test]
        public void SameBoardRevisionUsesLatestBallWhenVerifyingShortlistedSeal()
        {
            var original = RingState(3, true);
            var moved = original.WithWorld(new OpponentAiWorldSnapshot(new OpponentAiBallState(
                new Vector2(5.5f, 5.5f), Vector2.up, 2, .2f), original.World.Glass,
                phase: original.StrategyPhase));
            var options = new OpponentAiPlanningOptions { AllowMove = false, AllowMerge = false, AllowRefresh = false };
            using (var search = OpponentAiPlanner.BeginSearch(original, options))
            {
                search.Advance(16, double.PositiveInfinity);
                search.UpdateLiveSnapshot(moved, options);
                search.PublishBestPartial();
                Assert.AreNotEqual("一步封口", search.BestAction.DecisionReason);
            }
            var stale = new OpponentAiAction(OpponentAiActionKind.PurchaseAndDeploy, 21, 11,
                anchor: new BoardCoordinate(4, 3), decisionReason: "一步封口");
            Assert.IsFalse(OpponentAiPlanner.IsActionUseful(moved, stale));
        }

        [Test]
        public void BoundedTwoStepReturnsOneSafePreparationAndKeepsFollowUpIdentity()
        {
            var state = TwoStepState();
            var robots = state.Robots;
            var world = state.World;
            var evaluator = new OpponentAiRegionEvaluator();
            var preparation = new OpponentAiAction(OpponentAiActionKind.Move, sourceInstanceId: 3,
                anchor: new BoardCoordinate(3, 4), battleId: 123);
            Assert.IsTrue(evaluator.IsActionSafe(state, preparation), "第一步存在安全准备位置");
            var preparedBoard = state.Board;
            preparedBoard.Release(robots[2].Definition.Id);
            preparedBoard.TryPlace(preparation.Anchor, robots[2].Definition);
            var preparedRobots = new List<OpponentAiRobotSnapshot>(robots);
            var profile = robots[2].Profile;
            preparedRobots[2] = new OpponentAiRobotSnapshot(3, profile, 1, 5, 10, 1, RobotLocation.Board,
                preparation.Anchor, robots[2].Definition);
            var prepared = new OpponentAiSnapshot(BattleSide.Opponent, 0, preparedBoard, state.Offers,
                preparedRobots, state.CandidateProfiles, battleId: 123, world: world);
            Assert.IsTrue(evaluator.IsActionSafe(prepared, new OpponentAiAction(OpponentAiActionKind.Move,
                sourceInstanceId: 4, anchor: new BoardCoordinate(4, 3), battleId: 123)), "第二步存在安全封口位置");
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions { AllowMerge = false, AllowRefresh = false });
            Assert.AreEqual(OpponentAiActionKind.Move, action.Kind);
            Assert.IsTrue(action.FollowUp.HasValue, "单步收益不足时应返回安全准备动作及待重新校验的第二步");
            Assert.AreEqual(123, action.FollowUp.Value.BattleId);
            Assert.AreNotEqual(action.SourceInstanceId, action.FollowUp.Value.SourceInstanceId);
            Assert.IsTrue(new OpponentAiRegionEvaluator().IsActionSafe(state, action));
        }

        [Test]
        public void TwoStepVerificationUsesLatestBallForBothLayouts()
        {
            var state = TwoStepState();
            var options = new OpponentAiPlanningOptions { AllowMerge = false, AllowRefresh = false };
            using (var search = OpponentAiPlanner.BeginSearch(state, options))
            {
                while (search.EvaluatedCandidates < 192) search.Advance(1, double.PositiveInfinity);
                search.Advance(2, double.PositiveInfinity); // 进入第二层枚举后再移动球，结构版本不变。
                Assert.IsFalse(search.IsComplete);
                var moved = state.WithWorld(new OpponentAiWorldSnapshot(new OpponentAiBallState(
                    new Vector2(5.5f, 5.5f), Vector2.up, 2, .2f), state.World.Glass,
                    phase: state.StrategyPhase, settings: state.World.Settings));
                search.UpdateLiveSnapshot(moved, options);
                while (!search.IsComplete) search.Advance(64, double.PositiveInfinity);
                var action = search.BestAction;
                Assert.IsTrue(!action.FollowUp.HasValue || !action.FollowUp.Value.RequireClosed,
                    "不能用旧球位置给最新球位置返回已经失效的两步封口");
                if (action.Kind != OpponentAiActionKind.Wait)
                    Assert.IsTrue(OpponentAiPlanner.IsActionUseful(moved, action));
            }
        }

        private static OpponentAiSnapshot TwoStepState()
        {
            var board = new BoardModel(7, 7);
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1, new PlaceableDefinition("p", 1, 1));
            var anchors = new[] { new BoardCoordinate(2, 3), new BoardCoordinate(3, 2), new BoardCoordinate(0, 0), new BoardCoordinate(6, 6) };
            var robots = new List<OpponentAiRobotSnapshot>();
            for (var i = 0; i < anchors.Length; i++)
            {
                var definition = new PlaceableDefinition("r" + i, 1, 1);
                board.TryPlace(anchors[i], definition);
                robots.Add(new OpponentAiRobotSnapshot(i + 1, profile, 1, 5, 10, 1, RobotLocation.Board, anchors[i], definition));
            }
            var tuning = new OpponentAiStrategySettings { MinimumScoreGain = 1f };
            var world = new OpponentAiWorldSnapshot(new OpponentAiBallState(new Vector2(3.5f, 3.5f), Vector2.up, 2, .2f),
                new OpponentAiGlassState[0], phase: OpponentAiStrategyPhase.ComboControl, settings: tuning);
            return new OpponentAiSnapshot(BattleSide.Opponent, 0, board, new OpponentAiOfferSnapshot[0], robots,
                new[] { profile }, battleId: 123, world: world);
        }

        [Test]
        public void IncrementalSearchStopsAtCandidateBudgetOnLargeMap()
        {
            var board = new BoardModel(MapData.Create(64, 64));
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1f, new PlaceableDefinition("p", 1, 1));
            var state = new OpponentAiSnapshot(BattleSide.Opponent, 10, board,
                new[] { new OpponentAiOfferSnapshot(21, 11, profile, 1, 6, false) },
                new OpponentAiRobotSnapshot[0], new[] { profile });
            using (var search = OpponentAiPlanner.BeginSearch(state))
            {
                search.Advance(1, double.PositiveInfinity);
                search.Advance(3, double.PositiveInfinity);
                Assert.LessOrEqual(search.EvaluatedCandidates, 3);
                Assert.IsFalse(search.IsComplete, "不能在一次Advance里枚举完整64×64地图");
            }
        }

        [Test]
        public void MovingReleasesOwnFootprintWithoutMutatingSnapshotOrLiveBoard()
        {
            var shape = new PlaceableDefinition("r1", 2, 1);
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1, shape);
            var board = new BoardModel(4, 2);
            board.TryPlace(new BoardCoordinate(0, 0), shape);
            var robot = new OpponentAiRobotSnapshot(1, profile, 1, 5, 10, 1, RobotLocation.Board,
                new BoardCoordinate(0, 0), shape);
            var state = new OpponentAiSnapshot(BattleSide.Opponent, 0, board,
                new OpponentAiOfferSnapshot[0], new[] { robot }, new[] { profile });
            var action = new OpponentAiAction(OpponentAiActionKind.Move, sourceInstanceId: 1, anchor: new BoardCoordinate(1, 0));
            Assert.IsTrue(new OpponentAiRegionEvaluator().IsActionSafe(state, action));
            Assert.AreEqual("r1", board.GetOccupant(new BoardCoordinate(0, 0)));
            Assert.AreEqual("r1", state.Board.GetOccupant(new BoardCoordinate(0, 0)));
            Assert.IsFalse(board.IsOccupied(new BoardCoordinate(2, 0)));
        }

        [Test]
        public void ShopDeploymentCanCompleteSealWithStableSlotIdentity()
        {
            var state = RingState(3, true);
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions {
                AllowMove = false, AllowMerge = false, AllowRefresh = false });
            Assert.AreEqual(OpponentAiActionKind.PurchaseAndDeploy, action.Kind);
            Assert.AreEqual(21, action.SlotId, "SlotId不是数组索引");
            Assert.AreEqual(11, action.OfferId);
            Assert.AreEqual(new BoardCoordinate(4, 3), action.Anchor);
            Assert.AreEqual("一步封口", action.DecisionReason);
        }

        [Test]
        public void BoardMergeCannotRemoveWallOfEffectiveRobotEnclosure()
        {
            var state = RingState(4, false);
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions {
                AllowPurchase = false, AllowMove = false, AllowRefresh = false });
            Assert.AreEqual(OpponentAiActionKind.Wait, action.Kind);
        }

        private static OpponentAiSnapshot RingState(int count, bool offer)
        {
            var board = new BoardModel(7, 7);
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1, new PlaceableDefinition("p", 1, 1), 100);
            var robots = new List<OpponentAiRobotSnapshot>();
            var anchors = new[] { new BoardCoordinate(2, 3), new BoardCoordinate(3, 2), new BoardCoordinate(3, 4), new BoardCoordinate(4, 3) };
            for (var i = 0; i < count; i++)
            {
                var definition = new PlaceableDefinition("r" + i, 1, 1);
                board.TryPlace(anchors[i], definition);
                robots.Add(new OpponentAiRobotSnapshot(i + 1, profile, 1, 5, 10, 1, RobotLocation.Board, anchors[i], definition));
            }
            var world = new OpponentAiWorldSnapshot(new OpponentAiBallState(new Vector2(3.5f, 3.5f), Vector2.up, 2, .2f),
                new OpponentAiGlassState[0], phase: OpponentAiStrategyPhase.ComboControl);
            return new OpponentAiSnapshot(BattleSide.Opponent, offer ? 20 : 0, board,
                offer ? new[] { new OpponentAiOfferSnapshot(21, 11, profile, 1, 6, false) } : new OpponentAiOfferSnapshot[0],
                robots, new[] { profile }, world: world);
        }

        [Test]
        public void GlassExpansionDoesNotBuyMergeAfterThreeRobots()
        {
            var board = new BoardModel(5, 5);
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1f, new PlaceableDefinition("offer", 1, 1), 20);
            var robots = new List<OpponentAiRobotSnapshot>();
            for (var i = 0; i < 3; i++)
            {
                var definition = new PlaceableDefinition("r" + i, 1, 1);
                var anchor = new BoardCoordinate(i, 0);
                board.TryPlace(anchor, definition);
                robots.Add(new OpponentAiRobotSnapshot(i + 1, profile, 1, 5, 10, 1f, RobotLocation.Board, anchor, definition));
            }
            var world = new OpponentAiWorldSnapshot(new OpponentAiBallState(Vector2.zero, Vector2.up, 1, .2f, false),
                new[] { new OpponentAiGlassState(new BoardCoordinate(3, 3), 2, 1, 5) });
            var state = new OpponentAiSnapshot(BattleSide.Opponent, 20, board,
                new[] { new OpponentAiOfferSnapshot(19, 11, profile, 1, 6, false) }, robots, new[] { profile }, world: world);
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions { AllowRefresh = false });
            Assert.AreNotEqual(OpponentAiActionKind.PurchaseAndMerge, action.Kind, "前期不能因已有三个就把钱优先用于升级");
        }

        [Test]
        public void OneMoveClosesRegionAroundBallInsteadOfSeekingCentreExposure()
        {
            var board = new BoardModel(7, 7);
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1f, new PlaceableDefinition("p", 1, 1));
            var robots = new List<OpponentAiRobotSnapshot>();
            var anchors = new[] { new BoardCoordinate(2, 3), new BoardCoordinate(3, 2), new BoardCoordinate(3, 4), new BoardCoordinate(0, 0) };
            for (var i = 0; i < anchors.Length; i++)
            {
                var definition = new PlaceableDefinition("r" + i, 1, 1);
                board.TryPlace(anchors[i], definition);
                robots.Add(new OpponentAiRobotSnapshot(i + 1, profile, 1, 5, 10, 1f, RobotLocation.Board, anchors[i], definition));
            }
            var world = new OpponentAiWorldSnapshot(new OpponentAiBallState(new Vector2(3.5f, 3.5f), Vector2.up, 2, .2f),
                new OpponentAiGlassState[0], phase: OpponentAiStrategyPhase.ComboControl);
            var state = new OpponentAiSnapshot(BattleSide.Opponent, 0, board, new OpponentAiOfferSnapshot[0], robots,
                new[] { profile }, world: world);
            var action = OpponentAiPlanner.Plan(state, new OpponentAiPlanningOptions { AllowRefresh = false, AllowMerge = false });
            Assert.AreEqual(OpponentAiActionKind.Move, action.Kind);
            Assert.AreEqual(4, action.SourceInstanceId);
            Assert.AreEqual(new BoardCoordinate(4, 3), action.Anchor);
        }

        [Test]
        public void SnapshotKeepsBallOnlyTrackAndGlassTerrainLayers()
        {
            var map = MapData.Create(3, 3);
            var track = new BoardCoordinate(1, 1);
            var glass = new BoardCoordinate(2, 1);
            map.SetBaseTerrain(track, BaseTerrainType.Track);
            map.SetObstacle(glass, ObstacleType.GlassNormal);
            var board = new BoardModel(map);
            var snapshot = new OpponentAiSnapshot(BattleSide.Opponent, 0, board,
                new OpponentAiOfferSnapshot[0], new OpponentAiRobotSnapshot[0], new OpponentAiRobotProfile[0]);
            Assert.IsTrue(snapshot.Board.CanEnergyBallPass(track), "轨道可通球，不能丢失地形层");
            Assert.IsFalse(snapshot.Board.CanPlaceUnit(track));
            Assert.IsTrue(snapshot.Board.IsTerrainMap);
            Assert.AreEqual(ObstacleType.GlassNormal, snapshot.Board.GetObstacle(glass));
        }
    }
}
