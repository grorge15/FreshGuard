using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameLogic
{
    public sealed class OpponentAiDiagnostics
    {
        public int BrokenGlass { get; internal set; }
        public int NormalComboContacts { get; internal set; }
        public int BerserkCount { get; internal set; }
        public OpponentAiStrategyPhase Phase { get; internal set; }
        public OpponentAiAction LastAction { get; internal set; }
    }

    public sealed partial class BattleShopSceneController
    {
        [SerializeField] private OpponentAiStrategySettings _opponentAiStrategy = new OpponentAiStrategySettings();
        private int _aiInitialGlass = -1, _aiRevision, _aiSignature;
        private OpponentAiStrategyPhase _aiPhase;
        private PhysicalCollisionBall _aiObservedBall;
        private PlacementBoardsBootstrap _aiObservedBootstrap;
        private int _aiPreviousCombo;
        public OpponentAiDiagnostics OpponentAiDiagnostics { get; private set; } = new OpponentAiDiagnostics();

        private void ObserveOpponentAiWorld()
        {
            _aiInitialGlass = -1; _aiRevision = 0; _aiSignature = 0;
            _aiPhase = OpponentAiStrategyPhase.GlassExpansion;
            OpponentAiDiagnostics = new OpponentAiDiagnostics();
            _aiObservedBall = _bootstrap != null ? _bootstrap.OpponentBall : null;
            _aiObservedBootstrap = _bootstrap;
            if (_aiObservedBall != null)
            {
                _aiPreviousCombo = _aiObservedBall.ComboCount;
                _aiObservedBall.ComboChanged += OnAiComboChanged;
            }
            if (_aiObservedBootstrap != null) _aiObservedBootstrap.BerserkChanged += OnAiBerserkChanged;
        }

        private void OnAiComboChanged(PhysicalCollisionBall ball, int combo)
        {
            if (combo > _aiPreviousCombo && !ball.IsBerserk)
                OpponentAiDiagnostics.NormalComboContacts += combo - _aiPreviousCombo;
            _aiPreviousCombo = combo;
        }
        private void OnAiBerserkChanged(BattleSide side, bool active)
        { if (side == BattleSide.Opponent && active) OpponentAiDiagnostics.BerserkCount++; }

        private void UnobserveOpponentAiWorld()
        {
            if (_aiObservedBall != null) _aiObservedBall.ComboChanged -= OnAiComboChanged;
            if (_aiObservedBootstrap != null) _aiObservedBootstrap.BerserkChanged -= OnAiBerserkChanged;
            _aiObservedBall = null; _aiObservedBootstrap = null;
        }

        private Vector2 AiGridPoint(Vector3 world)
        {
            var point = _opponentBoard.transform.InverseTransformPoint(world);
            return new Vector2(point.x / _opponentBoard.CellSize + _opponentBoard.Model.Columns * .5f,
                point.y / _opponentBoard.CellSize + _opponentBoard.Model.Rows * .5f);
        }

        private OpponentAiColliderSnapshot CaptureAiCollider(RobotView view, string id, BoardCoordinate anchor)
        {
            var collider = view.GetPhysicalCollider();
            if (collider == null) return null;
            var paths = new List<Vector2[]>();
            for (var path = 0; path < collider.pathCount; path++)
            {
                var points = collider.GetPath(path);
                for (var i = 0; i < points.Length; i++)
                    points[i] = AiGridPoint(collider.transform.TransformPoint(points[i] + collider.offset));
                paths.Add(points);
            }
            return new OpponentAiColliderSnapshot(id, anchor, paths);
        }

        private OpponentAiSnapshot CaptureOpponentAiSnapshot()
        {
            if (Context == null || _opponentBoard == null || !_opponentBoard.IsInitialized) return null;
            var state = Context.CreateOpponentAiSnapshot(_opponentBoard.Model);
            var board = _opponentBoard.Model;
            var glass = new List<OpponentAiGlassState>();
            var colliders = new List<OpponentAiColliderSnapshot>();
            unchecked
            {
                // 布局版本只描述碰撞/机器人结构；纯金币或刷新商品不应使拖拽搜索失效。
                var signature = 17;
                for (var row = 0; row < board.Rows; row++)
                    for (var column = 0; column < board.Columns; column++)
                    {
                        var cell = new BoardCoordinate(column, row);
                        signature = signature * 31 + (board.CanPlaceUnit(cell) ? 1 : board.CanEnergyBallPass(cell) ? 2 : 3);
                        GlassView view;
                        Entity entity;
                        if (_opponentGlass == null || !_opponentGlass.TryGetView(cell, out view) ||
                            !Context.Registry.TryGet(view.InstanceId, out entity) || !(entity is GlassEntity value) || !value.IsAlive) continue;
                        int reward;
                        var kind = value.GlassKind == GlassKind.Normal ? ShopGlassKind.Normal : ShopGlassKind.Rainbow;
                        Context.Rules.GlassRewards.TryGetValue(kind, out reward);
                        glass.Add(new OpponentAiGlassState(cell, value.CurrentDurability, value.HitDurabilityLoss, reward));
                    }
                foreach (var robot in state.Robots)
                {
                    signature = signature * 31 + robot.InstanceId + robot.Level * 17 +
                        robot.Anchor.GetHashCode() + (int)robot.Location * 397;
                    if (!robot.IsOnBoard) continue;
                    GameObject instance;
                    if (!_views.TryGetValue(robot.InstanceId, out instance) || instance == null) continue;
                    var view = instance.GetComponent<RobotView>();
                    var collider = view != null ? CaptureAiCollider(view, robot.Definition.Id, robot.Anchor.Value) : null;
                    if (collider != null) colliders.Add(collider);
                }
                if (_aiInitialGlass < 0) _aiInitialGlass = glass.Count;
                var tuning = (_opponentAiStrategy ?? new OpponentAiStrategySettings()).Copy();
                if (glass.Count <= Math.Max(tuning.LowGlassMinimum, (int)Math.Ceiling(_aiInitialGlass * tuning.LowGlassRatio)))
                    _aiPhase = OpponentAiStrategyPhase.ComboControl;
                signature = signature * 31 + (int)_aiPhase;
                signature = signature * 31 + (_bootstrap != null && _bootstrap.OpponentBall != null && _bootstrap.OpponentBall.IsBerserk ? 1 : 0);
                if (signature != _aiSignature) { _aiSignature = signature; _aiRevision++; }
            }
            OpponentAiDiagnostics.BrokenGlass = Math.Max(0, _aiInitialGlass - glass.Count);
            OpponentAiDiagnostics.Phase = _aiPhase;
            var ball = _bootstrap != null ? _bootstrap.OpponentBall : null;
            var ballState = default(OpponentAiBallState);
            if (ball != null)
            {
                var position = ball.transform.position;
                var body = ball.GetComponent<Rigidbody2D>();
                if (body != null) position = body.position;
                var localVelocity = _opponentBoard.transform.InverseTransformVector(ball.CurrentDirection * ball.EffectiveSpeed);
                var localRadius = _opponentBoard.transform.InverseTransformVector(Vector3.right * ball.WorldRadius).magnitude;
                ballState = new OpponentAiBallState(AiGridPoint(position), ((Vector2)localVelocity).normalized,
                    localVelocity.magnitude / _opponentBoard.CellSize, localRadius / _opponentBoard.CellSize,
                    ball.IsLaunched, ball.IsBerserk, ball.ComboCount, ball.RecoveryState);
            }
            return state.WithWorld(new OpponentAiWorldSnapshot(ballState, glass, colliders, _aiRevision, _aiPhase, _opponentAiStrategy))
                .WithArmyState(_opponentAi != null && _opponentAi.ArmyEstablished);
        }

        private bool IsOpponentAiActionStillSafe(OpponentAiAction action, GameObject prepared = null)
        {
            var state = CaptureOpponentAiActionSnapshot(action, prepared);
            if (state == null) return false;
            var evaluator = new OpponentAiRegionEvaluator();
            if (!evaluator.IsActionSafe(state, action)) return false;
            return OpponentAiPlanner.IsActionUseful(state, action, evaluator);
        }

        private async UniTask<bool> IsOpponentAiActionStillSafeAsync(OpponentAiAction action,
            GameObject prepared, CancellationToken cancellationToken)
        {
            if (!Application.isPlaying) return IsOpponentAiActionStillSafe(action, prepared);
            var state = CaptureOpponentAiActionSnapshot(action, prepared);
            if (state == null) return false;
            OpponentAiActionProof proof;
            try
            {
                // 此lambda只持有纯数据；不从工作线程访问场景、Transform、物理组件或资源模块。
                proof = await UniTask.RunOnThreadPool(() => {
                    cancellationToken.ThrowIfCancellationRequested();
                    return OpponentAiActionProof.Create(state, action);
                });
            }
            finally { await UniTask.SwitchToMainThread(); }
            if (cancellationToken.IsCancellationRequested || this == null || Context == null || Context.IsEnded ||
                Context.BattleId != action.BattleId) return false;
            UnityEngine.Profiling.Profiler.BeginSample("OpponentAI.CommitValidation");
            try { return proof != null && proof.CanCommit(CaptureOpponentAiActionSnapshot(action, prepared)); }
            finally { UnityEngine.Profiling.Profiler.EndSample(); }
        }

        private OpponentAiSnapshot CaptureOpponentAiActionSnapshot(OpponentAiAction action, GameObject prepared)
        {
            var state = CaptureOpponentAiSnapshot();
            if (state == null) return null;
            if (prepared == null && action.Kind == OpponentAiActionKind.DeployOwned)
                _views.TryGetValue(action.SourceInstanceId, out prepared);
            // 新购视图加载后用其真实内缩轮廓复核，不能只依赖占格形状的近似。
            if (prepared != null && (action.Kind == OpponentAiActionKind.PurchaseAndDeploy || action.Kind == OpponentAiActionKind.DeployOwned))
            {
                var view = prepared.GetComponent<RobotView>();
                var target = new BoardPlacementTarget(_opponentBoard, action.Anchor);
                prepared.transform.SetParent(target.EntityParent, false);
                prepared.transform.position = target.AnchorWorldPosition;
                prepared.transform.localRotation = Quaternion.identity;
                view.ApplyBoardScale(_opponentBoard);
                var paths = new List<OpponentAiColliderSnapshot>(state.World.Colliders);
                var definitionId = "ai:offer:" + action.OfferId;
                if (action.Kind == OpponentAiActionKind.DeployOwned)
                    foreach (var owned in state.Robots)
                        if (owned.InstanceId == action.SourceInstanceId) { definitionId = owned.Definition.Id; break; }
                var captured = CaptureAiCollider(view, definitionId, action.Anchor);
                if (captured != null) paths.Add(captured);
                state = state.WithWorld(new OpponentAiWorldSnapshot(state.World.Ball, state.World.Glass,
                    paths, state.BoardRevision, state.StrategyPhase, state.World.Settings));
            }
            return state;
        }
    }
}
