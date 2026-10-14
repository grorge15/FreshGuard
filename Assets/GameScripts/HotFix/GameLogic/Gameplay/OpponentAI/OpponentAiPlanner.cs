using System;
using System.Collections.Generic;
using System.Text;

namespace GameLogic
{
    /// <summary>只读取己方快照；所有试摆和释放占格发生在虚拟棋盘。</summary>
    public static class OpponentAiPlanner
    {
        private const float Epsilon = 0.0001f;
        private static readonly BoardCoordinate[] Directions = {
            new BoardCoordinate(1, 0), new BoardCoordinate(-1, 0),
            new BoardCoordinate(0, 1), new BoardCoordinate(0, -1)
        };

        public static OpponentAiAction Plan(OpponentAiSnapshot snapshot, OpponentAiPlanningOptions options = null)
        {
            if (snapshot == null || snapshot.Side != BattleSide.Opponent) return OpponentAiAction.Wait();
            return new Search(snapshot, options ?? new OpponentAiPlanningOptions()).Plan();
        }

        public static bool CanRefresh(OpponentAiSnapshot snapshot)
            => snapshot != null && snapshot.Side == BattleSide.Opponent &&
                new Search(snapshot, new OpponentAiPlanningOptions()).CanRefresh();

        private sealed class Search
        {
            private readonly OpponentAiSnapshot _state;
            private readonly OpponentAiPlanningOptions _options;
            private readonly BoardModel _board;
            private readonly List<OpponentAiRobotSnapshot> _robots = new List<OpponentAiRobotSnapshot>();
            private readonly List<PlaceableDefinition> _shapes = new List<PlaceableDefinition>();
            private readonly List<OpponentAiOfferSnapshot> _offers = new List<OpponentAiOfferSnapshot>();

            public Search(OpponentAiSnapshot state, OpponentAiPlanningOptions options)
            {
                _state = state;
                _options = options;
                _board = state.Board;
                foreach (var robot in state.Robots) if (robot.IsOnBoard) _robots.Add(robot);
                _robots.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));
                foreach (var offer in state.Offers)
                    if (!offer.IsPurchased && offer.Price <= state.EnergyCoins && !IsBlocked(offer.OfferId)) _offers.Add(offer);
                _offers.Sort((a, b) => a.SlotId.CompareTo(b.SlotId));
                var keys = new HashSet<string>();
                foreach (var candidate in state.CandidateProfiles)
                    if (keys.Add(ShapeKey(candidate.Definition))) _shapes.Add(candidate.Definition);
            }

            public OpponentAiAction Plan()
            {
                if (_options.AllowPurchase && _options.PreferredOfferId != 0)
                {
                    var preferred = Deployment(_options.PreferredOfferId);
                    if (preferred.Kind != OpponentAiActionKind.Wait) return preferred;
                }
                var coverage = _robots.Count < Math.Max(1, _options.InitialRobotTarget);
                if (coverage && _options.AllowPurchase)
                {
                    var deploy = Deployment();
                    if (deploy.Kind != OpponentAiActionKind.Wait) return deploy;
                }
                if (!coverage && _options.AllowPurchase && _options.AllowMerge)
                {
                    var merge = OfferMerge();
                    if (merge.Kind != OpponentAiActionKind.Wait) return merge;
                }
                if (_options.AllowPurchase)
                {
                    var deploy = Deployment();
                    if (deploy.Kind != OpponentAiActionKind.Wait) return deploy;
                }
                if (_options.AllowMerge)
                {
                    var merge = BoardMerge();
                    if (merge.Kind != OpponentAiActionKind.Wait) return merge;
                }
                if (_options.AllowMove)
                {
                    var move = Move();
                    if (move.Kind != OpponentAiActionKind.Wait) return move;
                }
                if (_options.AllowRefresh && CanRefresh()) return Action(OpponentAiActionKind.Refresh);
                return OpponentAiAction.Wait();
            }

            private bool IsBlocked(long id)
            {
                if (_options.BlockedOfferIds == null) return false;
                foreach (var blocked in _options.BlockedOfferIds) if (blocked == id) return true;
                return false;
            }

            private OpponentAiAction Deployment(long requiredOffer = 0)
            {
                var best = OpponentAiAction.Wait();
                var efficiency = float.MinValue;
                var position = float.MinValue;
                foreach (var offer in _offers)
                {
                    if (requiredOffer != 0 && offer.OfferId != requiredOffer) continue;
                    BoardCoordinate anchor;
                    float score;
                    if (!BestAnchor(_board, offer.Profile.Definition, out anchor, out score)) continue;
                    var rate = Rate(offer.Profile.DamageAt(offer.Level), offer.Profile.TriggerInterval) / Math.Max(1, offer.Price);
                    if (rate < efficiency - Epsilon || (Math.Abs(rate - efficiency) <= Epsilon && score <= position + Epsilon)) continue;
                    efficiency = rate;
                    position = score;
                    best = Action(OpponentAiActionKind.PurchaseAndDeploy, offer.SlotId, offer.OfferId, anchor: anchor);
                }
                return best;
            }

            private OpponentAiAction OfferMerge()
            {
                var best = OpponentAiAction.Wait();
                var bestGain = float.MinValue;
                foreach (var offer in _offers)
                    foreach (var robot in _robots)
                    {
                        if (robot.Profile.RobotId != offer.Profile.RobotId || robot.Level != offer.Level ||
                            robot.Level >= Math.Min(5, robot.MaxLevel)) continue;
                        var gain = Rate(robot.Profile.DamagePerLevel, robot.TriggerInterval) *
                            ContactFactor(_board, robot.Anchor.Value, robot.Definition) / Math.Max(1, offer.Price);
                        if (gain <= bestGain + Epsilon) continue;
                        bestGain = gain;
                        best = Action(OpponentAiActionKind.PurchaseAndMerge, offer.SlotId, offer.OfferId,
                            target: robot.InstanceId, anchor: robot.Anchor.Value);
                    }
                return best;
            }

            private OpponentAiAction BoardMerge()
            {
                if (_robots.Count < 2) return OpponentAiAction.Wait();
                var keepsCoverage = _robots.Count - 1 >= Math.Max(3, _options.InitialRobotTarget);
                var before = TotalOutput(_board, _robots);
                var best = OpponentAiAction.Wait();
                var bestOutput = float.MinValue;
                // 合成的两个方向分别评估：目标位置保留，来源位置释放。
                foreach (var source in _robots)
                    foreach (var target in _robots)
                    {
                        if (source.InstanceId == target.InstanceId || source.Profile.RobotId != target.Profile.RobotId ||
                            source.Level != target.Level || target.Level >= Math.Min(5, target.MaxLevel)) continue;
                        var board = OpponentAiSnapshot.CopyBoard(_board);
                        board.Release(source.Definition.Id);
                        var robots = new List<OpponentAiRobotSnapshot>(_robots);
                        robots.RemoveAll(r => r.InstanceId == source.InstanceId || r.InstanceId == target.InstanceId);
                        robots.Add(ChangedRobot(target, target.Anchor.Value, target.Level + 1,
                            target.Damage + target.Profile.DamagePerLevel));
                        var after = TotalOutput(board, robots);
                        var freed = _options.AllowPurchase ? FreedOffer(board) : 0;
                        var maintainsOutput = keepsCoverage && after + Epsilon >= before;
                        // 腾位属于独立分支；下一步优先购买已腾出位置的商品，恢复阵容。
                        var freesPurchase = freed != 0 && after + Epsilon >= before * 0.9f;
                        if (!maintainsOutput && !freesPurchase) continue;
                        if (after <= bestOutput + Epsilon) continue;
                        bestOutput = after;
                        best = Action(OpponentAiActionKind.Merge, source: source.InstanceId,
                            target: target.InstanceId, anchor: target.Anchor.Value, freed: freed);
                    }
                return best;
            }

            private OpponentAiAction Move()
            {
                var before = TotalOutput(_board, _robots);
                var fitBefore = FitRatio(_board);
                var best = OpponentAiAction.Wait();
                var bestOutput = float.MinValue;
                var bestPosition = float.MinValue;
                var unblocksBest = false;
                foreach (var robot in _robots)
                {
                    var empty = OpponentAiSnapshot.CopyBoard(_board);
                    empty.Release(robot.Definition.Id);
                    for (var row = 0; row < empty.Rows; row++)
                        for (var column = 0; column < empty.Columns; column++)
                        {
                            var anchor = new BoardCoordinate(column, row);
                            if (anchor.Equals(robot.Anchor.Value) || !Fits(empty, anchor, robot.Definition)) continue;
                            var board = OpponentAiSnapshot.CopyBoard(empty);
                            board.TryPlace(anchor, robot.Definition);
                            var robots = new List<OpponentAiRobotSnapshot>(_robots);
                            robots.RemoveAll(r => r.InstanceId == robot.InstanceId);
                            robots.Add(ChangedRobot(robot, anchor, robot.Level, robot.Damage));
                            var after = TotalOutput(board, robots);
                            var freed = _options.AllowPurchase ? FreedOffer(board) : 0;
                            var unblock = freed != 0 && after + Epsilon >= before * 0.9f;
                            var improve = after > before + Epsilon && after + Epsilon >= before * 1.1f &&
                                FitRatio(board) + Epsilon >= fitBefore;
                            if (!unblock && !improve) continue;
                            var position = PositionScore(board, anchor, robot.Definition);
                            if (unblocksBest && !unblock) continue;
                            if (unblocksBest == unblock && (after < bestOutput - Epsilon ||
                                (Math.Abs(after - bestOutput) <= Epsilon && position <= bestPosition + Epsilon))) continue;
                            bestOutput = after;
                            bestPosition = position;
                            unblocksBest = unblock;
                            best = Action(OpponentAiActionKind.Move, source: robot.InstanceId, anchor: anchor,
                                freed: unblock ? freed : 0);
                        }
                }
                return best;
            }

            private long FreedOffer(BoardModel board)
            {
                foreach (var offer in _offers)
                    if (!HasPlacement(_board, offer.Profile.Definition) && HasPlacement(board, offer.Profile.Definition)) return offer.OfferId;
                return 0;
            }

            public bool CanRefresh()
            {
                if (!_options.AllowPurchase || _state.HasPurchasedShopRobots) return false;
                foreach (var offer in _state.Offers) if (offer.IsPurchased) return false;
                foreach (var robot in _state.Robots) if (robot.Location == RobotLocation.PurchasedShopSlot) return false;
                var minimum = int.MaxValue;
                var fits = false;
                foreach (var profile in _state.CandidateProfiles)
                {
                    minimum = Math.Min(minimum, profile.LevelOnePrice);
                    fits |= HasPlacement(_board, profile.Definition);
                }
                return fits && minimum != int.MaxValue && (long)_state.EnergyCoins >= (long)_state.RefreshPrice + minimum;
            }

            private bool BestAnchor(BoardModel board, PlaceableDefinition definition, out BoardCoordinate anchor, out float score)
            {
                anchor = default(BoardCoordinate);
                score = float.MinValue;
                var found = false;
                for (var row = 0; row < board.Rows; row++)
                    for (var column = 0; column < board.Columns; column++)
                    {
                        var candidate = new BoardCoordinate(column, row);
                        if (!Fits(board, candidate, definition)) continue;
                        var copy = OpponentAiSnapshot.CopyBoard(board);
                        copy.TryPlace(candidate, definition);
                        var value = PositionScore(copy, candidate, definition);
                        if (found && value <= score + Epsilon) continue;
                        found = true;
                        anchor = candidate;
                        score = value;
                    }
                return found;
            }

            private float PositionScore(BoardModel board, BoardCoordinate anchor, PlaceableDefinition definition)
            {
                var x = 0f;
                var y = 0f;
                foreach (var offset in definition.Cells) { x += anchor.Column + offset.Column; y += anchor.Row + offset.Row; }
                x /= definition.Cells.Count;
                y /= definition.Cells.Count;
                var distance = Math.Abs(x - (board.Columns - 1) * 0.5f) + Math.Abs(y - (board.Rows - 1) * 0.5f);
                var maxDistance = Math.Max(1f, (board.Columns + board.Rows - 2) * 0.5f);
                return 50f * Exposure(board, anchor, definition) + 35f * FitRatio(board) +
                    15f * Math.Max(0f, 1f - distance / maxDistance);
            }

            private float FitRatio(BoardModel board)
            {
                if (_shapes.Count == 0) return 0f;
                var fit = 0;
                foreach (var shape in _shapes) if (HasPlacement(board, shape)) fit++;
                return (float)fit / _shapes.Count;
            }

            private OpponentAiAction Action(OpponentAiActionKind kind, int slot = -1, long offer = 0,
                int source = 0, int target = 0, BoardCoordinate anchor = default(BoardCoordinate), long freed = 0)
            {
                return new OpponentAiAction(kind, slot, offer, source, target, anchor, _state.BattleId, freed);
            }
        }

        private static OpponentAiRobotSnapshot ChangedRobot(OpponentAiRobotSnapshot robot, BoardCoordinate anchor, int level, int damage)
        {
            return new OpponentAiRobotSnapshot(robot.InstanceId, robot.Profile, level, robot.MaxLevel,
                damage, robot.TriggerInterval, RobotLocation.Board, anchor, robot.Definition);
        }

        private static float TotalOutput(BoardModel board, IReadOnlyList<OpponentAiRobotSnapshot> robots)
        {
            var total = 0f;
            foreach (var robot in robots)
                total += Rate(robot.Damage, robot.TriggerInterval) * ContactFactor(board, robot.Anchor.Value, robot.Definition);
            return total;
        }

        private static float Rate(int damage, float interval) => damage / Math.Max(0.01f, interval);
        private static float ContactFactor(BoardModel board, BoardCoordinate anchor, PlaceableDefinition definition)
            => 0.25f + 0.75f * Exposure(board, anchor, definition);

        private static float Exposure(BoardModel board, BoardCoordinate anchor, PlaceableDefinition definition)
        {
            var cells = new HashSet<BoardCoordinate>(definition.Cells);
            var perimeter = 0;
            var exposed = 0;
            foreach (var offset in definition.Cells)
                foreach (var direction in Directions)
                {
                    var neighbor = new BoardCoordinate(offset.Column + direction.Column, offset.Row + direction.Row);
                    if (cells.Contains(neighbor)) continue;
                    perimeter++;
                    var world = new BoardCoordinate(anchor.Column + neighbor.Column, anchor.Row + neighbor.Row);
                    if (board.IsOpen(world) && !board.IsOccupied(world)) exposed++;
                }
            return perimeter == 0 ? 0f : (float)exposed / perimeter;
        }

        private static bool Fits(BoardModel board, BoardCoordinate anchor, PlaceableDefinition definition)
        {
            foreach (var offset in definition.Cells)
            {
                var cell = new BoardCoordinate(anchor.Column + offset.Column, anchor.Row + offset.Row);
                if (!board.IsOpen(cell) || board.IsOccupied(cell)) return false;
            }
            return true;
        }

        private static bool HasPlacement(BoardModel board, PlaceableDefinition definition)
        {
            for (var row = 0; row < board.Rows; row++)
                for (var column = 0; column < board.Columns; column++)
                    if (Fits(board, new BoardCoordinate(column, row), definition)) return true;
            return false;
        }

        private static string ShapeKey(PlaceableDefinition definition)
        {
            var cells = new List<BoardCoordinate>(definition.Cells);
            cells.Sort((a, b) => a.Row == b.Row ? a.Column.CompareTo(b.Column) : a.Row.CompareTo(b.Row));
            var key = new StringBuilder();
            foreach (var cell in cells) key.Append(cell.Column).Append(',').Append(cell.Row).Append(';');
            return key.ToString();
        }
    }
}
