using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class OpponentAiRegionTests
    {
        private readonly OpponentAiRegionEvaluator _evaluator = new OpponentAiRegionEvaluator();

        [Test]
        public void CoarseFloodPassesTrackButDeploymentDoesNot()
        {
            var map = MapData.Create(3, 1);
            map.SetBaseTerrain(new BoardCoordinate(1, 0), BaseTerrainType.Track);
            var state = State(new BoardModel(map), new Vector2(.5f, .5f));
            var region = _evaluator.Evaluate(state, state.Board, state.Robots);
            Assert.IsTrue(region.Valid);
            Assert.AreEqual(3, region.Area);
            Assert.IsTrue(region.Reachable[2]);
            Assert.IsFalse(_evaluator.IsActionSafe(WithOffer(state), Deploy(1, 0), false));
        }

        [Test]
        public void ExternalWallsAloneAreNotAClosedRobotRegion()
        {
            var state = State(new BoardModel(5, 5), new Vector2(2.5f, 2.5f));
            Assert.IsFalse(_evaluator.Evaluate(state, state.Board, state.Robots).IsClosed);
            Assert.IsFalse(_evaluator.Evaluate(state, state.Board, state.Robots, true).IsClosed);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RobotsUsingTheBoardCornerCannotEarnEnclosureReward(bool precise)
        {
            var robots = new[] { Robot(1, 1, 0), Robot(2, 0, 1) };
            var state = State(new BoardModel(5, 5), new Vector2(.5f, .5f), robots, radius: .2f);
            var result = _evaluator.Evaluate(state, state.Board, state.Robots, precise);
            Assert.IsTrue(result.Valid);
            Assert.IsFalse(result.IsClosed, "两台机器人借助棋盘两边围出的角落不能认定为封口");
            Assert.AreEqual(0f, result.Control, "边界小空间不能获得紧凑围挡分");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InteriorGlassRingStillEarnsSecondaryEnclosureReward(bool precise)
        {
            var glass = new[] {
                new OpponentAiGlassState(new BoardCoordinate(2, 3), 2, 1, 10),
                new OpponentAiGlassState(new BoardCoordinate(3, 2), 2, 1, 10),
                new OpponentAiGlassState(new BoardCoordinate(3, 4), 2, 1, 10),
                new OpponentAiGlassState(new BoardCoordinate(4, 3), 2, 1, 10)
            };
            var state = State(new BoardModel(7, 7), new Vector2(3.5f, 3.5f), glass: glass, radius: .2f);
            var result = _evaluator.Evaluate(state, state.Board, state.Robots, precise);
            Assert.IsTrue(result.Valid);
            Assert.IsTrue(result.IsClosed, "内部玻璃围住球属于次级围挡");
            Assert.AreEqual(OpponentAiEnclosureKind.Glass, result.Enclosure);
            Assert.Greater(result.Control, 0f);
        }

        [Test]
        public void FourInsetRobotsCloseTheCenterDespiteDiagonalFootprintGaps()
        {
            var robots = new[] { Robot(1, 2, 3), Robot(2, 3, 2), Robot(3, 3, 4), Robot(4, 4, 3) };
            var state = State(new BoardModel(7, 7), new Vector2(3.5f, 3.5f), robots, radius: .2f);
            var result = _evaluator.Evaluate(state, state.Board, state.Robots, true);
            Assert.IsTrue(result.Valid);
            Assert.IsTrue(result.GeometryComplete);
            Assert.IsTrue(result.IsClosed);
            Assert.AreEqual(OpponentAiEnclosureKind.Robots, result.Enclosure);
            Assert.IsTrue(result.Contains(new Vector2(3.5f, 3.5f)));
            Assert.IsFalse(result.Contains(new Vector2(5.5f, 5.5f)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GlassInsideAnIndependentRobotRingDoesNotDowngradeIt(bool precise)
        {
            var cells = new List<BoardCoordinate>();
            for (var y = 0; y < 7; y++)
                for (var x = 0; x < 7; x++)
                    if (x == 0 || y == 0 || x == 6 || y == 6) cells.Add(new BoardCoordinate(x, y));
            var definition = new PlaceableDefinition("large-ring", cells);
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1, definition);
            var robot = new OpponentAiRobotSnapshot(1, profile, 1, 5, 10, 1,
                RobotLocation.Board, new BoardCoordinate(1, 1), definition);
            var glass = new[] { new OpponentAiGlassState(new BoardCoordinate(4, 3), 2, 1, 10) };
            var state = State(new BoardModel(9, 9), new Vector2(3.5f, 3.5f), new[] { robot }, glass, radius: .2f);
            var result = _evaluator.Evaluate(state, state.Board, state.Robots, precise);
            Assert.IsTrue(result.IsClosed);
            Assert.AreEqual(OpponentAiEnclosureKind.Robots, result.Enclosure);
            Assert.Greater(result.GlassValue, 0f, "真实玻璃接触收益与机器人独立成环判断同时保留");
            Assert.IsFalse(result.Contains(new Vector2(4.5f, 3.5f)), "虚拟证明不得覆盖真实玻璃碰撞区域");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GlassAndRobotUsingTheBoardCornerHaveNoEnclosureReward(bool precise)
        {
            var state = State(new BoardModel(5, 5), new Vector2(.5f, .5f), new[] { Robot(1, 0, 1) },
                new[] { new OpponentAiGlassState(new BoardCoordinate(1, 0), 2, 1, 10) }, radius: .2f);
            var result = _evaluator.Evaluate(state, state.Board, state.Robots, precise);
            Assert.AreEqual(OpponentAiEnclosureKind.None, result.Enclosure);
            Assert.AreEqual(0f, result.Control);
            Assert.Greater(result.GlassValue, 0f, "正常破玻璃收益不受围挡分类影响");
        }

        [Test]
        public void UnlaunchedAndMissingWorldRemainValidForSceneTransactions()
        {
            var state = State(new BoardModel(5, 5), Vector2.zero, launched: false);
            Assert.IsTrue(_evaluator.Evaluate(state, state.Board, state.Robots, true).Valid);
            Assert.IsTrue(_evaluator.IsActionSafe(WithOffer(state), Deploy(2, 2)));
            var missing = new OpponentAiSnapshot(BattleSide.Opponent, 20, state.Board,
                state.Offers, state.Robots, state.CandidateProfiles);
            Assert.IsTrue(_evaluator.Evaluate(missing, missing.Board, missing.Robots, true).Valid);
            Assert.IsTrue(_evaluator.IsActionSafe(WithOffer(missing), Deploy(2, 2)));
        }

        [Test]
        public void OnlyReachableGlassFacesContributeRewardPerRemainingHit()
        {
            var map = MapData.Create(5, 3);
            for (var y = 0; y < 3; y++) map.SetObstacle(new BoardCoordinate(2, y), ObstacleType.GlassNormal);
            map.SetObstacle(new BoardCoordinate(4, 1), ObstacleType.GlassNormal);
            var glass = new[] {
                new OpponentAiGlassState(new BoardCoordinate(2, 0), 4, 2, 10),
                new OpponentAiGlassState(new BoardCoordinate(2, 1), 4, 2, 10),
                new OpponentAiGlassState(new BoardCoordinate(2, 2), 4, 2, 10),
                new OpponentAiGlassState(new BoardCoordinate(4, 1), 1, 1, 10000)
            };
            var state = State(new BoardModel(map), new Vector2(.5f, 1.5f), glass: glass);
            var result = _evaluator.Evaluate(state, state.Board, state.Robots);
            Assert.AreEqual(6, result.Area);
            Assert.AreEqual(15f, result.GlassValue, .001f);
        }

        [Test]
        public void ReserveNeedsARealExistingShapeAndCannotReleaseItFirst()
        {
            var robot = Robot(1, 0, 0, 2, 1);
            var state = State(new BoardModel(3, 1), Vector2.zero, new[] { robot }, launched: false);
            var result = _evaluator.Evaluate(state, state.Board, state.Robots);
            Assert.IsFalse(result.HasReserve, "一个空格不能容纳场上的二连格机器人");
            var larger = State(new BoardModel(4, 1), Vector2.zero, new[] { robot }, launched: false);
            Assert.IsTrue(_evaluator.Evaluate(larger, larger.Board, larger.Robots).HasReserve);
        }

        [Test]
        public void CoarseRobotValueUsesDamageIntervalAndReachableArea()
        {
            var robot = Robot(1, 2, 2);
            var state = State(new BoardModel(5, 5), new Vector2(.5f, .5f), new[] { robot });
            var result = _evaluator.Evaluate(state, state.Board, state.Robots);
            Assert.AreEqual(24, result.Area);
            Assert.AreEqual(40f / 24f, result.RobotValue, .001f);
        }

        [TestCase(7)]
        [TestCase(64)]
        public void ActualThinPolygonDoesNotBecomeItsWholeFootprint(int boardSize)
        {
            var robots = new[] { Robot(1, 2, 3), Robot(2, 3, 2), Robot(3, 3, 4), Robot(4, 4, 3) };
            var shape = new OpponentAiColliderSnapshot(robots[3].Definition.Id, robots[3].Anchor.Value,
                new[] { Rect(4.45f, 3.45f, 4.55f, 3.55f) });
            var state = State(new BoardModel(boardSize, boardSize), new Vector2(3.5f, 3.5f), robots,
                colliders: new[] { shape }, radius: .2f);
            var coarse = _evaluator.Evaluate(state, state.Board, state.Robots);
            var precise = _evaluator.Evaluate(state, state.Board, state.Robots, true);
            Assert.IsTrue(coarse.IsClosed, "粗筛会把细碰撞轮廓视为整格机器人，必须依靠精检纠正");
            Assert.IsTrue(precise.Valid);
            Assert.IsFalse(precise.IsClosed);
            Assert.AreEqual(0f, precise.Control, "已证明能通到外沿的区域不能保留粗筛围挡分");
            Assert.IsTrue(precise.Contains(new Vector2(5.5f, 3.5f)));
        }

        [Test]
        public void MovedRobotTranslatesCapturedPolygonByAnchorDelta()
        {
            var original = Robot(1, 2, 1);
            var moved = new OpponentAiRobotSnapshot(1, original.Profile, 1, 5, 10, 1f,
                RobotLocation.Board, new BoardCoordinate(3, 1), original.Definition);
            var collider = new OpponentAiColliderSnapshot(original.Definition.Id, original.Anchor.Value,
                new[] { Rect(2.2f, 1.2f, 2.8f, 1.8f) });
            var state = State(new BoardModel(5, 4), new Vector2(.5f, .5f), new[] { original },
                colliders: new[] { collider }, radius: .1f);
            var virtualBoard = new BoardModel(5, 4);
            virtualBoard.TryPlace(moved.Anchor.Value, moved.Definition);
            var result = _evaluator.Evaluate(state, virtualBoard, new[] { moved }, true);
            Assert.IsTrue(result.Valid);
            Assert.IsTrue(result.Contains(new Vector2(2.5f, 1.5f)));
            Assert.IsFalse(result.Contains(new Vector2(3.5f, 1.5f)));
        }

        [Test]
        public void FootprintFallbackDoesNotInventASeamBetweenAdjacentCells()
        {
            var robot = Robot(1, 1, 1, 2, 1);
            var state = State(new BoardModel(5, 4), new Vector2(2f, 1.5f), new[] { robot }, radius: .01f);
            Assert.IsFalse(_evaluator.Evaluate(state, state.Board, state.Robots, true).Valid);
        }

        [Test]
        public void ConcaveFallbackOffsetsTheOutlineInsteadOfLeavingACornerProtrusion()
        {
            var definition = new PlaceableDefinition("L", new[] {
                new BoardCoordinate(0, 0), new BoardCoordinate(1, 0), new BoardCoordinate(0, 1)
            });
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1f, definition);
            var robot = new OpponentAiRobotSnapshot(1, profile, 1, 5, 10, 1f,
                RobotLocation.Board, new BoardCoordinate(1, 1), definition);
            var point = new Vector2(1.98f, 1.98f);
            var state = State(new BoardModel(5, 4), point, new[] { robot }, radius: .01f);
            var result = _evaluator.Evaluate(state, state.Board, state.Robots, true);
            Assert.IsTrue(result.Valid);
            Assert.IsTrue(result.Contains(point), "L形凹角应由(2,2)内缩到(1.94,1.94)");
        }

        [Test]
        public void FallbackRingPreservesItsInteriorHole()
        {
            var cells = new List<BoardCoordinate>();
            for (var y = 0; y < 3; y++)
                for (var x = 0; x < 3; x++)
                    if (x != 1 || y != 1) cells.Add(new BoardCoordinate(x, y));
            var definition = new PlaceableDefinition("ring", cells);
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1f, definition);
            var robot = new OpponentAiRobotSnapshot(1, profile, 1, 5, 10, 1f,
                RobotLocation.Board, new BoardCoordinate(1, 1), definition);
            var state = State(new BoardModel(7, 7), new Vector2(2.5f, 2.5f), new[] { robot }, radius: .2f);
            var result = _evaluator.Evaluate(state, state.Board, state.Robots, true);
            Assert.IsTrue(result.Valid);
            Assert.IsTrue(result.IsClosed);
            Assert.IsTrue(result.Contains(new Vector2(2.5f, 2.5f)));
        }

        [Test]
        public void SeparateFallbackRobotsKeepTheirRealInsetGap()
        {
            var robots = new[] { Robot(1, 2, 0, 1, 2), Robot(2, 2, 2, 1, 3) };
            var state = State(new BoardModel(5, 5), new Vector2(1.5f, 2f), robots, radius: .04f);
            var region = _evaluator.Evaluate(state, state.Board, state.Robots, true);
            Assert.IsTrue(region.Valid);
            Assert.IsFalse(region.IsClosed);
            Assert.IsTrue(region.Contains(new Vector2(3.5f, 2f)));
        }

        [Test]
        public void SubSamplePositiveGapCannotBeCertifiedClosed()
        {
            var robot = Robot(1, 2, 0, 1, 5);
            var collider = new OpponentAiColliderSnapshot(robot.Definition.Id, robot.Anchor.Value,
                new[] { Rect(2f, 0f, 3f, 2.331f), Rect(2f, 2.632f, 3f, 5f) });
            var state = State(new BoardModel(5, 5), new Vector2(1.5f, 2f), new[] { robot },
                colliders: new[] { collider }, radius: .15f);
            Assert.IsFalse(_evaluator.Evaluate(state, state.Board, state.Robots, true).IsClosed,
                "宽 .301 的缝可通过直径 .30 的球，不能因采样未命中而报封口");
        }

        [Test]
        public void DiagonalRobotCornersAreSweptInsteadOfConnectingFreeEndpoints()
        {
            var robots = new[] { Robot(1, 1, 0), Robot(2, 0, 1) };
            var state = State(new BoardModel(5, 5), new Vector2(.5f, .5f), robots, radius: .15f);
            var result = _evaluator.Evaluate(state, state.Board, state.Robots, true);
            Assert.IsTrue(result.Valid);
            Assert.IsFalse(result.Contains(new Vector2(1.5f, 1.5f)));
            Assert.IsFalse(result.IsClosed, "扫掠仍能区分两侧，但借助棋盘角落不算有效围挡");
        }

        [Test]
        public void BerserkCurrentRegionPassesRobotsButNormalLayoutIsProtected()
        {
            var robot = Robot(1, 2, 0, 1, 5);
            var state = State(new BoardModel(7, 5), new Vector2(1.5f, 2.5f), new[] { robot }, berserk: true);
            var result = _evaluator.Evaluate(state, state.Board, state.Robots, true);
            Assert.IsFalse(result.IsClosed);
            Assert.IsTrue(result.Contains(new Vector2(4.5f, 2.5f)));
            Assert.IsFalse(_evaluator.IsActionSafe(state, new OpponentAiAction(OpponentAiActionKind.Move,
                sourceInstanceId: 1, anchor: new BoardCoordinate(5, 0))));
        }

        [Test]
        public void TrappedRecoveryCannotEarnAClosedRegion()
        {
            var robot = Robot(1, 2, 0, 1, 5);
            var state = State(new BoardModel(7, 5), new Vector2(1.5f, 2.5f), new[] { robot },
                recovery: PlacementRecoveryState.Trapped);
            Assert.IsFalse(_evaluator.Evaluate(state, state.Board, state.Robots, true).IsClosed);
        }

        [Test]
        public void DeploymentRejectsCurrentBallCoverageEvenDuringBerserk()
        {
            var state = WithOffer(State(new BoardModel(5, 5), new Vector2(2.5f, 2.5f), berserk: true));
            Assert.IsFalse(_evaluator.IsActionSafe(state, Deploy(2, 2), false));
        }

        [Test]
        public void DeploymentRejectsBallSweptBeforeTheFirstCollision()
        {
            var state = WithOffer(State(new BoardModel(7, 5), new Vector2(1.5f, 2.5f),
                direction: Vector2.right, speed: 20f));
            Assert.IsFalse(_evaluator.IsActionSafe(state, Deploy(3, 2), false));
            Assert.IsTrue(_evaluator.IsActionSafe(state, Deploy(5, 2), false));
        }

        [Test]
        public void MovingSourceCannotUseItsRemovedOldColliderToTruncatePrediction()
        {
            var state = State(new BoardModel(7, 5), new Vector2(.5f, 2.5f),
                new[] { Robot(1, 1, 2) }, direction: Vector2.right, speed: 30f);
            Assert.IsFalse(_evaluator.IsActionSafe(state, new OpponentAiAction(OpponentAiActionKind.Move,
                sourceInstanceId: 1, anchor: new BoardCoordinate(3, 2)), false));
        }

        [Test]
        public void SafetyUsesConfiguredPredictionSeconds()
        {
            var state = WithOffer(State(new BoardModel(7, 5), new Vector2(1.5f, 2.5f),
                direction: Vector2.right, speed: 20f));
            var world = new OpponentAiWorldSnapshot(state.World.Ball, state.World.Glass,
                settings: new OpponentAiStrategySettings { PredictionSeconds = .02f });
            Assert.IsTrue(_evaluator.IsActionSafe(state.WithWorld(world), Deploy(3, 2), false));
            Assert.IsFalse(_evaluator.IsActionSafe(state, Deploy(3, 2), false));
        }

        [Test]
        public void LoadedPurchasePolygonMatchesItsOfferDefinitionId()
        {
            var state = WithOffer(State(new BoardModel(5, 5), new Vector2(2.1f, 2.5f), radius: .1f));
            var collider = new OpponentAiColliderSnapshot("ai:offer:11", new BoardCoordinate(2, 2),
                new[] { Rect(2.7f, 2.1f, 2.9f, 2.9f) });
            var world = new OpponentAiWorldSnapshot(state.World.Ball, state.World.Glass, new[] { collider });
            Assert.IsTrue(_evaluator.IsActionSafe(state.WithWorld(world), Deploy(2, 2), false));
        }

        [Test]
        public void SweepStopsAtExistingGlassAndDoesNotPredictThroughIt()
        {
            var map = MapData.Create(7, 5);
            map.SetObstacle(new BoardCoordinate(2, 2), ObstacleType.GlassNormal);
            var state = WithOffer(State(new BoardModel(map), new Vector2(1.5f, 2.5f),
                direction: Vector2.right, speed: 30f));
            Assert.IsTrue(_evaluator.IsActionSafe(state, Deploy(4, 2), false));
        }

        [Test]
        public void MandatoryBounceDoesNotLookLikeLeavingAnExistingEnclosure()
        {
            var robot = Robot(1, 2, 0, 1, 5);
            var state = WithOffer(State(new BoardModel(7, 5), new Vector2(1.5f, 2.5f), new[] { robot },
                direction: Vector2.right, speed: 30f));
            Assert.IsTrue(_evaluator.IsActionSafe(state, Deploy(5, 2)));
        }

        [Test]
        public void DeploymentValidityAndFundsAreCheckedWithoutMutatingSnapshot()
        {
            var state = WithOffer(State(new BoardModel(5, 5), new Vector2(.5f, .5f)));
            Assert.IsTrue(_evaluator.IsActionSafe(state, Deploy(3, 3), false));
            Assert.IsFalse(state.Board.IsOccupied(new BoardCoordinate(3, 3)));
            Assert.IsFalse(_evaluator.IsActionSafe(state, Deploy(5, 3), false));
            var poor = new OpponentAiSnapshot(BattleSide.Opponent, 0, state.Board, state.Offers,
                state.Robots, state.CandidateProfiles, world: state.World);
            Assert.IsFalse(_evaluator.IsActionSafe(poor, Deploy(3, 3), false));
        }

        [Test]
        public void RemovingTheOnlyEnclosureWallByMergeIsUnsafe()
        {
            var robots = new[] { Robot(1, 2, 3), Robot(2, 3, 2), Robot(3, 3, 4), Robot(4, 4, 3) };
            var state = State(new BoardModel(7, 7), new Vector2(3.5f, 3.5f), robots, radius: .2f);
            Assert.IsFalse(_evaluator.IsActionSafe(state, new OpponentAiAction(OpponentAiActionKind.Merge,
                sourceInstanceId: 1, targetInstanceId: 2)));
        }

        [Test]
        public void WithoutBallGlassProximityStillDistinguishesLayouts()
        {
            var glass = new[] { new OpponentAiGlassState(new BoardCoordinate(4, 2), 4, 1, 20) };
            var near = State(new BoardModel(7, 5), Vector2.zero, new[] { Robot(1, 3, 2) },
                glass: glass, launched: false);
            var far = State(new BoardModel(7, 5), Vector2.zero, new[] { Robot(1, 0, 0) },
                glass: glass, launched: false);
            Assert.Greater(_evaluator.Evaluate(near, near.Board, near.Robots).GlassValue,
                _evaluator.Evaluate(far, far.Board, far.Robots).GlassValue);
        }

        [Test]
        public void LargeTinyRadiusGridReturnsUnconfirmedInsteadOfAnExpensiveFalseClosure()
        {
            var state = State(new BoardModel(64, 64), new Vector2(.5f, .5f), radius: .01f);
            Assert.IsTrue(state.World.Ball.IsLaunched);
            Assert.AreEqual(.01f, state.World.Ball.Radius);
            var result = _evaluator.Evaluate(state, state.Board, state.Robots, true);
            Assert.IsFalse(result.IsClosed);
            Assert.IsTrue(result.Valid);
            Assert.IsFalse(result.GeometryComplete, "超预算不能冒充完整区域，samples=" + result.SampleCount);
            Assert.LessOrEqual(result.SampleCount, OpponentAiRegionEvaluator.MAX_PRECISE_SAMPLES);
        }

        [Test]
        public void LargeOpenBoardAllowsSafeMovementDespiteFineSampleBudget()
        {
            var state = State(new BoardModel(64, 64), new Vector2(.5f, .5f),
                new[] { Robot(1, 32, 32) }, radius: .01f);
            Assert.IsTrue(_evaluator.IsActionSafe(state, new OpponentAiAction(OpponentAiActionKind.Move,
                sourceInstanceId: 1, anchor: new BoardCoordinate(40, 40))));
        }

        [Test]
        public void LargeBoardOpenCertificateCannotReleaseALocalEnclosure()
        {
            var robots = new[] { Robot(1, 2, 3), Robot(2, 3, 2), Robot(3, 3, 4), Robot(4, 4, 3) };
            var state = State(new BoardModel(64, 64), new Vector2(3.5f, 3.5f), robots, radius: .2f);
            Assert.IsFalse(_evaluator.IsActionSafe(state, new OpponentAiAction(OpponentAiActionKind.Move,
                sourceInstanceId: 1, anchor: new BoardCoordinate(40, 40))));
        }

        [Test]
        public void LargeRobotRingIsNotOpenMerelyBecauseItOccupiesMostOfTheBoard()
        {
            var cells = new List<BoardCoordinate>();
            for (var y = 0; y < 62; y++)
                for (var x = 0; x < 62; x++)
                    if (x == 0 || y == 0 || x == 61 || y == 61) cells.Add(new BoardCoordinate(x, y));
            var definition = new PlaceableDefinition("huge-ring", cells);
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1, definition);
            var robot = new OpponentAiRobotSnapshot(1, profile, 1, 5, 10, 1,
                RobotLocation.Board, new BoardCoordinate(1, 1), definition);
            var state = State(new BoardModel(64, 64), new Vector2(32.5f, 32.5f), new[] { robot }, radius: .5f);
            Assert.IsFalse(_evaluator.IsActionSafe(state, new OpponentAiAction(OpponentAiActionKind.Move,
                sourceInstanceId: 1, anchor: new BoardCoordinate(0, 1))),
                "大环不能被面积占比捷径当成开放区，证明未完成时应保守拒绝拆动");
        }

        private static OpponentAiSnapshot WithOffer(OpponentAiSnapshot state)
        {
            var profile = Robot(99, 0, 0).Profile;
            return new OpponentAiSnapshot(BattleSide.Opponent, 20, state.Board,
                new[] { new OpponentAiOfferSnapshot(0, 11, profile, 1, 6, false) },
                state.Robots, new[] { profile }, world: state.World);
        }

        private static OpponentAiAction Deploy(int x, int y) => new OpponentAiAction(
            OpponentAiActionKind.PurchaseAndDeploy, slotId: 0, offerId: 11, anchor: new BoardCoordinate(x, y));

        private static OpponentAiRobotSnapshot Robot(int id, int x, int y, int width = 1, int height = 1)
        {
            var definition = new PlaceableDefinition("robot-" + id, width, height);
            var profile = new OpponentAiRobotProfile(501, 1, 10, 1f, definition, 10);
            return new OpponentAiRobotSnapshot(id, profile, 1, 5, 10, 1f,
                RobotLocation.Board, new BoardCoordinate(x, y), definition);
        }

        private static OpponentAiSnapshot State(BoardModel board, Vector2 ball,
            IReadOnlyList<OpponentAiRobotSnapshot> robots = null,
            IReadOnlyList<OpponentAiGlassState> glass = null,
            IReadOnlyList<OpponentAiColliderSnapshot> colliders = null, float radius = .15f,
            bool launched = true, bool berserk = false, PlacementRecoveryState recovery = PlacementRecoveryState.None,
            Vector2 direction = default(Vector2), float speed = 0f)
        {
            robots = robots ?? Array.Empty<OpponentAiRobotSnapshot>();
            foreach (var robot in robots) if (robot.IsOnBoard) board.TryPlace(robot.Anchor.Value, robot.Definition);
            var world = new OpponentAiWorldSnapshot(new OpponentAiBallState(ball, direction, speed, radius,
                launched, berserk, recoveryState: recovery), glass, colliders);
            return new OpponentAiSnapshot(BattleSide.Opponent, 20, board,
                Array.Empty<OpponentAiOfferSnapshot>(), robots, Array.Empty<OpponentAiRobotProfile>(), world: world);
        }

        private static Vector2[] Rect(float x0, float y0, float x1, float y1) => new[] {
            new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)
        };
    }
}
