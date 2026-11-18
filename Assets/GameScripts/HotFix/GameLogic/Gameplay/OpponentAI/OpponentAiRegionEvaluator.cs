using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameLogic
{
    public enum OpponentAiEnclosureKind { None, Glass, Robots }

    public sealed class OpponentAiRegionEvaluation
    {
        public bool Valid, HasReserve;
        public OpponentAiEnclosureKind Enclosure;
        public bool IsClosed => Enclosure != OpponentAiEnclosureKind.None;
        public float GlassValue, RobotValue, Control, Mobility;
        public int Area;
        public bool[] Reachable;
        // 超预算不是非法布局，但不能作为可靠围挡的证据。
        public bool GeometryComplete;
        public int SampleCount;
        internal Func<Vector2, bool> ContainsPoint;
        internal OpponentAiRegionEvaluator.FineRegion Fine;
        public bool Contains(Vector2 point) => Valid && ContainsPoint != null && ContainsPoint(point);
        public bool ImprovesEnclosure(OpponentAiEnclosureKind before, float beforeControl)
            => IsClosed && (Enclosure > before || Enclosure == before && Control > beforeControl + .05f);
        internal void UseVerifiedEnclosure(OpponentAiRegionEvaluation precise)
        {
            Enclosure = precise?.Enclosure ?? OpponentAiEnclosureKind.None;
            Control = precise != null && precise.GeometryComplete && precise.IsClosed ? precise.Control : 0f;
        }
    }

    /// <summary>后台生成区域证明，主线程只复查版本、球所在连通区及短时扫掠。</summary>
    public sealed class OpponentAiActionProof
    {
        private readonly OpponentAiSnapshot _snapshot;
        private readonly OpponentAiAction _action;
        private readonly OpponentAiRegionEvaluation _before, _after, _final;
        private OpponentAiActionProof(OpponentAiSnapshot snapshot, OpponentAiAction action,
            OpponentAiRegionEvaluation before, OpponentAiRegionEvaluation after, OpponentAiRegionEvaluation final)
        { _snapshot = snapshot; _action = action; _before = before; _after = after; _final = final; }

        public static OpponentAiActionProof Create(OpponentAiSnapshot snapshot, OpponentAiAction action)
        {
            if (snapshot == null) return null;
            BoardModel board; List<OpponentAiRobotSnapshot> robots; int cost;
            if (!OpponentAiPlanner.TrySimulate(snapshot, action, out board, out robots, out cost)) return null;
            var evaluator = new OpponentAiRegionEvaluator();
            var before = evaluator.Evaluate(snapshot, snapshot.Board, snapshot.Robots, true);
            var after = evaluator.Evaluate(snapshot, board, robots, true);
            OpponentAiRegionEvaluation final;
            if (!before.Valid || !after.Valid || !evaluator.IsActionSafe(snapshot, action, true, before, after) ||
                !OpponentAiPlanner.IsActionUseful(snapshot, action, evaluator, before, after, out final)) return null;
            return new OpponentAiActionProof(snapshot, action, before, after, action.FollowUp.HasValue ? final : null);
        }

        public bool CanCommit(OpponentAiSnapshot current)
        {
            if (current == null || !OpponentAiArmyRules.Allows(current, _action)) return false;
            if (current.BattleId != _snapshot.BattleId || current.BoardRevision != _snapshot.BoardRevision ||
                current.StrategyPhase != _snapshot.StrategyPhase) return false;
            var spends = _action.Kind == OpponentAiActionKind.PurchaseAndDeploy || _action.Kind == OpponentAiActionKind.PurchaseAndMerge ||
                _action.FollowUp.HasValue && (_action.FollowUp.Value.Kind == OpponentAiActionKind.PurchaseAndDeploy ||
                    _action.FollowUp.Value.Kind == OpponentAiActionKind.PurchaseAndMerge);
            if (spends && current.EnergyCoins != _snapshot.EnergyCoins) return false;
            var oldBall = _snapshot.World?.Ball ?? default(OpponentAiBallState);
            var ball = current.World?.Ball ?? default(OpponentAiBallState);
            if (oldBall.IsLaunched != ball.IsLaunched || oldBall.IsBerserk != ball.IsBerserk ||
                oldBall.RecoveryState != ball.RecoveryState || oldBall.Radius != ball.Radius) return false;
            if (ball.IsLaunched && (!_before.Contains(ball.Position) || !_after.Contains(ball.Position) ||
                _final != null && !_final.Contains(ball.Position))) return false;
            // 围挡/收益已由后台证明；这里只做最新位置及首次碰撞前的压球检查，不再Flood。
            return new OpponentAiRegionEvaluator().IsActionSafe(current, _action, protectEnclosure: false);
        }
    }

    /// <summary>纯快照区域分析。坐标为棋盘格单位；不访问场景或改变真实占格。</summary>
    public sealed class OpponentAiRegionEvaluator
    {
        public const int MAX_PRECISE_SAMPLES = 32768;
        private const float EPSILON = .00001f;
        private const float INSET = .06f;
        private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private readonly Dictionary<PlaceableDefinition, Vector2[][]> _footprints = new Dictionary<PlaceableDefinition, Vector2[][]>();
        private readonly Dictionary<int, bool> _sampleFree = new Dictionary<int, bool>();
        private readonly List<int> _queue = new List<int>();
        private StaticGeometry _static;

        public OpponentAiRegionEvaluation Evaluate(OpponentAiSnapshot state, BoardModel board,
            IReadOnlyList<OpponentAiRobotSnapshot> robots, bool precise = false)
        {
            if (state == null || board == null || robots == null) return new OpponentAiRegionEvaluation();
            var terrain = GetStatic(state.World, board);
            var owners = Owners(board, robots);
            var ball = state.World != null ? state.World.Ball : default(OpponentAiBallState);
            var result = Coarse(board, robots, terrain, owners, ball);
            if (!precise || !ball.IsLaunched) return result;
            result.Reachable = null;
            result.Enclosure = OpponentAiEnclosureKind.None;
            result.Control = 0f;
            result.GeometryComplete = false;
            if (!Finite(ball.Position) || !Finite(ball.Radius) || ball.Radius <= 0f)
            { result.Valid = false; return result; }
            var geometry = BuildGeometry(state, board, robots, terrain, owners);
            if (!geometry.Valid || !geometry.Clear(ball.Position, ball.Radius, ball.IsBerserk))
            { result.Valid = false; result.ContainsPoint = null; return result; }
            if (ball.RecoveryState == PlacementRecoveryState.Trapped)
            { result.Valid = false; result.ContainsPoint = null; return result; }
            var step = Math.Min(.25f, ball.Radius * .5f);
            // 不分配宽×高细格数组，小半径和64×64同样只从球附近展开。
            var widthDouble = Math.Ceiling(board.Columns / (double)step);
            var heightDouble = Math.Ceiling(board.Rows / (double)step);
            if (widthDouble * heightDouble > int.MaxValue || widthDouble > int.MaxValue || heightDouble > int.MaxValue)
            { result.ContainsPoint = p => (p - ball.Position).sqrMagnitude < EPSILON * EPSILON; return result; }
            if (widthDouble * heightDouble > MAX_PRECISE_SAMPLES * 2d && !ball.IsBerserk &&
                ProveOpen(geometry, terrain, ball.Position, ball.Radius))
            {
                // 已证明开放，不再花细格预算扫描大盘；精确可达查询保留直线扫掠证据。
                result.Valid = true;
                result.ContainsPoint = p => Finite(p) && !geometry.Cast(ball.Position, p, ball.Radius, false).Found;
                return result;
            }
            var grid = new FineRegion(geometry, step, (int)widthDouble, (int)heightDouble, ball.Radius, ball.IsBerserk, ball.Position);
            var contacts = new Contacts();
            bool complete, reachedGoal;
            var budget = MAX_PRECISE_SAMPLES;
            grid.Nodes = Flood(grid, ball.Position, ball.Radius, ball.IsBerserk, false, 0,
                contacts, ref budget, out complete, out reachedGoal);
            result.SampleCount = MAX_PRECISE_SAMPLES - budget;
            result.Fine = grid;
            result.ContainsPoint = grid.Contains;
            if (grid.Nodes.Count == 0)
            {
                // 合法球点可能落在细采样未覆盖的极窄通道；不能当作压球或封口。
                result.Valid = true;
                return result;
            }
            result.Valid = true;
            var coarseCells = new HashSet<int>();
            foreach (var node in grid.Nodes)
            {
                var p = grid.Point(node);
                coarseCells.Add(Mathf.FloorToInt(p.y) * board.Columns + Mathf.FloorToInt(p.x));
            }
            result.Area = coarseCells.Count;
            result.GlassValue = contacts.Glass;
            result.RobotValue = contacts.Robot / Math.Max(step * step, grid.Nodes.Count * step * step);
            result.Control = Tightness(contacts, grid.Nodes.Count * step * step, result.Enclosure);
            if (!complete) return result;
            result.GeometryComplete = true;
            if (ball.IsBerserk || ball.RecoveryState != PlacementRecoveryState.None || contacts.WallLength > EPSILON ||
                contacts.RobotLength + contacts.GlassLength <= EPSILON)
                return result;

            // 每个真实通路都映射到一个可能通行的8邻细格路径。放宽半格对角线距离，
            // 只检查点而不限制边，得到可达区域的上界，防止窄缝漏采样造成假封口。
            var relaxedRadius = Math.Max(EPSILON, ball.Radius - step * .707107f - EPSILON);
            var upperContacts = new Contacts();
            var upper = Flood(grid, ball.Position, relaxedRadius, false, true, 0, upperContacts,
                ref budget, out complete, out reachedGoal, stopAtWall: true);
            result.SampleCount = MAX_PRECISE_SAMPLES - budget;
            if (!complete) { result.GeometryComplete = false; return result; }
            if (upper.Count == 0 || upperContacts.WallLength > EPSILON ||
                grid.Nodes.Count * step * step < ball.Radius * ball.Radius * 2f) return result;

            var enclosure = OpponentAiEnclosureKind.Robots;
            if (contacts.GlassLength > EPSILON)
            {
                // 忽略玻璃和其他静态地形后仍不能通到外边界，才是机器人独立成环。
                // 环内有玻璃不应把完整机器人环降级；开放空间一触边就停止，沿用总采样预算。
                var robotsOnly = new FineRegion(geometry.WithoutTerrain(), step, grid.Width, grid.Height,
                    ball.Radius, false, ball.Position);
                var robotContacts = new Contacts();
                var robotNodes = Flood(robotsOnly, ball.Position, relaxedRadius, false, true, 0, robotContacts,
                    ref budget, out complete, out reachedGoal, stopAtWall: true);
                enclosure = robotNodes.Count > 0 && robotContacts.WallLength <= EPSILON
                    ? OpponentAiEnclosureKind.Robots : OpponentAiEnclosureKind.Glass;
            }
            result.SampleCount = MAX_PRECISE_SAMPLES - budget;
            if (!complete) { result.GeometryComplete = false; return result; }
            result.Enclosure = enclosure;
            result.Control = Tightness(contacts, grid.Nodes.Count * step * step, result.Enclosure);
            return result;
        }

        public bool IsActionSafe(OpponentAiSnapshot state, OpponentAiAction action, bool protectEnclosure = true)
            => IsActionSafe(state, action, protectEnclosure, null, null);

        internal bool IsActionSafe(OpponentAiSnapshot state, OpponentAiAction action, bool protectEnclosure,
            OpponentAiRegionEvaluation beforeRegion, OpponentAiRegionEvaluation afterRegion)
        {
            if (state == null || (action.BattleId != 0 && action.BattleId != state.BattleId)) return false;
            if (action.Kind == OpponentAiActionKind.Wait || action.Kind == OpponentAiActionKind.Refresh) return true;
            BoardModel afterBoard;
            List<OpponentAiRobotSnapshot> afterRobots;
            int cost;
            if (!OpponentAiPlanner.TrySimulate(state, action, out afterBoard, out afterRobots, out cost)) return false;
            if (state.World == null || !state.World.Ball.IsLaunched) return true;
            var ball = state.World.Ball;
            if (!Finite(ball.Position) || !Finite(ball.Radius) || ball.Radius <= 0f || !Finite(ball.Speed) ||
                !Finite(ball.Direction)) return false;
            var beforeBoard = state.Board;
            var beforeGeometry = BuildGeometry(state, beforeBoard, state.Robots, GetStatic(state.World, beforeBoard),
                Owners(beforeBoard, state.Robots));
            var afterGeometry = BuildGeometry(state, afterBoard, afterRobots, GetStatic(state.World, afterBoard),
                Owners(afterBoard, afterRobots));
            if (!beforeGeometry.Valid || !afterGeometry.Valid) return false;
            var geometryChanged = false;
            var removesGeometry = false;
            var persistentRobots = new List<OpponentAiRobotSnapshot>();
            foreach (var before in state.Robots)
            {
                if (!before.IsOnBoard) continue;
                var same = false;
                foreach (var after in afterRobots)
                    if (after.InstanceId == before.InstanceId && after.Definition.Id == before.Definition.Id &&
                        after.Anchor.Equals(before.Anchor)) { same = true; break; }
                if (same) persistentRobots.Add(before);
                else { removesGeometry = true; geometryChanged = true; }
            }
            var horizon = state.World.Settings != null ? state.World.Settings.PredictionSeconds : .12f;
            if (!Finite(horizon) || horizon < 0f) return false;
            var end = ball.Position + ball.Direction.normalized * Math.Max(0f, ball.Speed) * horizon;
            if (!Finite(end)) return false;
            // 移动/合成后消失的旧轮廓不会再产生反弹，不能用它提前截断轨迹。
            var predictionGeometry = removesGeometry ? BuildGeometry(state, beforeBoard, persistentRobots,
                GetStatic(state.World, beforeBoard), Owners(beforeBoard, state.Robots)) : beforeGeometry;
            var first = predictionGeometry.Cast(ball.Position, end, ball.Radius, ball.IsBerserk);
            // 已存在的第一次碰撞后将反弹；不沿原方向穿墙推断逃逸。
            if (first.Found)
                end = Vector2.Lerp(ball.Position, end, Math.Max(0f, first.Time - .0001f));
            foreach (var after in afterRobots)
            {
                var same = false;
                foreach (var before in state.Robots)
                    if (before.IsOnBoard && before.InstanceId == after.InstanceId && before.Definition.Id == after.Definition.Id &&
                        before.Anchor.Equals(after.Anchor)) { same = true; break; }
                if (same) continue;
                geometryChanged = true;
                var afterTerrain = GetStatic(state.World, afterBoard);
                foreach (var cell in after.Definition.Cells)
                {
                    var x = after.Anchor.Value.Column + cell.Column;
                    var y = after.Anchor.Value.Row + cell.Row;
                    if (x < 0 || y < 0 || x >= afterBoard.Columns || y >= afterBoard.Rows ||
                        !afterTerrain.Pass[y * afterBoard.Columns + x]) return false;
                }
                // 对新增轮廓单独检查，暴走时也不允许在球/首次碰撞前轨迹上部署。
                var changed = new SpatialIndex(afterBoard.Columns, afterBoard.Rows);
                AddRobot(changed, state.World, after);
                if (!changed.Valid || changed.Cast(ball.Position, end, ball.Radius).Found) return false;
            }
            if (!geometryChanged || !protectEnclosure || !removesGeometry) return true;
            // 暴走球可能已穿出原圈，当前位置不能证明原围挡已经失效。保留所有原占格直到普通形态。
            if (ball.IsBerserk) return false;
            // 暴走数据只用于当前运动；布局保护始终按普通实体墙重新评估。
            var normalBall = new OpponentAiBallState(ball.Position, ball.Direction, ball.Speed, ball.Radius,
                ball.IsLaunched, false, ball.Combo, ball.RecoveryState);
            var normalWorld = new OpponentAiWorldSnapshot(normalBall, state.World.Glass, state.World.Colliders,
                state.World.BoardRevision, state.World.Phase, state.World.Settings);
            var normal = state.WithWorld(normalWorld);
            // 以真实扫掠证明能到达外沿，才可跳过围挡保护；面积大不能证明机器人环开放。
            if (beforeRegion == null && ProveOpen(beforeGeometry, GetStatic(state.World, beforeBoard), ball.Position, ball.Radius)) return true;
            beforeRegion = beforeRegion ?? Evaluate(normal, beforeBoard, state.Robots, true);
            // 未能确认旧布局的连通性时拒绝拆卸；短时扫掠已经独立完成。
            if (!beforeRegion.Valid || !beforeRegion.GeometryComplete) return false;
            if (!beforeRegion.IsClosed) return true;
            afterRegion = afterRegion ?? Evaluate(normal, afterBoard, afterRobots, true);
            if (!afterRegion.Valid || !afterRegion.GeometryComplete || !afterRegion.IsClosed ||
                afterRegion.Enclosure < beforeRegion.Enclosure ||
                afterRegion.Area > beforeRegion.Area || !afterRegion.Contains(end)) return false;
            if (afterRegion.Fine == null) return false;
            var sameGrid = beforeRegion.Fine != null && beforeRegion.Fine.Width == afterRegion.Fine.Width &&
                beforeRegion.Fine.Height == afterRegion.Fine.Height && beforeRegion.Fine.Step == afterRegion.Fine.Step;
            foreach (var node in afterRegion.Fine.Nodes)
                if (sameGrid ? !beforeRegion.Fine.Nodes.Contains(node) : !beforeRegion.Contains(afterRegion.Fine.Point(node))) return false;
            return true;
        }

        private static bool ProveOpen(Geometry geometry, StaticGeometry terrain, Vector2 origin, float radius)
        {
            if (!geometry.Clear(origin, radius, false)) return false;
            var columns = terrain.Index.Columns; var rows = terrain.Index.Rows;
            var ox = Mathf.FloorToInt(origin.x); var oy = Mathf.FloorToInt(origin.y);
            if (ox < 0 || oy < 0 || ox >= columns || oy >= rows) return false;
            var seed = oy * columns + ox;
            if (!terrain.Pass[seed]) return false;
            var queue = new int[terrain.Pass.Length];
            var baseline = new bool[queue.Length];
            var total = 1;
            queue[0] = seed; baseline[seed] = true;
            for (var head = 0; head < total; head++)
            {
                var node = queue[head];
                for (var d = 0; d < 4; d++)
                {
                    var x = node % columns + DX[d]; var y = node / columns + DY[d];
                    if (x < 0 || y < 0 || x >= columns || y >= rows) continue;
                    var next = y * columns + x;
                    if (baseline[next] || !terrain.Pass[next]) continue;
                    baseline[next] = true; queue[total++] = next;
                }
            }
            var visited = new bool[queue.Length];
            var free = new byte[queue.Length];
            var count = 0;
            for (var y = oy - 1; y <= oy + 1; y++)
                for (var x = ox - 1; x <= ox + 1; x++)
                {
                    if (x < 0 || y < 0 || x >= columns || y >= rows) continue;
                    var node = y * columns + x;
                    if (!baseline[node]) continue;
                    var point = new Vector2(x + .5f, y + .5f);
                    if (geometry.Clear(point, radius, false) && !geometry.Cast(origin, point, radius, false).Found)
                    { visited[node] = true; free[node] = 2; queue[count++] = node; }
                }
            for (var head = 0; head < count; head++)
            {
                var node = queue[head];
                var from = new Vector2(node % columns + .5f, node / columns + .5f);
                var xCell = node % columns; var yCell = node / columns;
                if (xCell == 0 && !geometry.Cast(from, new Vector2(radius + EPSILON, from.y), radius, false).Found ||
                    yCell == 0 && !geometry.Cast(from, new Vector2(from.x, radius + EPSILON), radius, false).Found ||
                    xCell == columns - 1 && !geometry.Cast(from, new Vector2(columns - radius - EPSILON, from.y), radius, false).Found ||
                    yCell == rows - 1 && !geometry.Cast(from, new Vector2(from.x, rows - radius - EPSILON), radius, false).Found)
                    return true;
                for (var d = 0; d < 4; d++)
                {
                    var x = node % columns + DX[d]; var y = node / columns + DY[d];
                    if (x < 0 || y < 0 || x >= columns || y >= rows) continue;
                    var next = y * columns + x;
                    if (!baseline[next] || visited[next] || free[next] == 1) continue;
                    var to = new Vector2(x + .5f, y + .5f);
                    if (free[next] == 0) free[next] = geometry.Clear(to, radius, false) ? (byte)2 : (byte)1;
                    if (free[next] == 2 && !geometry.Cast(from, to, radius, false).Found)
                    { visited[next] = true; queue[count++] = next; }
                }
            }
            return false;
        }

        private OpponentAiRegionEvaluation Coarse(BoardModel board, IReadOnlyList<OpponentAiRobotSnapshot> robots,
            StaticGeometry terrain, int[] owners, OpponentAiBallState ball)
        {
            var reachable = new bool[owners.Length];
            var result = new OpponentAiRegionEvaluation { Valid = true, Reachable = reachable, GeometryComplete = !ball.IsLaunched };
            Reserve(board, robots, terrain, owners, result);
            var queue = new int[owners.Length];
            var count = 0;
            var seed = CellIndex(board, ball.Position);
            if (ball.IsLaunched)
            {
                if (seed < 0 || !terrain.Pass[seed] || (owners[seed] != 0 && !ball.IsBerserk) ||
                    ball.RecoveryState == PlacementRecoveryState.Trapped)
                { result.Valid = false; return result; }
                reachable[seed] = true;
                queue[count++] = seed;
            }
            else
            {
                for (var i = 0; i < owners.Length; i++)
                    if (terrain.Pass[i] && owners[i] == 0) { reachable[i] = true; queue[count++] = i; }
            }
            var contacts = new Contacts();
            for (var head = 0; head < count; head++)
            {
                var cell = queue[head];
                var x = cell % board.Columns;
                var y = cell / board.Columns;
                for (var d = 0; d < 4; d++)
                {
                    var nx = x + DX[d]; var ny = y + DY[d];
                    if (nx < 0 || ny < 0 || nx >= board.Columns || ny >= board.Rows)
                    { contacts.WallLength++; continue; }
                    var next = ny * board.Columns + nx;
                    if (!terrain.Pass[next])
                    { contacts.Add(terrain.Glass[next] > 0f ? Hit.Glass(terrain.Glass[next]) : Hit.Wall(), 1f); continue; }
                    if (owners[next] != 0 && !ball.IsBerserk)
                    {
                        var owner = owners[next];
                        contacts.Add(owner > 0 ? Hit.Robot(Rate(robots[owner - 1])) : Hit.Wall(), 1f);
                        continue;
                    }
                    if (!reachable[next]) { reachable[next] = true; queue[count++] = next; }
                }
            }
            result.Area = count;
            result.GlassValue = contacts.Glass;
            result.RobotValue = contacts.Robot / Math.Max(1, count);
            if (ball.IsLaunched && !ball.IsBerserk && count > 0 && contacts.WallLength <= EPSILON &&
                contacts.RobotLength + contacts.GlassLength > EPSILON && ball.RecoveryState == PlacementRecoveryState.None)
            {
                result.Enclosure = contacts.GlassLength <= EPSILON || RobotOnlyCoarse(board, owners, seed, queue)
                    ? OpponentAiEnclosureKind.Robots : OpponentAiEnclosureKind.Glass;
            }
            if (!ball.IsLaunched) NoBallScore(robots, terrain, board.Columns, result);
            result.Control = Tightness(contacts, count, result.Enclosure);
            result.ContainsPoint = p => { var i = CellIndex(board, p); return i >= 0 && reachable[i]; };
            return result;
        }

        private static bool RobotOnlyCoarse(BoardModel board, int[] owners, int seed, int[] queue)
        {
            var visited = new bool[owners.Length];
            queue[0] = seed; visited[seed] = true;
            var count = 1;
            for (var head = 0; head < count; head++)
            {
                var index = queue[head];
                for (var d = 0; d < 4; d++)
                {
                    var x = index % board.Columns + DX[d]; var y = index / board.Columns + DY[d];
                    if (x < 0 || y < 0 || x >= board.Columns || y >= board.Rows) return false;
                    var next = y * board.Columns + x;
                    if (owners[next] < 0) return false;
                    if (owners[next] > 0 || visited[next]) continue;
                    visited[next] = true; queue[count++] = next;
                }
            }
            return true;
        }

        private static void NoBallScore(IReadOnlyList<OpponentAiRobotSnapshot> robots, StaticGeometry terrain,
            int columns, OpponentAiRegionEvaluation result)
        {
            var value = 0f;
            foreach (var robot in robots)
            {
                if (!robot.IsOnBoard) continue;
                var best = 0f;
                foreach (var offset in robot.Definition.Cells)
                {
                    var x = robot.Anchor.Value.Column + offset.Column;
                    var y = robot.Anchor.Value.Row + offset.Row;
                    foreach (var i in terrain.GlassCells)
                        best = Math.Max(best, terrain.Glass[i] / (1f + Math.Abs(x - i % columns) + Math.Abs(y - i / columns)));
                }
                value += best;
            }
            result.GlassValue = value;
        }

        private static void Reserve(BoardModel board, IReadOnlyList<OpponentAiRobotSnapshot> robots,
            StaticGeometry terrain, int[] owners, OpponentAiRegionEvaluation result)
        {
            var count = 0; var checkedCount = 0;
            var shapes = new HashSet<string>();
            foreach (var robot in robots)
            {
                if (!robot.IsOnBoard || !shapes.Add(ShapeKey(robot.Definition))) continue;
                for (var y = 0; y <= board.Rows - robot.Definition.Height; y++)
                    for (var x = 0; x <= board.Columns - robot.Definition.Width; x++)
                    {
                        checkedCount++;
                        var fits = true;
                        foreach (var offset in robot.Definition.Cells)
                        {
                            var cell = new BoardCoordinate(x + offset.Column, y + offset.Row);
                            var i = cell.Row * board.Columns + cell.Column;
                            if (!board.CanPlaceUnit(cell) || !terrain.Pass[i] || owners[i] != 0) { fits = false; break; }
                        }
                        if (fits) count++;
                    }
            }
            result.HasReserve = count > 0;
            result.Mobility = (float)count / Math.Max(1, checkedCount);
        }

        private static int[] Owners(BoardModel board, IReadOnlyList<OpponentAiRobotSnapshot> robots)
        {
            var owners = new int[board.Columns * board.Rows];
            for (var y = 0; y < board.Rows; y++)
                for (var x = 0; x < board.Columns; x++)
                    if (board.IsOccupied(new BoardCoordinate(x, y))) owners[y * board.Columns + x] = -1;
            for (var r = 0; r < robots.Count; r++)
            {
                var robot = robots[r];
                if (!robot.IsOnBoard) continue;
                foreach (var offset in robot.Definition.Cells)
                {
                    var cell = new BoardCoordinate(robot.Anchor.Value.Column + offset.Column, robot.Anchor.Value.Row + offset.Row);
                    if (board.IsInside(cell)) owners[cell.Row * board.Columns + cell.Column] = r + 1;
                }
            }
            return owners;
        }

        private StaticGeometry GetStatic(OpponentAiWorldSnapshot world, BoardModel board)
        {
            var pass = new bool[board.Columns * board.Rows];
            var glass = new float[pass.Length];
            for (var y = 0; y < board.Rows; y++)
                for (var x = 0; x < board.Columns; x++)
                {
                    var cell = new BoardCoordinate(x, y);
                    pass[y * board.Columns + x] = board.CanEnergyBallPass(cell);
                }
            if (world != null)
                foreach (var item in world.Glass)
                {
                    if (item.Durability <= 0 || !board.IsInside(item.Coordinate)) continue;
                    var i = item.Coordinate.Row * board.Columns + item.Coordinate.Column;
                    pass[i] = false; glass[i] = Math.Max(0f, item.Value);
                }
            if (_static != null && _static.Index.Columns == board.Columns && _static.Index.Rows == board.Rows &&
                Same(_static.Pass, pass) && Same(_static.Glass, glass)) return _static;
            var index = new SpatialIndex(board.Columns, board.Rows);
            for (var i = 0; i < pass.Length; i++)
                if (!pass[i]) index.Add(new Polygon(Rect(i % board.Columns, i / board.Columns,
                    i % board.Columns + 1f, i / board.Columns + 1f), Vector2.zero,
                    glass[i] > 0 ? Hit.Glass(glass[i]) : Hit.Wall()));
            var glassCells = new List<int>();
            for (var i = 0; i < glass.Length; i++) if (glass[i] > 0f) glassCells.Add(i);
            _static = new StaticGeometry { Pass = pass, Glass = glass, Index = index, GlassCells = glassCells };
            return _static;
        }

        private Geometry BuildGeometry(OpponentAiSnapshot state, BoardModel board,
            IReadOnlyList<OpponentAiRobotSnapshot> robots, StaticGeometry terrain, int[] owners)
        {
            var dynamic = new SpatialIndex(board.Columns, board.Rows);
            foreach (var robot in robots) if (robot.IsOnBoard) AddRobot(dynamic, state.World, robot);
            for (var i = 0; i < owners.Length; i++)
                if (owners[i] < 0) dynamic.Add(new Polygon(Rect(i % board.Columns, i / board.Columns,
                    i % board.Columns + 1f, i / board.Columns + 1f), Vector2.zero, Hit.Wall()));
            return new Geometry(terrain.Index, dynamic);
        }

        private void AddRobot(SpatialIndex index, OpponentAiWorldSnapshot world, OpponentAiRobotSnapshot robot)
        {
            if (!robot.IsOnBoard) return;
            if (world != null)
                foreach (var collider in world.Colliders)
                {
                    if (collider.DefinitionId != robot.Definition.Id || collider.Paths.Count == 0) continue;
                    var delta = new Vector2(robot.Anchor.Value.Column - collider.Anchor.Column, robot.Anchor.Value.Row - collider.Anchor.Row);
                    var actualPaths = new Vector2[collider.Paths.Count][];
                    for (var i = 0; i < actualPaths.Length; i++) actualPaths[i] = collider.Paths[i];
                    index.Add(new Polygon(actualPaths, delta, Hit.Robot(Rate(robot))));
                    return;
                }
            Vector2[][] paths;
            if (!_footprints.TryGetValue(robot.Definition, out paths))
            {
                paths = InsetFootprint(robot.Definition);
                if (_footprints.Count >= 128) _footprints.Clear();
                _footprints[robot.Definition] = paths;
            }
            var anchor = new Vector2(robot.Anchor.Value.Column, robot.Anchor.Value.Row);
            index.Add(new Polygon(paths, anchor, Hit.Robot(Rate(robot))));
        }

        private static Vector2[][] InsetFootprint(PlaceableDefinition definition)
        {
            var cells = new HashSet<BoardCoordinate>(definition.Cells);
            var edges = new Dictionary<BoardCoordinate, List<BoardCoordinate>>();
            foreach (var cell in definition.Cells)
            {
                var x = cell.Column; var y = cell.Row;
                // 有向边的左侧始终是实体；共边直接消去，凹角和内部孔洞也保留。
                if (!cells.Contains(new BoardCoordinate(x, y - 1))) Edge(edges, x, y, x + 1, y);
                if (!cells.Contains(new BoardCoordinate(x + 1, y))) Edge(edges, x + 1, y, x + 1, y + 1);
                if (!cells.Contains(new BoardCoordinate(x, y + 1))) Edge(edges, x + 1, y + 1, x, y + 1);
                if (!cells.Contains(new BoardCoordinate(x - 1, y))) Edge(edges, x, y + 1, x, y);
            }
            var result = new List<Vector2[]>();
            while (edges.Count > 0)
            {
                var start = default(BoardCoordinate);
                foreach (var key in edges.Keys) { start = key; break; }
                var current = start;
                var incoming = Vector2.right;
                var loop = new List<Vector2>();
                do
                {
                    List<BoardCoordinate> options;
                    if (!edges.TryGetValue(current, out options)) return Array.Empty<Vector2[]>();
                    var best = 0; var bestTurn = -1;
                    for (var i = 0; i < options.Count; i++)
                    {
                        var direction = new Vector2(options[i].Column - current.Column, options[i].Row - current.Row);
                        var cross = Cross(incoming, direction);
                        // 对角接触的两块在同一顶点有多个出边，优先左转以分别追踪轮廓。
                        var turn = cross > 0 ? 3 : Vector2.Dot(incoming, direction) > 0 ? 2 : cross < 0 ? 1 : 0;
                        if (turn > bestTurn) { best = i; bestTurn = turn; }
                    }
                    var next = options[best];
                    options.RemoveAt(best);
                    if (options.Count == 0) edges.Remove(current);
                    loop.Add(new Vector2(current.Column, current.Row));
                    incoming = new Vector2(next.Column - current.Column, next.Row - current.Row);
                    current = next;
                } while (!current.Equals(start));
                if (loop.Count < 3) return Array.Empty<Vector2[]>();
                var corners = new List<Vector2>();
                for (var i = 0; i < loop.Count; i++)
                    if (Math.Abs(Cross(loop[i] - loop[(i + loop.Count - 1) % loop.Count],
                        loop[(i + 1) % loop.Count] - loop[i])) > EPSILON) corners.Add(loop[i]);
                loop = corners;
                if (loop.Count < 3) return Array.Empty<Vector2[]>();
                var inset = new Vector2[loop.Count];
                for (var i = 0; i < loop.Count; i++)
                {
                    var before = (loop[i] - loop[(i + loop.Count - 1) % loop.Count]).normalized;
                    var after = (loop[(i + 1) % loop.Count] - loop[i]).normalized;
                    var first = new Vector2(-before.y, before.x);
                    var second = new Vector2(-after.y, after.x);
                    inset[i] = loop[i] + (first + second) * (INSET / Math.Max(.001f, 1f + Vector2.Dot(first, second)));
                }
                result.Add(inset);
            }
            return result.ToArray();
        }

        private static void Edge(Dictionary<BoardCoordinate, List<BoardCoordinate>> edges, int ax, int ay, int bx, int by)
        {
            var start = new BoardCoordinate(ax, ay);
            List<BoardCoordinate> list;
            if (!edges.TryGetValue(start, out list)) { list = new List<BoardCoordinate>(); edges.Add(start, list); }
            list.Add(new BoardCoordinate(bx, by));
        }

        private HashSet<int> Flood(FineRegion grid, Vector2 origin, float radius, bool ignoreRobots,
            bool optimistic, int goal, Contacts contacts, ref int budget, out bool complete, out bool reachedGoal,
            bool stopAtWall = false)
        {
            _sampleFree.Clear(); _queue.Clear();
            var nodes = new HashSet<int>();
            complete = true; reachedGoal = false;
            var ox = Mathf.FloorToInt(origin.x / grid.Step - .5f);
            var oy = Mathf.FloorToInt(origin.y / grid.Step - .5f);
            for (var y = oy - 1; y <= oy + 2; y++)
                for (var x = ox - 1; x <= ox + 2; x++)
                {
                    if (!grid.Inside(x, y)) continue;
                    var node = y * grid.Width + x;
                    bool free;
                    if (!Sample(grid, node, radius, ignoreRobots, ref budget, out free)) { complete = false; return nodes; }
                    if (free && (optimistic || !grid.Geometry.Cast(origin, grid.Point(node), radius, ignoreRobots).Found) && nodes.Add(node))
                        _queue.Add(node);
                }
            for (var head = 0; head < _queue.Count; head++)
            {
                if (goal > 0 && nodes.Count >= goal) { reachedGoal = true; return nodes; }
                var node = _queue[head];
                var from = grid.Point(node);
                for (var d = 0; d < 8; d++)
                {
                    var x = node % grid.Width + DX[d]; var y = node / grid.Width + DY[d];
                    if (!grid.Inside(x, y))
                    {
                        if (d < 4 && contacts != null) contacts.WallLength += grid.Step;
                        if (stopAtWall && contacts != null && contacts.WallLength > EPSILON) return nodes;
                        continue;
                    }
                    var next = y * grid.Width + x;
                    if (nodes.Contains(next)) continue;
                    bool free;
                    if (!Sample(grid, next, radius, ignoreRobots, ref budget, out free)) { complete = false; return nodes; }
                    var hit = default(Hit);
                    if (!optimistic && free || !free && d < 4 && contacts != null)
                        hit = grid.Geometry.Cast(from, grid.Point(next), radius, ignoreRobots);
                    if (free && !hit.Found) { nodes.Add(next); _queue.Add(next); }
                    else if (d < 4 && contacts != null) contacts.Add(hit, grid.Step);
                    if (stopAtWall && contacts != null && contacts.WallLength > EPSILON) return nodes;
                }
            }
            if (goal > 0 && nodes.Count >= goal) reachedGoal = true;
            return nodes;
        }

        private bool Sample(FineRegion grid, int node, float radius, bool ignoreRobots, ref int budget, out bool free)
        {
            if (_sampleFree.TryGetValue(node, out free)) return true;
            if (budget <= 0) { free = false; return false; }
            budget--;
            free = grid.Geometry.Clear(grid.Point(node), radius, ignoreRobots);
            _sampleFree.Add(node, free);
            return true;
        }

        internal sealed class FineRegion
        {
            internal readonly Geometry Geometry;
            internal readonly float Step, Radius;
            internal readonly int Width, Height;
            internal readonly bool IgnoreRobots;
            private readonly Vector2 _origin;
            internal HashSet<int> Nodes;
            internal FineRegion(Geometry geometry, float step, int width, int height, float radius, bool ignoreRobots, Vector2 origin)
            { Geometry = geometry; Step = step; Width = width; Height = height; Radius = radius; IgnoreRobots = ignoreRobots; _origin = origin; }
            internal bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
            internal Vector2 Point(int i) => new Vector2((i % Width + .5f) * Step, (i / Width + .5f) * Step);
            internal bool Contains(Vector2 point)
            {
                if (!Finite(point) || !Geometry.Clear(point, Radius, IgnoreRobots)) return false;
                if (!Geometry.Cast(_origin, point, Radius, IgnoreRobots).Found) return true;
                var px = Mathf.FloorToInt(point.x / Step - .5f); var py = Mathf.FloorToInt(point.y / Step - .5f);
                for (var y = py - 1; y <= py + 2; y++)
                    for (var x = px - 1; x <= px + 2; x++)
                    {
                        var i = y * Width + x;
                        if (Inside(x, y) && Nodes.Contains(i) && !Geometry.Cast(point, Point(i), Radius, IgnoreRobots).Found) return true;
                    }
                return false;
            }
        }

        internal sealed class Geometry
        {
            private readonly SpatialIndex _terrain, _robots;
            private readonly bool _withoutTerrain;
            internal bool Valid => _terrain.Valid && _robots.Valid;
            internal Geometry(SpatialIndex terrain, SpatialIndex robots, bool withoutTerrain = false)
            { _terrain = terrain; _robots = robots; _withoutTerrain = withoutTerrain; }
            internal Geometry WithoutTerrain() => new Geometry(_terrain, _robots, true);
            internal bool Clear(Vector2 point, float radius, bool ignoreRobots) => !Cast(point, point, radius, ignoreRobots).Found;
            internal Hit Cast(Vector2 from, Vector2 to, float radius, bool ignoreRobots)
            {
                var r = Math.Max(0f, radius - EPSILON);
                var hit = _withoutTerrain ? default(Hit) : _terrain.Cast(from, to, r);
                var dynamicHit = _robots.Cast(from, to, r, ignoreRobots);
                if (dynamicHit.Found && (!hit.Found || dynamicHit.Time < hit.Time)) hit = dynamicHit;
                if (from.x < r || from.y < r || from.x > _terrain.Columns - r || from.y > _terrain.Rows - r)
                    return Hit.Wall(0f);
                var delta = to - from;
                var t = 2f;
                if (to.x < r && delta.x < 0f) t = Math.Min(t, (r - from.x) / delta.x);
                if (to.y < r && delta.y < 0f) t = Math.Min(t, (r - from.y) / delta.y);
                if (to.x > _terrain.Columns - r && delta.x > 0f) t = Math.Min(t, (_terrain.Columns - r - from.x) / delta.x);
                if (to.y > _terrain.Rows - r && delta.y > 0f) t = Math.Min(t, (_terrain.Rows - r - from.y) / delta.y);
                if (t <= 1f && (!hit.Found || t < hit.Time)) hit = Hit.Wall(t);
                return hit;
            }
        }

        private sealed class StaticGeometry
        {
            internal bool[] Pass;
            internal float[] Glass;
            internal List<int> GlassCells;
            internal SpatialIndex Index;
        }

        internal sealed class SpatialIndex
        {
            internal readonly int Columns, Rows;
            internal bool Valid = true;
            private readonly List<Polygon>[] _bins;
            internal SpatialIndex(int columns, int rows) { Columns = columns; Rows = rows; _bins = new List<Polygon>[columns * rows]; }
            internal void Add(Polygon polygon)
            {
                if (!polygon.Valid) { Valid = false; return; }
                polygon.MinX = Clamp(Mathf.FloorToInt(polygon.Min.x), Columns);
                polygon.MinY = Clamp(Mathf.FloorToInt(polygon.Min.y), Rows);
                polygon.MaxX = Clamp(Mathf.FloorToInt(polygon.Max.x), Columns);
                polygon.MaxY = Clamp(Mathf.FloorToInt(polygon.Max.y), Rows);
                for (var y = polygon.MinY; y <= polygon.MaxY; y++)
                    for (var x = polygon.MinX; x <= polygon.MaxX; x++)
                    {
                        var i = y * Columns + x;
                        if (_bins[i] == null) _bins[i] = new List<Polygon>();
                        _bins[i].Add(polygon);
                    }
            }
            internal Hit Cast(Vector2 from, Vector2 to, float radius, bool ignoreRobots = false)
            {
                var min = Vector2.Min(from, to) - Vector2.one * radius;
                var max = Vector2.Max(from, to) + Vector2.one * radius;
                var x0 = Clamp(Mathf.FloorToInt(min.x), Columns); var x1 = Clamp(Mathf.FloorToInt(max.x), Columns);
                var y0 = Clamp(Mathf.FloorToInt(min.y), Rows); var y1 = Clamp(Mathf.FloorToInt(max.y), Rows);
                var hit = default(Hit);
                for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                    {
                        var bin = _bins[y * Columns + x];
                        if (bin == null) continue;
                        foreach (var polygon in bin)
                        {
                            // 同一多边形只在查询框与其包围框的首个交集格处理一次。
                            if (x != Math.Max(x0, polygon.MinX) || y != Math.Max(y0, polygon.MinY) ||
                                (ignoreRobots && polygon.Contact.Kind == 2) || polygon.Max.x < min.x || polygon.Min.x > max.x ||
                                polygon.Max.y < min.y || polygon.Min.y > max.y) continue;
                            var time = polygon.Cast(from, to, radius);
                            if (time <= 1f && (!hit.Found || time < hit.Time))
                            { hit = polygon.Contact; hit.Time = time; if (time <= 0f) return hit; }
                        }
                    }
                return hit;
            }
        }

        internal sealed class Polygon
        {
            private readonly Vector2[][] _paths;
            private readonly Vector2 _offset;
            internal readonly Hit Contact;
            internal Vector2 Min, Max;
            internal int MinX, MaxX, MinY, MaxY;
            internal bool Valid;
            internal Polygon(Vector2[] points, Vector2 offset, Hit contact)
                : this(new[] { points }, offset, contact) { }
            internal Polygon(Vector2[][] paths, Vector2 offset, Hit contact)
            {
                _paths = paths; _offset = offset; Contact = contact;
                if (paths == null || paths.Length == 0 || paths[0] == null || paths[0].Length < 3) return;
                Min = Max = paths[0][0] + offset;
                foreach (var points in paths)
                {
                    if (points == null || points.Length < 3) return;
                    var area = 0f;
                    for (var i = 0; i < points.Length; i++)
                    {
                        if (!Finite(points[i])) return;
                        Min = Vector2.Min(Min, points[i] + offset); Max = Vector2.Max(Max, points[i] + offset);
                        area += Cross(points[i], points[(i + 1) % points.Length]);
                    }
                    if (Math.Abs(area) <= EPSILON) return;
                }
                Valid = Finite(offset);
            }
            internal float Cast(Vector2 from, Vector2 to, float radius)
            {
                from -= _offset; to -= _offset;
                var inside = false;
                var earliest = 2f;
                foreach (var points in _paths)
                    for (var i = 0; i < points.Length; i++)
                    {
                        var a = points[i]; var b = points[(i + 1) % points.Length];
                        if ((a.y > from.y) != (b.y > from.y) && from.x < (b.x - a.x) * (from.y - a.y) / (b.y - a.y) + a.x)
                            inside = !inside;
                        earliest = Math.Min(earliest, Capsule(from, to - from, a, b, radius));
                    }
                return inside ? 0f : earliest;
            }
        }

        internal struct Hit
        {
            internal bool Found;
            internal int Kind;
            internal float Time, Value;
            internal static Hit Wall(float time = 0f) => new Hit { Found = true, Kind = 0, Time = time };
            internal static Hit Glass(float value) => new Hit { Found = true, Kind = 1, Value = value };
            internal static Hit Robot(float value) => new Hit { Found = true, Kind = 2, Value = value };
        }

        private sealed class Contacts
        {
            internal float Glass, Robot, WallLength, GlassLength, RobotLength;
            internal void Add(Hit hit, float length)
            {
                if (!hit.Found) return;
                if (hit.Kind == 1) { Glass += hit.Value * length; GlassLength += length; }
                else if (hit.Kind == 2) { Robot += hit.Value * length; RobotLength += length; }
                else WallLength += length;
            }
        }

        // 线段扫掠圆等价于点射线与边的胶囊相交，检查端帽和有限长侧边。
        private static float Capsule(Vector2 from, Vector2 delta, Vector2 a, Vector2 b, float radius)
        {
            var edge = b - a;
            var length = edge.magnitude;
            if (length < EPSILON) return Circle(from, delta, a, radius);
            var tangent = edge / length;
            var normal = new Vector2(-tangent.y, tangent.x);
            var along = Vector2.Dot(from - a, tangent);
            var distance = Vector2.Dot(from - a, normal);
            if (along >= 0f && along <= length && Math.Abs(distance) <= radius) return 0f;
            var first = Math.Min(Circle(from, delta, a, radius), Circle(from, delta, b, radius));
            var speed = Vector2.Dot(delta, normal);
            if (Math.Abs(speed) < EPSILON) return first;
            for (var sign = -1; sign <= 1; sign += 2)
            {
                var time = (sign * radius - distance) / speed;
                if (time < 0f || time > 1f) continue;
                var projection = along + time * Vector2.Dot(delta, tangent);
                if (projection >= 0f && projection <= length) first = Math.Min(first, time);
            }
            return first;
        }

        private static float Circle(Vector2 from, Vector2 delta, Vector2 center, float radius)
        {
            var offset = from - center;
            var c = Vector2.Dot(offset, offset) - radius * radius;
            if (c <= 0f) return 0f;
            var a = Vector2.Dot(delta, delta);
            if (a < EPSILON * EPSILON) return 2f;
            var b = Vector2.Dot(offset, delta);
            var discriminant = b * b - a * c;
            if (discriminant < 0f) return 2f;
            var time = (-b - Mathf.Sqrt(Math.Max(0f, discriminant))) / a;
            return time >= 0f && time <= 1f ? time : 2f;
        }

        private static float Tightness(Contacts contacts, float area, OpponentAiEnclosureKind enclosure)
        {
            if (enclosure == OpponentAiEnclosureKind.None) return 0f;
            var perimeter = contacts.RobotLength + contacts.GlassLength + contacts.WallLength;
            if (perimeter <= EPSILON || contacts.WallLength > EPSILON) return 0f;
            var barrierRatio = (contacts.RobotLength + .5f * contacts.GlassLength) / perimeter;
            var compactness = Math.Min(1f, 4f * Mathf.PI * area / (perimeter * perimeter));
            var closure = enclosure == OpponentAiEnclosureKind.Robots ? .65f :
                enclosure == OpponentAiEnclosureKind.Glass ? .30f : 0f;
            return barrierRatio * (.35f * compactness + closure);
        }
        private static float Rate(OpponentAiRobotSnapshot robot) => Math.Max(0, robot.Damage) / Math.Max(.01f, robot.TriggerInterval);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static int Clamp(int value, int size) => Math.Max(0, Math.Min(size - 1, value));
        private static int CellIndex(BoardModel board, Vector2 point)
        {
            if (!Finite(point) || point.x < 0f || point.y < 0f || point.x >= board.Columns || point.y >= board.Rows) return -1;
            return Mathf.FloorToInt(point.y) * board.Columns + Mathf.FloorToInt(point.x);
        }
        private static bool Same<T>(T[] left, T[] right)
        {
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++) if (!EqualityComparer<T>.Default.Equals(left[i], right[i])) return false;
            return true;
        }
        private static string ShapeKey(PlaceableDefinition definition)
        {
            var cells = new List<BoardCoordinate>(definition.Cells);
            cells.Sort((a, b) => a.Row == b.Row ? a.Column.CompareTo(b.Column) : a.Row.CompareTo(b.Row));
            var key = new StringBuilder();
            foreach (var cell in cells) key.Append(cell.Column).Append(',').Append(cell.Row).Append(';');
            return key.ToString();
        }
        private static Vector2[] Rect(float x0, float y0, float x1, float y1) => new[] {
            new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)
        };
    }
}
