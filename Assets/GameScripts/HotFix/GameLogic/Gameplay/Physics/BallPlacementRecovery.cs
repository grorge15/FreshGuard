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
        public PlacementRecoveryState State { get; private set; }
        public PlacementBoardView Board { get; private set; }
        public BoardCoordinate OriginCell { get; private set; }
        public BoardCoordinate TargetCell { get; private set; }
        public Vector2 TargetPosition { get; private set; }
        public Vector2 EndpointA { get; private set; }
        public Vector2 EndpointB { get; private set; }
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
            // 行、列顺序也是等距离时的稳定优先级；严格只遍历原格周围八格。
            for (int row = OriginCell.Row - 1; row <= OriginCell.Row + 1; row++)
                for (int column = OriginCell.Column - 1; column <= OriginCell.Column + 1; column++)
                {
                    if (column == OriginCell.Column && row == OriginCell.Row) continue;
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
            if (State == PlacementRecoveryState.Trapped) return (EndpointA + EndpointB) * 0.5f;
            var center = Board.GetCellLocalPosition(OriginCell);
            float half = Board.CellSize * 0.5f;
            Vector3 a, b;
            switch (edge)
            {
                case 0: a = center + new Vector3(-half, -half); b = center + new Vector3(half, -half); break;
                case 1: a = center + new Vector3(half, -half); b = center + new Vector3(half, half); break;
                case 2: a = center + new Vector3(-half, half); b = center + new Vector3(half, half); break;
                case 3: a = center + new Vector3(-half, -half); b = center + new Vector3(-half, half); break;
                default: throw new ArgumentOutOfRangeException(nameof(edge));
            }
            EndpointA = Board.transform.TransformPoint(a);
            EndpointB = Board.transform.TransformPoint(b);
            _towardB = towardB;
            _travelSinceTurn = 0f;
            State = PlacementRecoveryState.Trapped;
            return (EndpointA + EndpointB) * 0.5f;
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
