using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameLogic
{
    public enum RobotDragPhase { Idle, Pressed, Dragging, Committing }

    /// <summary>通用已购机器人拖拽；购买由商店完成，盘面校验由 PlacementController 完成。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotDragController : MonoBehaviour
    {
        public const float LONG_PRESS_SECONDS = 0.2f;
        private BattleShopUI _ui;
        private BattleShopSceneController _host;
        private int _instanceId;
        private int _pointerId;
        private Vector2 _position;
        private float _pressedAt;
        private int _generation;
        private bool _ownsInteraction;
        private CancellationTokenSource _gestureCancellation;
        public RobotDragPhase Phase { get; private set; }
        public int? DraggedInstanceId => Phase == RobotDragPhase.Dragging || Phase == RobotDragPhase.Committing
            ? (int?)_instanceId : null;

        public void Initialize(BattleShopUI ui, BattleShopSceneController host)
        {
            Cancel();
            _ui = ui;
            _host = host;
        }

        public bool Press(int instanceId, int pointerId, Vector2 position, float now)
        {
            if (_host == null || _host.Context == null || Phase != RobotDragPhase.Idle)
                return false;
            var context = _host.Context;
            var generation = _generation;
            if (!context.TryBeginInteraction(BattleSide.Player, instanceId)) return false;
            // Changed listeners may cancel or end the battle before TryBeginInteraction returns.
            if (generation != _generation || _host == null || _host.Context != context || context.IsEnded)
            {
                context.CancelInteraction();
                return false;
            }
            _instanceId = instanceId;
            _pointerId = pointerId;
            _position = position;
            _pressedAt = now;
            _ownsInteraction = true;
            _gestureCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            Phase = RobotDragPhase.Pressed;
            return true;
        }

        public void Move(int pointerId, Vector2 position)
        {
            if (pointerId != _pointerId || Phase == RobotDragPhase.Idle || Phase == RobotDragPhase.Committing) return;
            _position = position;
            if (Phase == RobotDragPhase.Dragging) UpdatePreview();
        }

        public void Advance(float now)
        {
            if (_host == null || _host.Context == null || _host.Context.IsEnded) { Cancel(); return; }
            if (Phase == RobotDragPhase.Pressed && now - _pressedAt >= LONG_PRESS_SECONDS)
            {
                Entity entity;
                if (!_host.Context.Registry.TryGet(_instanceId, out entity) || !(entity is RobotEntity robot) ||
                    !_host.Placement.BeginDrag(robot.Definition, new BoardCoordinate(0, 0), BattleSide.Player))
                {
                    Cancel();
                    return;
                }
                Phase = RobotDragPhase.Dragging;
                _ui.ShowGhost(robot.Configuration.RobotIcon, _position);
                _ui.RefreshView();
                UpdatePreview();
            }
        }

        private void UpdatePreview()
        {
            _ui.MoveGhost(_position);
            _host.Placement.UpdateDrag(_position);
            _ui.SetStorageHighlighted(_ui.IsOverStorage(_position));
        }

        public void Release(int pointerId, Vector2 position)
        {
            if (Phase == RobotDragPhase.Idle || Phase == RobotDragPhase.Committing || pointerId != _pointerId) return;
            _position = position;
            if (Phase == RobotDragPhase.Pressed) { Cancel(); return; }
            SubmitAsync(++_generation, _gestureCancellation.Token).Forget();
        }

        private async UniTaskVoid SubmitAsync(int generation, CancellationToken cancellationToken)
        {
            Phase = RobotDragPhase.Committing;
            var result = ShopOperationResult.InvalidPlacement;
            try
            {
                if (_ui.IsOverStorage(_position)) result = _host.Context.TryStoreOrSwap(BattleSide.Player, _instanceId);
                else
                {
                    _host.Placement.UpdateDrag(_position);
                    BoardPlacementTarget target;
                    if (_host.Placement.TryGetDragTarget(out target))
                    {
                        var occupant = target.Board.Model.GetOccupant(target.Anchor);
                        int targetId;
                        if (int.TryParse(occupant, out targetId))
                            result = _host.Context.TryMerge(BattleSide.Player, _instanceId, targetId);
                        else if (target.Board.Model.Evaluate(target.Anchor, GetRobot().Definition).IsValid)
                            result = await _host.DeployRobotAsync(_instanceId, target, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                if (generation == _generation)
                {
                    Cancel();
                    if (_ui != null && result != ShopOperationResult.Success) _ui.ShowFailure(result);
                }
            }
        }

        private RobotEntity GetRobot()
        {
            Entity entity;
            return _host.Context.Registry.TryGet(_instanceId, out entity) ? entity as RobotEntity : null;
        }

        public void Cancel()
        {
            _generation++;
            var cancellation = _gestureCancellation;
            _gestureCancellation = null;
            cancellation?.Cancel();
            cancellation?.Dispose();
            if (_ownsInteraction && _host != null && _host.Context != null) _host.Context.CancelInteraction();
            _ownsInteraction = false;
            if (_host != null && _host.Placement != null) _host.Placement.CancelDrag();
            Phase = RobotDragPhase.Idle;
            if (_ui != null)
            {
                _ui.HideGhost();
                _ui.SetStorageHighlighted(false);
                _ui.RefreshView();
            }
        }
        private void OnApplicationFocus(bool focused) { if (!focused) Cancel(); }
        private void OnDisable() { Cancel(); }
        private void OnDestroy() { Cancel(); _ui = null; _host = null; }
    }
}
