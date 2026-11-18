using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>纯数据入口。运行时使用后台步进搜索；同步入口供工具和小棋盘测试使用。</summary>
    public static class OpponentAiPlanner
    {
        public static OpponentAiSearchSession BeginSearch(OpponentAiSnapshot snapshot, OpponentAiPlanningOptions options = null)
            => new OpponentAiSearchSession(snapshot, options ?? new OpponentAiPlanningOptions());

        public static OpponentAiAction Plan(OpponentAiSnapshot snapshot, OpponentAiPlanningOptions options = null)
        {
            using (var search = BeginSearch(snapshot, options))
            {
                while (!search.IsComplete) search.Advance(256, double.PositiveInfinity);
                return search.BestAction;
            }
        }

        public static bool CanRefresh(OpponentAiSnapshot state)
        {
            if (state == null || state.Side != BattleSide.Opponent || state.HasPurchasedShopRobots) return false;
            foreach (var offer in state.Offers) if (offer.IsPurchased) return false;
            var board = state.Board;
            foreach (var candidate in state.CandidateProfiles)
            {
                if ((long)state.RefreshPrice + candidate.LevelOnePrice > state.EnergyCoins) continue;
                if (HasPlacement(board, candidate.Definition)) return true;
                if (state.StrategyPhase != OpponentAiStrategyPhase.ComboControl) continue;
                foreach (var robot in state.Robots)
                    if (robot.IsOnBoard && robot.Profile.RobotId == candidate.RobotId && robot.Level == 1 &&
                        robot.Level < robot.MaxLevel && robot.Profile.DamagePerLevel > 0) return true;
            }
            return false;
        }

        /// <summary>资源加载后重新证明动作的玩法收益；只验证，不沿用计划时的旧分数。</summary>
        public static bool IsActionUseful(OpponentAiSnapshot state, OpponentAiAction action, OpponentAiRegionEvaluator evaluator = null,
            OpponentAiRegionEvaluation beforeRegion = null, OpponentAiRegionEvaluation afterRegion = null)
            => IsActionUseful(state, action, evaluator, beforeRegion, afterRegion, out _);

        internal static bool IsActionUseful(OpponentAiSnapshot state, OpponentAiAction action, OpponentAiRegionEvaluator evaluator,
            OpponentAiRegionEvaluation beforeRegion, OpponentAiRegionEvaluation afterRegion,
            out OpponentAiRegionEvaluation finalRegion)
        {
            finalRegion = null;
            if (state == null) return false;
            if (!OpponentAiArmyRules.Allows(state, action)) return false;
            if (action.DecisionReason == null && action.MinimumEnclosure == OpponentAiEnclosureKind.None)
                return true; // 兼容未指定围挡目标的工具入口。
            evaluator = evaluator ?? new OpponentAiRegionEvaluator();
            BoardModel board; List<OpponentAiRobotSnapshot> robots; int cost;
            if (!TrySimulate(state, action, out board, out robots, out cost)) return false;
            if (action.FollowUp.HasValue)
            {
                var next = AfterSimulation(state, action, board, robots, cost);
                var follow = action.FollowUp.Value.ToAction();
                if (!evaluator.IsActionSafe(next, follow)) return false;
                if (!TrySimulate(next, follow, out board, out robots, out cost)) return false;
                afterRegion = null; // 两步收益比较的是最终布局，而非第一步布局。
            }
            var before = beforeRegion ?? evaluator.Evaluate(state, state.Board, state.Robots, true);
            var after = afterRegion ?? evaluator.Evaluate(state, board, robots, true);
            finalRegion = after; // 保留精确最终区域，主线程可检验球是否已离开两步目标。
            if (!before.Valid || !after.Valid) return false;
            var minimumEnclosure = action.FollowUp?.MinimumEnclosure ?? action.MinimumEnclosure;
            if (after.Enclosure < minimumEnclosure) return false;
            // 扩军购买不要求即时评分增长；安全性由独立的IsActionSafe/提交证明保证。
            if (!action.FollowUp.HasValue && OpponentAiArmyRules.NeedsRecruitment(state) &&
                OpponentAiArmyRules.AddsRobot(action) && after.HasReserve) return true;
            var close = after.ImprovesEnclosure(before.Enclosure, before.Control);
            if (action.DecisionReason == "一步封口") return close;
            if (action.FollowUp.HasValue && action.FollowUp.Value.RequireClosed && !close) return false;
            if (!before.HasReserve && after.HasReserve) return true;
            var precise = before.GeometryComplete && after.GeometryComplete;
            if (action.ScoreReference.HasValue && action.ScoreReference.Value.Precise && !precise) return false;
            if (action.ScoreReference.HasValue ? !action.ScoreReference.Value.Precise : !precise)
            {
                var coarseBefore = evaluator.Evaluate(state, state.Board, state.Robots);
                var coarseAfter = evaluator.Evaluate(state, board, robots);
                coarseBefore.UseVerifiedEnclosure(before);
                coarseAfter.UseVerifiedEnclosure(after);
                before = coarseBefore;
                after = coarseAfter;
            }
            if (action.Kind == OpponentAiActionKind.Merge || action.Kind == OpponentAiActionKind.PurchaseAndMerge)
                return state.StrategyPhase == OpponentAiStrategyPhase.ComboControl && after.RobotValue + .0001f >= before.RobotValue;
            if (!after.HasReserve && !close) return false;
            if (close) return true;
            var tuning = state.World?.Settings ?? new OpponentAiStrategySettings();
            var weights = state.StrategyPhase == OpponentAiStrategyPhase.ComboControl ? tuning.ComboWeights : tuning.GlassWeights;
            var reference = action.ScoreReference ?? OpponentAiScoreReference.ForPair(before, after, precise);
            var gain = reference.Score(after, weights) - reference.Score(before, weights);
            return gain >= (action.DecisionReason == "完成两步调整" ? .0001f : tuning.MinimumScoreGain);
        }

        internal static bool HasPlacement(BoardModel board, PlaceableDefinition shape)
        {
            for (var row = 0; row <= board.Rows - shape.Height; row++)
                for (var column = 0; column <= board.Columns - shape.Width; column++)
                    if (board.Evaluate(new BoardCoordinate(column, row), shape).IsValid) return true;
            return false;
        }

        internal static OpponentAiSnapshot AfterSimulation(OpponentAiSnapshot state, OpponentAiAction action,
            BoardModel board, List<OpponentAiRobotSnapshot> robots, int cost)
        {
            var offers = new List<OpponentAiOfferSnapshot>();
            foreach (var offer in state.Offers) if (offer.OfferId != action.OfferId) offers.Add(offer);
            var all = new List<OpponentAiRobotSnapshot>(robots);
            foreach (var owned in state.Robots)
            {
                if (!owned.IsOnBoard && owned.InstanceId != action.SourceInstanceId) all.Add(owned);
                if (action.Kind == OpponentAiActionKind.ReturnToShop && owned.InstanceId == action.SourceInstanceId)
                    all.Add(new OpponentAiRobotSnapshot(owned.InstanceId, owned.Profile, owned.Level, owned.MaxLevel,
                        owned.Damage, owned.TriggerInterval, RobotLocation.PurchasedShopSlot, null, owned.Definition));
            }
            var slots = new List<int>(state.ReturnSlotIds);
            if (action.Kind == OpponentAiActionKind.ReturnToShop) slots.Remove(action.SlotId);
            if (action.Kind == OpponentAiActionKind.PurchaseAndDeploy && !slots.Contains(action.SlotId)) slots.Add(action.SlotId);
            var next = new OpponentAiSnapshot(state.Side, state.EnergyCoins - cost, board, offers, all,
                state.CandidateProfiles, state.RefreshPrice, state.BattleId,
                state.HasPurchasedShopRobots || action.Kind == OpponentAiActionKind.ReturnToShop,
                state.World, slots, state.Army);
            return state.Army.HasValue ? next.WithArmyState(state.Army.Value.Established) : next;
        }

        internal static bool TrySimulate(OpponentAiSnapshot state, OpponentAiAction action, out BoardModel board,
            out List<OpponentAiRobotSnapshot> robots, out int cost)
        {
            board = state.Board; robots = new List<OpponentAiRobotSnapshot>(); cost = 0;
            if (!OpponentAiArmyRules.Allows(state, action)) return false;
            foreach (var robot in state.Robots) if (robot.IsOnBoard) robots.Add(robot);
            if (action.Kind == OpponentAiActionKind.DeployOwned)
            {
                foreach (var owned in state.Robots)
                {
                    if (owned.InstanceId != action.SourceInstanceId || owned.Location != RobotLocation.PurchasedShopSlot) continue;
                    if (!board.TryPlace(action.Anchor, owned.Definition)) return false;
                    robots.Add(Changed(owned, action.Anchor, owned.Level, owned.Damage));
                    return true;
                }
                return false;
            }
            if (action.Kind == OpponentAiActionKind.PurchaseAndDeploy)
            {
                foreach (var offer in state.Offers)
                {
                    if (offer.SlotId != action.SlotId || offer.OfferId != action.OfferId || offer.IsPurchased || offer.Price > state.EnergyCoins) continue;
                    var definition = new PlaceableDefinition("ai:offer:" + offer.OfferId, offer.Profile.Definition.Cells);
                    if (!board.TryPlace(action.Anchor, definition)) return false;
                    cost = offer.Price;
                    robots.Add(new OpponentAiRobotSnapshot(-1, offer.Profile, offer.Level, 5,
                        offer.Profile.DamageAt(offer.Level), offer.Profile.TriggerInterval, RobotLocation.Board, action.Anchor, definition));
                    return true;
                }
                return false;
            }
            var sourceIndex = robots.FindIndex(r => r.InstanceId == action.SourceInstanceId);
            if (action.Kind == OpponentAiActionKind.ReturnToShop)
            {
                if (sourceIndex < 0 || !ContainsSlot(state.ReturnSlotIds, action.SlotId)) return false;
                board.Release(robots[sourceIndex].Definition.Id);
                robots.RemoveAt(sourceIndex);
                return true;
            }
            if (action.Kind == OpponentAiActionKind.Move)
            {
                if (sourceIndex < 0) return false;
                var source = robots[sourceIndex]; board.Release(source.Definition.Id);
                if (!board.TryPlace(action.Anchor, source.Definition)) return false;
                robots[sourceIndex] = Changed(source, action.Anchor, source.Level, source.Damage);
                return true;
            }
            var targetIndex = robots.FindIndex(r => r.InstanceId == action.TargetInstanceId);
            if (targetIndex < 0) return false;
            var target = robots[targetIndex];
            if (target.Level >= target.MaxLevel) return false;
            if (action.Kind == OpponentAiActionKind.PurchaseAndMerge)
            {
                var found = false;
                foreach (var offer in state.Offers)
                    if (offer.SlotId == action.SlotId && offer.OfferId == action.OfferId && !offer.IsPurchased &&
                        offer.Level == target.Level && offer.Profile.RobotId == target.Profile.RobotId && offer.Price <= state.EnergyCoins)
                    { found = true; cost = offer.Price; break; }
                if (!found) return false;
            }
            else if (action.Kind == OpponentAiActionKind.Merge)
            {
                if (sourceIndex < 0 || sourceIndex == targetIndex) return false;
                var source = robots[sourceIndex];
                if (source.Level != target.Level || source.Profile.RobotId != target.Profile.RobotId) return false;
                board.Release(source.Definition.Id);
            }
            else return false;
            robots[targetIndex] = Changed(target, target.Anchor.Value, target.Level + 1, target.Damage + target.Profile.DamagePerLevel);
            if (action.Kind == OpponentAiActionKind.Merge) robots.RemoveAt(sourceIndex);
            return true;
        }

        private static OpponentAiRobotSnapshot Changed(OpponentAiRobotSnapshot robot, BoardCoordinate anchor, int level, int damage)
            => new OpponentAiRobotSnapshot(robot.InstanceId, robot.Profile, level, robot.MaxLevel, damage,
                robot.TriggerInterval, RobotLocation.Board, anchor, robot.Definition);

        private static bool ContainsSlot(IReadOnlyList<int> slots, int id)
        {
            foreach (var slot in slots) if (slot == id) return true;
            return false;
        }
    }
}
