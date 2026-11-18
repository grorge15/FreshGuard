using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    public enum PlacementRecoveryState { None, Escaping, Trapped }

    /// <summary>只负责格子选择和恢复轨迹；刚体、攻击及碰撞保护由球组件管理。</summary>
    public sealed class BallPlacementRecovery
    {
        private readonly HashSet<int> _placements = new HashSet<int>();
        private bool _towardB;
        private float _travelSinceTurn;
        private BoardCoordinate _edgeCell;
        private int _edge;
        public PlacementRecoveryState State { get; private set; }
        public PlacementBoardView Board { get; private set; }
        public BoardCoordinate OriginCell { get; private set; }
        public BoardCoordinate TargetCell { get; private set; }
        public Vector2 TargetPosition { get; private set; }
        public Vector2 EndpointA { get; private set; }
        public Vector2 EndpointB { get; private set; }
        public bool HasValidTrappedEdge => State == PlacementRecoveryState.Trapped && !IsInternalEdge(_edgeCell, _edge);
        public Vector2 Destination => State == PlacementRecoveryState.Escaping ? TargetPosition :
            _towardB ? EndpointB : EndpointA;

        public void Begin(PlacementBoardView board, Vector2 center)
        {
            if (State != PlacementRecoveryState.None) return;
            if (board == null || !board.TryGetCoordinate(center, out var cell))
                throw new InvalidOperationException("重叠恢复需要已初始化棋盘内的球心。");
            Board = board;
            OriginCell = cell;
            State = PlacementRecoveryState.Escaping;
        }

        public bool RegisterPlacement(int colliderId) => _placements.Add(colliderId);

        public bool TrySelectTarget(Vector2 center, Func<Vector2, bool> canUse)
        {
            if (Board == null || !Board.IsInitialized || Board.Model == null)
                throw new InvalidOperationException("重叠恢复的棋盘已失效。");
            bool found = false;
            float best = float.PositiveInfinity;
            // 只检查原格及其八邻格；原格可能本来就空闲，只有球边缘与邻格机器人重叠。
            // 原格同样经过占用和物理可达检查，不改变用于卡顿边线定位的OriginCell。
            // 行、列顺序也是等距离时的稳定优先级。
            for (int row = OriginCell.Row - 1; row <= OriginCell.Row + 1; row++)
                for (int column = OriginCell.Column - 1; column <= OriginCell.Column + 1; column++)
                {
                    var cell = new BoardCoordinate(column, row);
                    if (!Board.Model.IsInside(cell) || !Board.Model.CanEnergyBallPass(cell) || Board.Model.IsOccupied(cell)) continue;
                    Vector2 position = Board.GetCellWorldPosition(cell);
                    float distance = (position - center).sqrMagnitude;
                    if (distance >= best - 0.000001f || !canUse(position)) continue;
                    found = true; best = distance; TargetCell = cell; TargetPosition = position;
                }
            if (found) State = PlacementRecoveryState.Escaping;
            return found;
        }

        public Vector2 EnterTrapped(int edge, bool towardB)
        {
            if (edge < 0 || edge > 3) throw new ArgumentOutOfRangeException(nameof(edge));
            if (HasValidTrappedEdge) return (EndpointA + EndpointB) * 0.5f;
            _edgeCell = OriginCell;
            _edge = edge;
            if (IsInternalEdge(_edgeCell, _edge))
            {
                // 原格可能被多格机器人包在内部，需在该机器人的全部占格中找最近外边。
                var occupant = Board.Model.GetOccupant(OriginCell);
                var origin = Board.GetCellLocalPosition(OriginCell);
                float best = float.PositiveInfinity;
                for (int row = 0; row < Board.Model.Rows; row++)
                    for (int column = 0; column < Board.Model.Columns; column++)
                    {
                        var cell = new BoardCoordinate(column, row);
                        if (Board.Model.GetOccupant(cell) != occupant) continue;
                        for (int offset = 0; offset < 4; offset++)
                        {
                            int candidate = (edge + offset) % 4;
                            if (IsInternalEdge(cell, candidate)) continue;
                            GetEdge(cell, candidate, out var a, out var b);
                            float distance = ((a + b) * 0.5f - origin).sqrMagnitude;
                            if (distance >= best) continue;
                            best = distance; _edgeCell = cell; _edge = candidate;
                        }
                    }
            }
            GetEdge(_edgeCell, _edge, out var start, out var end);
            EndpointA = Board.transform.TransformPoint(start);
            EndpointB = Board.transform.TransformPoint(end);
            _towardB = towardB;
            _travelSinceTurn = 0f;
            State = PlacementRecoveryState.Trapped;
            return (EndpointA + EndpointB) * 0.5f;
        }

        private bool IsInternalEdge(BoardCoordinate cell, int edge)
        {
            var occupant = Board.Model.GetOccupant(cell);
            if (string.IsNullOrEmpty(occupant)) return false;
            var neighbor = new BoardCoordinate(cell.Column + (edge == 1 ? 1 : edge == 3 ? -1 : 0),
                cell.Row + (edge == 2 ? 1 : edge == 0 ? -1 : 0));
            return Board.Model.GetOccupant(neighbor) == occupant;
        }

        private void GetEdge(BoardCoordinate cell, int edge, out Vector3 a, out Vector3 b)
        {
            var center = Board.GetCellLocalPosition(cell);
            float half = Board.CellSize * 0.5f;
            switch (edge)
            {
                case 0: a = center + new Vector3(-half, -half); b = center + new Vector3(half, -half); break;
                case 1: a = center + new Vector3(half, -half); b = center + new Vector3(half, half); break;
                case 2: a = center + new Vector3(-half, half); b = center + new Vector3(half, half); break;
                case 3: a = center + new Vector3(-half, -half); b = center + new Vector3(-half, half); break;
                default: throw new ArgumentOutOfRangeException(nameof(edge));
            }
        }

        public void Travel(float distance) { _travelSinceTurn += distance; }

        public bool Turn(float minimumTravel)
        {
            _towardB = !_towardB;
            bool count = _travelSinceTurn > minimumTravel;
            _travelSinceTurn = 0f;
            return count;
        }

        public void Clear()
        {
            State = PlacementRecoveryState.None;
            Board = null;
            _placements.Clear();
            _travelSinceTurn = 0f;
        }
    }
}
