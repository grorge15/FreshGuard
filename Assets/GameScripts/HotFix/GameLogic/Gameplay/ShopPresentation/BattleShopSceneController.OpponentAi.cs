using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    public sealed partial class BattleShopSceneController
    {
        [SerializeField] private OpponentAiSettings _opponentAiSettings = new OpponentAiSettings();
        private OpponentAiController _opponentAi;
        // 可替换资源入口用于验证慢加载；生产环境默认走同一个 TEngine 资源模块。
        internal Func<string, Transform, CancellationToken, UniTask<GameObject>> OpponentViewLoader { get; set; }

        private void InitializeOpponentAi()
        {
            DisposeOpponentAi();
            _opponentAi = new OpponentAiController(_opponentAiSettings,
                () => Context == null || _opponentBoard == null || !_opponentBoard.IsInitialized
                    ? null : Context.CreateOpponentAiSnapshot(_opponentBoard.Model),
                ExecuteOpponentAiActionAsync, logException: e => Debug.LogException(e, this));
        }

        private void AdvanceOpponentAi(float deltaTime)
            => _opponentAi?.Advance(deltaTime, IsReady && !_frozen);

        private ShopOperationResult CheckOpponentAiBattle(BattleShopContext context, OpponentAiAction action,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || this == null || Context != context ||
                context == null || context.IsEnded || context.IsEndRequested) return ShopOperationResult.BattleEnded;
            if (context.BattleId != action.BattleId) return ShopOperationResult.WrongBattle;
            if (!IsReady || _frozen || _opponentBoard == null || !_opponentBoard.IsInitialized || context.IsBusy)
                return ShopOperationResult.Busy;
            return ShopOperationResult.Success;
        }

        private async UniTask<ShopOperationResult> ExecuteOpponentAiActionAsync(OpponentAiAction action,
            CancellationToken cancellationToken)
        {
            var context = Context;
            var gate = CheckOpponentAiBattle(context, action, cancellationToken);
            if (gate != ShopOperationResult.Success) return gate;
            if (action.Kind == OpponentAiActionKind.Refresh)
            {
                // 再检查快照中的已购槽，避免刷新销毁已购买但未部署的机器人。
                var snapshot = context.CreateOpponentAiSnapshot(_opponentBoard.Model);
                if (!OpponentAiPlanner.CanRefresh(snapshot)) return ShopOperationResult.InvalidLocation;
                return context.TryRefresh(BattleSide.Opponent);
            }
            var source = action.Kind == OpponentAiActionKind.PurchaseAndDeploy || action.Kind == OpponentAiActionKind.PurchaseAndMerge
                ? RobotDragSource.Offer(action.SlotId, action.OfferId) : RobotDragSource.Owned(action.SourceInstanceId);
            if (action.Kind == OpponentAiActionKind.PurchaseAndMerge || action.Kind == OpponentAiActionKind.Merge)
            {
                var mergeCheck = context.CheckMerge(BattleSide.Opponent, source, action.TargetInstanceId);
                if (mergeCheck != ShopOperationResult.Success) return mergeCheck;
                RobotDragSession mergeSession;
                gate = context.TryBeginDrag(BattleSide.Opponent, source, out mergeSession);
                if (gate != ShopOperationResult.Success) return gate;
                var mergeResult = CheckOpponentAiCommit(context, action, mergeSession, cancellationToken);
                if (mergeResult == ShopOperationResult.Success)
                    mergeResult = context.TryCommitMerge(mergeSession.InteractionId, action.TargetInstanceId);
                if (mergeResult != ShopOperationResult.Success) context.CancelInteraction(mergeSession.InteractionId);
                return mergeResult;
            }
            if (action.Kind != OpponentAiActionKind.PurchaseAndDeploy && action.Kind != OpponentAiActionKind.Move)
                return ShopOperationResult.InvalidArgument;

            GameConfig.robot.Robot config;
            int level;
            PlaceableDefinition definition;
            gate = context.GetDragContent(BattleSide.Opponent, source, out config, out level, out definition);
            if (gate != ShopOperationResult.Success) return gate;
            GameObject existing = null;
            if (action.Kind == OpponentAiActionKind.Move) _views.TryGetValue(action.SourceInstanceId, out existing);
            GameObject prepared = null;
            RobotDragSession session = null;
            try
            {
                // 资源 IO 不占用共享拖拽会话；只有加载完成、再次校验后才拿起并同步提交。
                if (existing == null)
                {
                    prepared = OpponentViewLoader != null
                        ? await OpponentViewLoader(config.RobotPrefab, _preparedRoot, cancellationToken)
                        : await GameModule.Resource.LoadGameObjectAsync(config.RobotPrefab, _preparedRoot, cancellationToken);
                    if (prepared == null) return ShopOperationResult.ResourceFailed;
                    prepared.SetActive(false);
                    var checkedView = prepared.GetComponent<RobotView>();
                    if (checkedView == null || checkedView.ConfigId != config.Id) return ShopOperationResult.ResourceFailed;
                }
                gate = CheckOpponentAiBattle(context, action, cancellationToken);
                if (gate != ShopOperationResult.Success) return gate;
                GameConfig.robot.Robot currentConfig;
                int currentLevel;
                gate = context.GetDragContent(BattleSide.Opponent, source, out currentConfig, out currentLevel, out definition);
                if (gate != ShopOperationResult.Success) return gate;
                if (currentConfig.Id != config.Id || currentLevel != level) return ShopOperationResult.InvalidLocation;
                gate = context.TryBeginDrag(BattleSide.Opponent, source, out session);
                if (gate != ShopOperationResult.Success) return gate;
                gate = CheckOpponentAiCommit(context, action, session, cancellationToken);
                if (gate != ShopOperationResult.Success) return gate;
                RobotEntity robot;
                gate = context.TryCommitDeploy(session.InteractionId, BattleSide.Opponent,
                    _opponentBoard.Model, action.Anchor, out robot);
                if (gate != ShopOperationResult.Success) return gate;
                if (Context != context || context.IsEnded || robot.IsRemoved) return ShopOperationResult.Success;
                BindDeployedRobotView(context, robot, existing != null ? existing : prepared,
                    new BoardPlacementTarget(_opponentBoard, action.Anchor));
                prepared = null;
                return ShopOperationResult.Success;
            }
            catch (OperationCanceledException) { return ShopOperationResult.BattleEnded; }
            catch (Exception e) { Debug.LogException(e, this); return ShopOperationResult.ResourceFailed; }
            finally
            {
                if (session != null && context.IsCurrentInteraction(session.InteractionId))
                    context.CancelInteraction(session.InteractionId);
                if (prepared != null)
                {
                    prepared.SetActive(false);
                    if (Application.isPlaying) Destroy(prepared);
                    else DestroyImmediate(prepared);
                }
            }
        }

        private ShopOperationResult CheckOpponentAiCommit(BattleShopContext context, OpponentAiAction action,
            RobotDragSession session, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || Context != context || context.IsEnded || context.IsEndRequested)
                return ShopOperationResult.BattleEnded;
            if (action.BattleId != context.BattleId) return ShopOperationResult.WrongBattle;
            if (_frozen || !IsReady || !context.IsCurrentInteraction(session.InteractionId)) return ShopOperationResult.Busy;
            return ShopOperationResult.Success;
        }

        private void DisposeOpponentAi()
        {
            _opponentAi?.Dispose();
            _opponentAi = null;
        }
    }
}
