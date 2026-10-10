using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    public sealed partial class BattleShopSceneController
    {
        [SerializeField] private string _towerPrefab = "Tower";
        [SerializeField] private int _towerConfigId = 3001;
        [SerializeField] private int _towerMaxHp = 100;
        private TowerBattleSession _towers;
        private readonly List<GameObject> _towerViews = new List<GameObject>();
        private BattleSettlementSnapshot? _pendingTowerSettlement;
        public TowerBattleSession Towers => _towers;

        /// <summary>敌人提交已结算伤害的入口。旧对局、准备期、冻结和终局请求均拒绝。</summary>
        public DamageResult ApplyTowerDamage(long battleId, BattleSide targetSide, int damage)
        {
            if (!IsCombatRunning || _towers == null) return DamageResult.Ignored;
            return _towers.ApplyDamage(battleId, targetSide, damage);
        }

        private async UniTask PrepareTowersAsync(BattleShopContext context, CancellationToken cancellationToken)
        {
            if (!GameModule.Resource.CheckLocationValid(_towerPrefab))
                throw new InvalidOperationException("塔预制体未加入 YooAsset 收集：" + _towerPrefab);
            var session = new TowerBattleSession(context.BattleId, context.Registry, _towerMaxHp, _towerConfigId);
            _towers = session;
            session.SettlementRequested += OnTowerSettlementRequested;
            var parent = _preparedRoot;
            await PrepareTowerViewAsync(context, session, session.PlayerTower, _playerBoard, parent, cancellationToken);
            await PrepareTowerViewAsync(context, session, session.OpponentTower, _opponentBoard, parent, cancellationToken);
        }

        private async UniTask PrepareTowerViewAsync(BattleShopContext context, TowerBattleSession session,
            TowerEntity tower, PlacementBoardView board, Transform parent, CancellationToken cancellationToken)
        {
            GameObject instance = null;
            try
            {
                instance = await GameModule.Resource.LoadGameObjectAsync(_towerPrefab, parent, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (this == null || Context != context || _towers != session || context.IsEnded || session.IsEnded)
                    throw new OperationCanceledException(cancellationToken);
                if (instance == null) throw new InvalidOperationException("塔预制体加载失败。");
                instance.SetActive(false);
                var view = instance.GetComponent<TowerView>();
                if (view == null) throw new InvalidOperationException("塔预制体缺少 TowerView。");
                view.Bind(tower);
                instance.name = tower.Side == BattleSide.Player ? "PlayerTower" : "OpponentTower";
                instance.transform.SetParent(board.transform, false);
                instance.transform.localPosition = new Vector3(0f, -board.Size.y * 0.5f - 0.65f, 0f);
                _towerViews.Add(instance);
                instance = null;
            }
            finally { if (instance != null) Destroy(instance); }
        }

        private void OnTowerSettlementRequested(BattleSettlementSnapshot snapshot)
        {
            if (Context == null || snapshot.BattleId != Context.BattleId || _pendingTowerSettlement.HasValue) return;
            _pendingTowerSettlement = snapshot;
            Context.RequestEndBattle();
            _running = false;
            _bootstrap?.StopBalls();
        }

        private void ClearTowers()
        {
            var session = _towers;
            _towers = null;
            _pendingTowerSettlement = null;
            if (session != null)
            {
                session.SettlementRequested -= OnTowerSettlementRequested;
                session.Dispose();
            }
            foreach (var view in _towerViews) if (view != null) Destroy(view);
            _towerViews.Clear();
        }

        private static async UniTask NotifySettlementAfterCleanupAsync(BattleShopContext context, BattleSettlementSnapshot snapshot)
        {
            // 销毁入口时商店可能仍在事务通知中；其 finally 会完成 EndBattle。
            // 此通知独立于已销毁的 MonoBehaviour，避免场景切换丢失结算事实。
            if (context != null && !context.IsEnded) await UniTask.WaitUntil(() => context.IsEnded);
            GameEvent.Send<BattleSettlementSnapshot>(BattleSettlementEvents.Requested, snapshot);
        }
    }
}
