using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>以作者11列9行区域容纳网格内容；不改相机和战场装饰。</summary>
    [DisallowMultipleComponent]
    public sealed class BattleMapLayoutController : MonoBehaviour
    {
        private Side _player;
        private Side _opponent;

        public void Configure(PlacementBoardView player, PlacementBoardView opponent,
            PhysicalCollisionBall playerBall, PhysicalCollisionBall opponentBall,
            PhysicalCollisionBall[] playerBerserkBalls = null,
            PhysicalCollisionBall[] opponentBerserkBalls = null)
        {
            if (player == null || opponent == null || playerBall == null || opponentBall == null ||
                player == opponent || playerBall == opponentBall)
                throw new ArgumentException("网格适配需要双方独立的棋盘与能源球。");
            if (player.transform.IsChildOf(opponent.transform) || opponent.transform.IsChildOf(player.transform) ||
                playerBall.transform.IsChildOf(player.transform) || playerBall.transform.IsChildOf(opponent.transform) ||
                opponentBall.transform.IsChildOf(player.transform) || opponentBall.transform.IsChildOf(opponent.transform) ||
                player.transform.IsChildOf(playerBall.transform) || player.transform.IsChildOf(opponentBall.transform) ||
                opponent.transform.IsChildOf(playerBall.transform) || opponent.transform.IsChildOf(opponentBall.transform) ||
                playerBall.transform.IsChildOf(opponentBall.transform) || opponentBall.transform.IsChildOf(playerBall.transform))
                throw new ArgumentException("棋盘不能相互嵌套，能源球刚体必须独立于网格缩放节点。");
            if (_player != null && _opponent != null && _player.Matches(player, playerBall) && _opponent.Matches(opponent, opponentBall)) return;
            ValidateAxes(player.transform);
            ValidateAxes(opponent.transform);
            Restore();
            _player = new Side(player, playerBall, playerBerserkBalls);
            _opponent = new Side(opponent, opponentBall, opponentBerserkBalls);
        }

        public bool TryGetReference(PlacementBoardView board, out Vector3 originalLocalScale, out float referenceCellSize)
        {
            var side = _player != null && _player.Board == board ? _player : _opponent != null && _opponent.Board == board ? _opponent : null;
            originalLocalScale = side != null ? side.OriginalScale : Vector3.one;
            referenceCellSize = side != null ? side.ReferenceCellSize : MapGridMath.REFERENCE_CELL_SIZE;
            return side != null;
        }

        public void Apply()
        {
            if (_player == null || _opponent == null) throw new InvalidOperationException("请先绑定双方棋盘和能源球。");
            var playerFit = _player.CalculateFit();
            var opponentFit = _opponent.CalculateFit();
            try { _player.Apply(playerFit); _opponent.Apply(opponentFit); }
            catch { Restore(); throw; }
        }

        public void Restore()
        {
            _player?.Restore();
            _opponent?.Restore();
        }

        private void OnDisable() { Restore(); }
        private void OnDestroy() { Restore(); }

        private static void ValidateAxes(Transform target)
        {
            var x = target.TransformVector(Vector3.right);
            var y = target.TransformVector(Vector3.up);
            if (!Finite(x.magnitude) || !Finite(y.magnitude) || x.magnitude <= 0f || y.magnitude <= 0f ||
                Mathf.Abs(x.magnitude - y.magnitude) > Mathf.Max(x.magnitude, y.magnitude) * 0.0001f ||
                Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > 0.0001f)
                throw new InvalidOperationException("棋盘世界XY已有非等比缩放或剪切，无法保持正方形格子：" + target.name);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class Side
        {
            public readonly PlacementBoardView Board;
            public readonly Vector3 OriginalScale;
            public readonly float ReferenceCellSize;
            private readonly PhysicalCollisionBall[] _balls;
            private readonly List<FixedChild> _fixedChildren = new List<FixedChild>();
            public Side(PlacementBoardView board, PhysicalCollisionBall ball,
                PhysicalCollisionBall[] additionalBalls)
            {
                Board = board;
                var balls = new List<PhysicalCollisionBall> { ball };
                if (additionalBalls != null)
                    foreach (var additionalBall in additionalBalls)
                        if (additionalBall != null && !balls.Contains(additionalBall)) balls.Add(additionalBall);
                _balls = balls.ToArray();
                OriginalScale = board.transform.localScale;
                ReferenceCellSize = board.ReferenceCellSize;
                // 当前场景的塔和路线是棋盘子节点；仅网格内容参与缩放。
                foreach (Transform child in board.transform)
                    if (child.name != "Slots" && child.name != "Boundary" && child.name != "Glasses" &&
                        child.GetComponent<RobotView>() == null)
                        _fixedChildren.Add(new FixedChild(child));
            }
            public bool Matches(PlacementBoardView board, PhysicalCollisionBall ball) =>
                Board == board && _balls.Length > 0 && _balls[0] == ball;
            public float CalculateFit() => MapGridMath.GetFitScale(Board.Columns, Board.Rows, Board.CellSize, ReferenceCellSize);
            public void Apply(float fit)
            {
                foreach (var child in _fixedChildren) child.Detach(Board.transform);
                Board.transform.localScale = new Vector3(OriginalScale.x * fit, OriginalScale.y * fit, OriginalScale.z);
                var ballScale = fit * Board.CellSize / ReferenceCellSize;
                foreach (var ball in _balls) ball.SetMapScale(ballScale);
                Board.SetBattleContentVisible(true);
            }
            public void Restore()
            {
                if (Board != null)
                {
                    Board.SetBattleContentVisible(false);
                    Board.transform.localScale = OriginalScale;
                    foreach (var child in _fixedChildren) child.Restore(Board.transform);
                }
                foreach (var ball in _balls)
                    if (ball != null) { ball.Stop(); ball.SetMapScale(1f); }
            }
        }

        private sealed class FixedChild
        {
            private readonly Transform _target;
            private readonly int _siblingIndex;
            private bool _detached;
            public FixedChild(Transform target) { _target = target; _siblingIndex = target.GetSiblingIndex(); }
            public void Detach(Transform board)
            {
                if (_target == null || _detached) return;
                _target.SetParent(board.parent, true);
                _detached = true;
            }
            public void Restore(Transform board)
            {
                if (_target == null || !_detached) return;
                _target.SetParent(board, true);
                _target.SetSiblingIndex(_siblingIndex);
                _detached = false;
            }
        }
    }
}
