using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GameLogic
{
    /// <summary>搜索只持有快照；粗筛逐锚点推进，精检不可抢占，运行时由调度器在后台执行。</summary>
    public sealed class OpponentAiSearchSession : IDisposable
    {
        private readonly OpponentAiSnapshot _state;
        private readonly OpponentAiPlanningOptions _options;
        private OpponentAiSnapshot _liveState;
        private OpponentAiPlanningOptions _liveOptions;
        private readonly OpponentAiRegionEvaluator _evaluator = new OpponentAiRegionEvaluator();
        private readonly IEnumerator<bool> _steps;
        private readonly List<Candidate> _candidates = new List<Candidate>();
        private readonly List<Candidate> _preview = new List<Candidate>(9);
        private readonly float[] _maxima = new float[4];
        private OpponentAiRegionEvaluation _before;
        private OpponentAiStrategySettings Tuning => _liveState.World?.Settings ?? DefaultTuning;
        private static readonly OpponentAiStrategySettings DefaultTuning = new OpponentAiStrategySettings();
        public bool IsComplete { get; private set; }
        public OpponentAiAction BestAction { get; private set; } = OpponentAiAction.Wait();
        public int BoardRevision => _state?.BoardRevision ?? 0;
        public int EvaluatedCandidates { get; private set; }

        private sealed class Candidate
        {
            public OpponentAiAction Action;
            public float G, R, C, M;
            public bool Reserve;
            public OpponentAiEnclosureKind Enclosure;
            public bool Closed => Enclosure != OpponentAiEnclosureKind.None;
            public float Score;
            public OpponentAiRegionEvaluation PreciseRegion;
            public Candidate(OpponentAiAction action, OpponentAiRegionEvaluation evaluation)
            {
                Action = action; G = evaluation.GlassValue; R = evaluation.RobotValue;
                C = evaluation.Control; M = evaluation.Mobility;
                Enclosure = evaluation.Enclosure; Reserve = evaluation.HasReserve;
            }
        }

        internal OpponentAiSearchSession(OpponentAiSnapshot state, OpponentAiPlanningOptions options)
        { _state = state; _liveState = state; _options = options; _liveOptions = options; _steps = Search().GetEnumerator(); }

        public bool MatchesOptions(OpponentAiPlanningOptions options) =>
            options.AllowMove == _options.AllowMove && options.AllowPurchase == _options.AllowPurchase &&
            options.AllowMerge == _options.AllowMerge && options.AllowRefresh == _options.AllowRefresh &&
            options.AllowTwoStep == _options.AllowTwoStep && Nullable.Equals(options.PendingFollowUp, _options.PendingFollowUp) &&
            SameBlocked(options.BlockedOfferIds, _options.BlockedOfferIds);

        private static bool SameBlocked(IReadOnlyCollection<long> a, IReadOnlyCollection<long> b)
        {
            if ((a?.Count ?? 0) != (b?.Count ?? 0)) return false;
            if (a == null || b == null) return true;
            foreach (var id in a) { var found = false; foreach (var other in b) if (id == other) { found = true; break; } if (!found) return false; }
            return true;
        }

        public void UpdateLiveSnapshot(OpponentAiSnapshot snapshot, OpponentAiPlanningOptions options)
        { _liveState = snapshot; _liveOptions = options; }

        public void Advance(int maxCandidates, double maxMilliseconds)
        {
            if (IsComplete) return;
            var timer = Stopwatch.StartNew();
            for (var i = 0; i < Math.Max(1, maxCandidates); i++)
            {
                if (!_steps.MoveNext()) { IsComplete = true; _steps.Dispose(); break; }
                if (timer.Elapsed.TotalMilliseconds >= maxMilliseconds) break;
            }
        }

        // 调度器达到搜索时间窗时调用；未扫完全盘也可使用已经精检的候选。
        public void PublishBestPartial()
        {
            if (_before == null || IsComplete || BestAction.Kind != OpponentAiActionKind.Wait) return;
            foreach (var candidate in _preview) candidate.Score = Score(candidate.G, candidate.R, candidate.C, candidate.M);
            _preview.Sort(Compare);
            BestAction = SelectVerified(_preview, _liveState);
        }

        private IEnumerable<bool> Search()
        {
            if (_state == null || _state.Side != BattleSide.Opponent) yield break;
            var board = _state.Board;
            _before = _evaluator.Evaluate(_state, board, _state.Robots);
            Accumulate(_before.GlassValue, _before.RobotValue, _before.Control, _before.Mobility);
            yield return true;
            foreach (var action in SpatialActions(_state, _options))
            {
                Candidate candidate;
                if (TryEvaluate(_state, action, out candidate))
                {
                    _candidates.Add(candidate);
                    Accumulate(candidate.G, candidate.R, candidate.C, candidate.M);
                    candidate.Score = Score(candidate.G, candidate.R, candidate.C, candidate.M);
                    foreach (var preview in _preview) preview.Score = Score(preview.G, preview.R, preview.C, preview.M);
                    _preview.Add(candidate); _preview.Sort(Compare);
                    if (_preview.Count > 8) _preview.RemoveAt(8);
                }
                EvaluatedCandidates++;
                yield return true;
            }
            var beforeScore = Score(_before.GlassValue, _before.RobotValue, _before.Control, _before.Mobility);
            foreach (var candidate in _candidates) candidate.Score = Score(candidate.G, candidate.R, candidate.C, candidate.M);
            _candidates.Sort(Compare);
            BestAction = SelectVerified(_candidates, _liveState);
            if (BestAction.Kind != OpponentAiActionKind.Wait) yield break;

            // 第二步保留目标，但使用当前球、商品、额度和几何重算；不重放旧动作队列。
            if (_liveOptions.PendingFollowUp.HasValue)
            {
                var follow = _liveOptions.PendingFollowUp.Value;
                var action = follow.ToAction();
                Candidate candidate;
                var permitted = action.BattleId == _liveState.BattleId &&
                    (action.Kind == OpponentAiActionKind.Move ? _liveOptions.AllowMove : _liveOptions.AllowPurchase && !Blocked(_liveOptions, action.OfferId));
                if (permitted && TryEvaluate(_liveState, action, out candidate))
                {
                    BestAction = SelectVerified(new List<Candidate> { candidate }, _liveState, minimumGain: .0001f);
                    if (BestAction.Kind != OpponentAiActionKind.Wait &&
                        (!follow.RequireClosed || BestAction.DecisionReason == "一步封口"))
                    {
                        if (BestAction.DecisionReason != "一步封口")
                            BestAction = Describe(BestAction, "完成两步调整", BestAction.ScoreBefore, BestAction.ScoreAfter);
                        yield break;
                    }
                    BestAction = OpponentAiAction.Wait();
                }
                yield return true;
            }

            if (_options.AllowTwoStep && !(_state.World?.Ball.IsBerserk ?? false))
            {
                // 准备步骤允许当前分下降，应按封口潜力选点，不能再次沿用贪心单步排名。
                _candidates.Sort(ComparePreparation);
                for (var i = 0; i < Math.Min(Tuning.FinalistCount, _candidates.Count); i++)
                {
                    var first = _candidates[i];
                    if (IsReverse(first.Action) || !Safe(first.Action)) { yield return true; continue; }
                    BoardModel nextBoard; List<OpponentAiRobotSnapshot> nextRobots; int cost;
                    if (!OpponentAiPlanner.TrySimulate(_liveState, first.Action, out nextBoard, out nextRobots, out cost)) continue;
                    var offers = new List<OpponentAiOfferSnapshot>();
                    foreach (var offer in _liveState.Offers) if (offer.OfferId != first.Action.OfferId) offers.Add(offer);
                    var next = new OpponentAiSnapshot(_liveState.Side, _liveState.EnergyCoins - cost, nextBoard, offers,
                        nextRobots, _liveState.CandidateProfiles, _liveState.RefreshPrice, _liveState.BattleId, _liveState.HasPurchasedShopRobots, _liveState.World);
                    var secondOptions = new OpponentAiPlanningOptions { AllowPurchase = _options.AllowPurchase,
                        AllowMove = _options.AllowMove, AllowMerge = false, AllowRefresh = false, AllowTwoStep = false,
                        BlockedOfferIds = _options.BlockedOfferIds };
                    var secondCandidates = new List<Candidate>();
                    foreach (var second in SpatialActions(next, secondOptions))
                    {
                        if (second.SourceInstanceId < 0 || first.Action.Kind == OpponentAiActionKind.Move &&
                            second.Kind == OpponentAiActionKind.Move && first.Action.SourceInstanceId == second.SourceInstanceId) continue;
                        Candidate result;
                        if (TryEvaluate(next, second, out result))
                        {
                            result.Score = Score(result.G, result.R, result.C, result.M);
                            secondCandidates.Add(result);
                        }
                        yield return true;
                    }
                    secondCandidates.Sort(Compare);
                    var final = SelectVerified(secondCandidates, next, _liveState);
                    if (final.Kind != OpponentAiActionKind.Wait && Safe(first.Action))
                    {
                        BestAction = Describe(first.Action, "两步准备，后续按最新盘面重算", final.ScoreBefore, final.ScoreAfter,
                            new OpponentAiFollowUp(final, final.DecisionReason == "一步封口"), final.ScoreReference);
                        yield break;
                    }
                    yield return true;
                }
            }

            foreach (var merge in MergeActions())
            {
                BoardModel afterBoard; List<OpponentAiRobotSnapshot> afterRobots; int cost;
                if (!OpponentAiPlanner.TrySimulate(_state, merge, out afterBoard, out afterRobots, out cost)) continue;
                var after = _evaluator.Evaluate(_liveState, afterBoard, afterRobots);
                var liveBefore = _evaluator.Evaluate(_liveState, _liveState.Board, _liveState.Robots, true);
                var liveAfter = _evaluator.Evaluate(_liveState, afterBoard, afterRobots, true);
                var beforeRobot = liveBefore.GeometryComplete && liveAfter.GeometryComplete ? liveBefore.RobotValue :
                    _evaluator.Evaluate(_liveState, _liveState.Board, _liveState.Robots).RobotValue;
                var afterRobot = liveBefore.GeometryComplete && liveAfter.GeometryComplete ? liveAfter.RobotValue : after.RobotValue;
                var unblocking = !_before.HasReserve && after.HasReserve;
                var late = _state.StrategyPhase == OpponentAiStrategyPhase.ComboControl;
                if ((!late && !unblocking) || !after.Valid || !liveAfter.Valid || afterRobot + .0001f < beforeRobot ||
                    _before.IsClosed && !after.IsClosed || !Safe(merge)) { yield return true; continue; }
                BestAction = Describe(merge, unblocking ? "合成恢复机动空间" : "保留围挡升级", beforeScore,
                    Score(after.GlassValue, after.RobotValue, after.Control, after.Mobility));
                yield break;
            }
            if (_options.AllowRefresh && OpponentAiPlanner.CanRefresh(_state))
                BestAction = new OpponentAiAction(OpponentAiActionKind.Refresh, battleId: _state.BattleId, decisionReason: "刷新寻找可用形状或升级");
        }

        private bool TryEvaluate(OpponentAiSnapshot state, OpponentAiAction action, out Candidate candidate)
        {
            candidate = null;
            BoardModel board; List<OpponentAiRobotSnapshot> robots; int cost;
            if (!OpponentAiPlanner.TrySimulate(state, action, out board, out robots, out cost)) return false;
            var result = _evaluator.Evaluate(state, board, robots);
            if (!result.Valid) return false;
            candidate = new Candidate(action, result);
            return true;
        }

        private OpponentAiAction SelectVerified(IReadOnlyList<Candidate> candidates, OpponentAiSnapshot simulationState,
            OpponentAiSnapshot comparisonState = null, float? minimumGain = null)
        {
            var sameLayout = comparisonState == null || ReferenceEquals(simulationState, comparisonState);
            // 第二层枚举可能跨帧；最终模拟布局和基线必须使用同一时刻的球与玻璃。
            if (_liveState.World != null) simulationState = simulationState.WithWorld(_liveState.World);
            comparisonState = comparisonState ?? simulationState;
            if (_liveState.World != null) comparisonState = comparisonState.WithWorld(_liveState.World);
            var before = _evaluator.Evaluate(comparisonState, comparisonState.Board, comparisonState.Robots, true);
            var preciseBefore = before;
            if (!before.Valid) return OpponentAiAction.Wait();
            var finalists = new List<Candidate>();
            var coarse = new List<OpponentAiRegionEvaluation>();
            var allComplete = before.GeometryComplete;
            for (var i = 0; i < Math.Min(Tuning.FinalistCount, candidates.Count); i++)
            {
                BoardModel board; List<OpponentAiRobotSnapshot> robots; int cost;
                var action = candidates[i].Action;
                if (!OpponentAiPlanner.TrySimulate(simulationState, action, out board, out robots, out cost)) continue;
                var after = _evaluator.Evaluate(simulationState, board, robots, true);
                if (!after.Valid) continue;
                finalists.Add(new Candidate(action, after) { PreciseRegion = after });
                coarse.Add(_evaluator.Evaluate(simulationState, board, robots));
                allComplete &= after.GeometryComplete;
            }
            if (!allComplete)
            {
                // 不把不同采样进度的部分区域拿来比收益；整组统一退回粗筛指标，封口仍须精检证明。
                before = _evaluator.Evaluate(comparisonState, comparisonState.Board, comparisonState.Robots);
                before.UseVerifiedEnclosure(preciseBefore);
                for (var i = 0; i < finalists.Count; i++)
                {
                    var preciseRegion = finalists[i].PreciseRegion;
                    coarse[i].UseVerifiedEnclosure(preciseRegion);
                    finalists[i] = new Candidate(finalists[i].Action, coarse[i]) { PreciseRegion = preciseRegion };
                }
            }
            var maxima = new[] { before.GlassValue, before.RobotValue, before.Control, before.Mobility };
            foreach (var candidate in finalists)
            {
                maxima[0] = Math.Max(maxima[0], candidate.G); maxima[1] = Math.Max(maxima[1], candidate.R);
                maxima[2] = Math.Max(maxima[2], candidate.C); maxima[3] = Math.Max(maxima[3], candidate.M);
            }
            var reference = new OpponentAiScoreReference(new UnityEngine.Vector4(maxima[0], maxima[1], maxima[2], maxima[3]), allComplete);
            var baseline = Score(before.GlassValue, before.RobotValue, before.Control, before.Mobility, maxima);
            foreach (var candidate in finalists) candidate.Score = Score(candidate.G, candidate.R, candidate.C, candidate.M, maxima);
            finalists.Sort(Compare);
            foreach (var candidate in finalists)
            {
                if (candidate.Enclosure < candidate.Action.MinimumEnclosure) continue;
                var close = candidate.Closed && (candidate.Enclosure > before.Enclosure ||
                    candidate.Enclosure == before.Enclosure && candidate.C > before.Control + .05f);
                var unblocking = !before.HasReserve && candidate.Reserve;
                var startup = (comparisonState.World == null || !comparisonState.World.Ball.IsLaunched) &&
                    CountRobots(comparisonState) < 2 && candidate.Action.Kind == OpponentAiActionKind.PurchaseAndDeploy;
                if (!close && !unblocking && !startup && candidate.Score < baseline + (minimumGain ?? Tuning.MinimumScoreGain) ||
                    !candidate.Reserve && !close || IsReverse(candidate.Action) && !close && !unblocking ||
                    !_evaluator.IsActionSafe(simulationState, candidate.Action, true,
                        sameLayout ? preciseBefore : null, candidate.PreciseRegion)) continue;
                var reason = close ? "一步封口" : unblocking ? "恢复机动空间" : startup ? "开局部署" :
                    comparisonState.StrategyPhase == OpponentAiStrategyPhase.GlassExpansion ? "改善玻璃接触" : "改善连击围挡";
                return Describe(candidate.Action, reason, baseline, candidate.Score, scoreReference: reference,
                    minimumEnclosure: candidate.Enclosure);
            }
            return OpponentAiAction.Wait();
        }

        private bool Safe(OpponentAiAction action, bool requireClosed = false)
        {
            if (!_evaluator.IsActionSafe(_liveState, action)) return false;
            // IsActionSafe已负责球扫掠与旧围挡保护；仅封口候选还需要证明新区域确实封闭。
            if (!requireClosed) return true;
            BoardModel board; List<OpponentAiRobotSnapshot> robots; int cost;
            if (!OpponentAiPlanner.TrySimulate(_liveState, action, out board, out robots, out cost)) return false;
            var after = _evaluator.Evaluate(_liveState, board, robots, true);
            return after.Valid && after.IsClosed;
        }

        private static IEnumerable<OpponentAiAction> SpatialActions(OpponentAiSnapshot state, OpponentAiPlanningOptions options)
        {
            var board = state.Board;
            var anchors = new List<BoardCoordinate>();
            for (var row = 0; row < board.Rows; row++)
                for (var column = 0; column < board.Columns; column++) anchors.Add(new BoardCoordinate(column, row));
            var focus = state.World != null && state.World.Ball.IsLaunched ? state.World.Ball.Position :
                new UnityEngine.Vector2(board.Columns * .5f, board.Rows * .5f);
            anchors.Sort((a, b) => {
                var distanceA = Math.Abs(a.Column + .5f - focus.x) + Math.Abs(a.Row + .5f - focus.y);
                var distanceB = Math.Abs(b.Column + .5f - focus.x) + Math.Abs(b.Row + .5f - focus.y);
                var distance = distanceA.CompareTo(distanceB);
                return distance != 0 ? distance : a.Row == b.Row ? a.Column.CompareTo(b.Column) : a.Row.CompareTo(b.Row);
            });
            if (options.AllowMove && !(state.World?.Ball.IsBerserk ?? false))
                foreach (var robot in state.Robots)
                {
                    if (!robot.IsOnBoard || robot.InstanceId < 0) continue;
                    foreach (var anchor in anchors)
                        {
                            if (anchor.Column + robot.Definition.Width > board.Columns || anchor.Row + robot.Definition.Height > board.Rows) continue;
                            if (anchor.Equals(robot.Anchor.Value)) continue;
                            yield return new OpponentAiAction(OpponentAiActionKind.Move, sourceInstanceId: robot.InstanceId,
                                anchor: anchor, battleId: state.BattleId);
                        }
                }
            if (!options.AllowPurchase) yield break;
            foreach (var offer in state.Offers)
            {
                if (offer.IsPurchased || offer.Price > state.EnergyCoins || Blocked(options, offer.OfferId)) continue;
                foreach (var anchor in anchors)
                {
                    if (anchor.Column + offer.Profile.Definition.Width > board.Columns || anchor.Row + offer.Profile.Definition.Height > board.Rows) continue;
                        yield return new OpponentAiAction(OpponentAiActionKind.PurchaseAndDeploy, offer.SlotId,
                            offer.OfferId, anchor: anchor, battleId: state.BattleId);
                }
            }
        }

        private IEnumerable<OpponentAiAction> MergeActions()
        {
            if (!_options.AllowMerge) yield break;
            if (_options.AllowPurchase && _state.StrategyPhase == OpponentAiStrategyPhase.ComboControl)
                foreach (var offer in _state.Offers)
                    foreach (var target in _state.Robots)
                    {
                        if (offer.IsPurchased || Blocked(_options, offer.OfferId) || !target.IsOnBoard ||
                            offer.Price > _state.EnergyCoins || target.Level >= target.MaxLevel || target.Level != offer.Level ||
                            target.Profile.RobotId != offer.Profile.RobotId || target.Profile.DamagePerLevel <= 0) continue;
                        yield return new OpponentAiAction(OpponentAiActionKind.PurchaseAndMerge, offer.SlotId, offer.OfferId,
                            targetInstanceId: target.InstanceId, anchor: target.Anchor.Value, battleId: _state.BattleId);
                    }
            if (_liveState.World?.Ball.IsBerserk ?? false) yield break;
            foreach (var source in _state.Robots)
                foreach (var target in _state.Robots)
                {
                    if (!source.IsOnBoard || !target.IsOnBoard || source.InstanceId == target.InstanceId ||
                        source.Level != target.Level || source.Profile.RobotId != target.Profile.RobotId || target.Level >= target.MaxLevel) continue;
                    yield return new OpponentAiAction(OpponentAiActionKind.Merge, sourceInstanceId: source.InstanceId,
                        targetInstanceId: target.InstanceId, anchor: target.Anchor.Value, battleId: _state.BattleId);
                }
        }

        private bool IsReverse(OpponentAiAction action)
        {
            if (action.Kind != OpponentAiActionKind.Move || _liveOptions.RecentMoves == null) return false;
            foreach (var memory in _liveOptions.RecentMoves)
                if (memory.InstanceId == action.SourceInstanceId && memory.From.Equals(action.Anchor) &&
                    _liveOptions.ActiveTime - memory.ActiveTime < Tuning.ReverseMoveBlock) return true;
            return false;
        }

        private void Accumulate(float g, float r, float c, float m)
        { _maxima[0] = Math.Max(_maxima[0], g); _maxima[1] = Math.Max(_maxima[1], r); _maxima[2] = Math.Max(_maxima[2], c); _maxima[3] = Math.Max(_maxima[3], m); }
        private float Score(float g, float r, float c, float m, float[] maxima = null)
        {
            maxima = maxima ?? _maxima;
            var weights = _state.StrategyPhase == OpponentAiStrategyPhase.ComboControl ? Tuning.ComboWeights : Tuning.GlassWeights;
            return new OpponentAiScoreReference(new UnityEngine.Vector4(maxima[0], maxima[1], maxima[2], maxima[3]), false)
                .Score(g, r, c, m, weights);
        }
        private int Compare(Candidate a, Candidate b)
        {
            var close = b.Enclosure.CompareTo(a.Enclosure);
            if (close != 0) return close;
            // 固定分桶保证比较关系可传递；逐对使用“差值小于阈值”会让Sort产生循环排序。
            var scoreOrder = Math.Floor(b.Score * 100f).CompareTo(Math.Floor(a.Score * 100f));
            if (scoreOrder != 0) return scoreOrder;
            var free = (a.Action.Kind == OpponentAiActionKind.Move ? 0 : 1).CompareTo(b.Action.Kind == OpponentAiActionKind.Move ? 0 : 1);
            if (free != 0) return free;
            // 开放大区域里许多位置边界分相同；优先球附近的准备位，避免八个名额被远处等价位置占满。
            var proximity = DistanceToBall(a.Action).CompareTo(DistanceToBall(b.Action));
            if (proximity != 0) return proximity;
            var instance = a.Action.SourceInstanceId.CompareTo(b.Action.SourceInstanceId);
            if (instance != 0) return instance;
            var slot = a.Action.SlotId.CompareTo(b.Action.SlotId);
            if (slot != 0) return slot;
            return a.Action.Anchor.Row == b.Action.Anchor.Row ? a.Action.Anchor.Column.CompareTo(b.Action.Anchor.Column) : a.Action.Anchor.Row.CompareTo(b.Action.Anchor.Row);
        }
        private float DistanceToBall(OpponentAiAction action)
        {
            var board = _liveState.World;
            if (board == null || !board.Ball.IsLaunched) return 0f;
            var centre = new UnityEngine.Vector2(action.Anchor.Column + .5f, action.Anchor.Row + .5f);
            return (centre - board.Ball.Position).sqrMagnitude;
        }
        private int ComparePreparation(Candidate a, Candidate b)
        {
            var proximity = DistanceToBall(a.Action).CompareTo(DistanceToBall(b.Action));
            if (proximity != 0) return proximity;
            // 相同目标优先搬远处单位，保留已经参与球附近围挡的机器人。
            var distance = OriginDistance(b.Action).CompareTo(OriginDistance(a.Action));
            return distance != 0 ? distance : Compare(a, b);
        }
        private float OriginDistance(OpponentAiAction action)
        {
            if (_liveState.World == null || !_liveState.World.Ball.IsLaunched || action.Kind != OpponentAiActionKind.Move) return 0;
            foreach (var robot in _liveState.Robots)
                if (robot.InstanceId == action.SourceInstanceId && robot.Anchor.HasValue)
                {
                    var centre = new UnityEngine.Vector2(robot.Anchor.Value.Column + .5f, robot.Anchor.Value.Row + .5f);
                    return (centre - _liveState.World.Ball.Position).sqrMagnitude;
                }
            return 0;
        }
        private static bool Blocked(OpponentAiPlanningOptions options, long id)
        { if (options.BlockedOfferIds != null) foreach (var blocked in options.BlockedOfferIds) if (blocked == id) return true; return false; }
        private static int CountRobots(OpponentAiSnapshot state)
        { var count = 0; foreach (var robot in state.Robots) if (robot.IsOnBoard) count++; return count; }
        private static OpponentAiAction Describe(OpponentAiAction a, string reason, float before, float after,
            OpponentAiFollowUp? followUp = null, OpponentAiScoreReference? scoreReference = null,
            OpponentAiEnclosureKind? minimumEnclosure = null)
            => new OpponentAiAction(a.Kind, a.SlotId, a.OfferId, a.SourceInstanceId, a.TargetInstanceId, a.Anchor,
                a.BattleId, a.FreedOfferId, reason, before, after, followUp, scoreReference ?? a.ScoreReference,
                minimumEnclosure ?? a.MinimumEnclosure);
        public void Dispose() { _steps.Dispose(); IsComplete = true; }
    }
}
