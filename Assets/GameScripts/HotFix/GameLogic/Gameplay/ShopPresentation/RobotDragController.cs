using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameLogic
{
    public enum RobotDragPhase { Idle, Pressed, Dragging, Committing }

    /// <summary>四来源共用的指针会话；拿起和最终购买均由上下文校验。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotDragController : MonoBehaviour
    {
        public const float LONG_PRESS_SECONDS = 0.2f;
        private BattleShopUI _ui;
        private BattleShopSceneController _host;
        private BattleShopContext _context;
        private RobotDragSource _source;
        private RobotDragSession _session;
        private BoardCoordinate _grabbedCell;
        private int _pointerId;
        private Vector2 _position;
        private float _pressedAt;
        private int _generation;
        private CancellationTokenSource _gestureCancellation;
        public RobotDragPhase Phase { get; private set; }
        private bool IsPickedUp => Phase == RobotDragPhase.Dragging || Phase == RobotDragPhase.Committing;
        public int? DraggedInstanceId => IsPickedUp && _source.Kind == RobotDragSourceKind.OwnedRobot ? (int?)_source.InstanceId : null;
        public long? DraggedOfferId => IsPickedUp && _source.Kind == RobotDragSourceKind.ShopOffer ? (long?)_source.OfferId : null;

        public void Initialize(BattleShopUI ui, BattleShopSceneController host)
        { Cancel(); _ui = ui; _host = host; }

        public bool Press(int instanceId, int pointerId, Vector2 position, float now)
        { return Press(RobotDragSource.Owned(instanceId), pointerId, position, now, new BoardCoordinate(0, 0)); }

        public bool Press(RobotDragSource source, int pointerId, Vector2 position, float now, BoardCoordinate grabbedCell)
        {
            if (_host == null || !_host.IsReady || _ui == null || Phase != RobotDragPhase.Idle || _host.Context.IsBusy) return false;
            GameConfig.robot.Robot config;
            int level;
            PlaceableDefinition definition;
            var context = _host.Context;
            var result = context.GetDragContent(BattleSide.Player, source, out config, out level, out definition);
            if (result != ShopOperationResult.Success) { _ui.ShowFailure(result); return false; }
            _context = context;
            _source = source;
            _grabbedCell = grabbedCell;
            _pointerId = pointerId;
            _position = position;
            _pressedAt = now;
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
            if (Phase == RobotDragPhase.Idle) return;
            if (_host == null || _context == null || _host.Context != _context || _context.IsEnded)
            { Cancel(); return; }
            if (Phase == RobotDragPhase.Pressed && now - _pressedAt >= LONG_PRESS_SECONDS)
            {
                var generation = _generation;
                var context = _context;
                RobotDragSession session;
                var result = context.TryBeginDrag(BattleSide.Player, _source, out session);
                // 通知监听器可能同步关闭窗口、终局或取消本次手势。
                if (generation != _generation || _host == null || _host.Context != context || context.IsEnded)
                { if (session != null) context.CancelInteraction(session.InteractionId); return; }
                if (result != ShopOperationResult.Success) { Cancel(); _ui?.ShowFailure(result); return; }
                _session = session;
                GameConfig.robot.Robot config;
                int level;
                PlaceableDefinition definition;
                result = context.GetDragContent(BattleSide.Player, _source, out config, out level, out definition);
                if (result != ShopOperationResult.Success || !_host.Placement.BeginDrag(definition, _grabbedCell, BattleSide.Player))
                { Cancel(); return; }
                Phase = RobotDragPhase.Dragging;
                _host.SetPlayerDragging(true);
                _ui.ShowGhost(config.RobotIcon, level, _position);
                _ui.RefreshView();
            }
            if (Phase == RobotDragPhase.Dragging) UpdatePreview();
        }

        private void UpdatePreview()
        {
            _ui.MoveGhost(_position);
            _host.Placement.UpdateDrag(_position);
            _host.UpdateMergeCandidates(_source);
            var storage = _ui.IsOverStorage(_position);
            _ui.SetStorageHighlighted(storage);
            int occupant;
            if (storage || (_host.Placement.TryGetDragOccupant(out occupant) &&
                _context.CheckMerge(BattleSide.Player, _source, occupant) == ShopOperationResult.Success))
                _host.Placement.ClearPlacementPreview();
        }

        public void Release(int pointerId, Vector2 position)
        {
            if (Phase == RobotDragPhase.Idle || Phase == RobotDragPhase.Committing || pointerId != _pointerId) return;
            _position = position;
            // PointerUp可能先于本帧Update，按实际按住时长补做拿起判定。
            if (Phase == RobotDragPhase.Pressed) Advance(Time.unscaledTime);
            if (Phase == RobotDragPhase.Idle || Phase == RobotDragPhase.Committing) return;
            if (Phase == RobotDragPhase.Pressed) { Cancel(); return; }
            SubmitAsync(++_generation, _gestureCancellation.Token).Forget();
        }

        private async UniTaskVoid SubmitAsync(int generation, CancellationToken cancellationToken)
        {
            Phase = RobotDragPhase.Committing;
            var context = _context;
            var session = _session;
            var host = _host;
            var result = ShopOperationResult.InvalidPlacement;
            try
            {
                if (session == null || !context.IsCurrentInteraction(session.InteractionId)) return;
                RobotEntity robot;
                if (_ui.IsOverStorage(_position)) result = context.TryCommitStorage(session.InteractionId, out robot);
                else
                {
                    host.Placement.UpdateDrag(_position);
                    int targetId;
                    BoardPlacementTarget target;
                    if (host.Placement.TryGetDragOccupant(out targetId))
                        result = context.TryCommitMerge(session.InteractionId, targetId);
                    else if (host.Placement.TryGetDragTarget(out target))
                        result = await host.DeployDragAsync(session, target, cancellationToken);
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

        public void Cancel()
        {
            _generation++;
            var cancellation = _gestureCancellation;
            _gestureCancellation = null;
            var session = _session;
            var context = _context;
            _session = null;
            _context = null;
            Phase = RobotDragPhase.Idle;
            cancellation?.Cancel();
            cancellation?.Dispose();
            if (_host != null && (context == null || _host.Context == context))
            {
                _host.Placement?.CancelDrag();
                _host.ClearMergeCandidates();
                _host.SetPlayerDragging(false);
            }
            if (_ui != null)
            { _ui.HideGhost(); _ui.SetStorageHighlighted(false); _ui.RefreshView(); }
            // 最后通知Context；监听器重入或重启后，不再清理其新手势/新局展示。
            if (session != null) context?.CancelInteraction(session.InteractionId);
        }
        private void OnApplicationFocus(bool focused) { if (!focused) Cancel(); }
        private void OnDisable() { Cancel(); }
        private void OnDestroy() { Cancel(); _ui = null; _host = null; }
    }
}
