using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    public sealed partial class BattleShopSceneController
    {
        [Header("场景塔：直接调整各自 Transform 的位置和缩放")]
        [SerializeField] private TowerView _playerTowerView;
        [SerializeField] private TowerView _opponentTowerView;
        [SerializeField] private int _towerConfigId = 3001;
        private TowerBattleSession _towers;
        private BattleSettlementSnapshot? _pendingTowerSettlement;
        public TowerBattleSession Towers => _towers;

        /// <summary>敌人提交已结算伤害的入口。旧对局、准备期、冻结和终局请求均拒绝。</summary>
        public DamageResult ApplyTowerDamage(long battleId, BattleSide targetSide, int damage)
        {
            if (!IsCombatRunning || _towers == null) return DamageResult.Ignored;
            return _towers.ApplyDamage(battleId, targetSide, damage);
        }

        public bool TryGetTowerTarget(BattleSide side, out int instanceId, out Transform target)
        {
            instanceId = 0;
            target = null;
            if (Context == null || Context.IsEnded || Context.IsEndRequested || _towers == null || _towers.IsEnded ||
                (side != BattleSide.Player && side != BattleSide.Opponent)) return false;
            var view = side == BattleSide.Player ? _playerTowerView : _opponentTowerView;
            var tower = side == BattleSide.Player ? _towers.PlayerTower : _towers.OpponentTower;
            if (view == null || !tower.IsAlive || view.InstanceId != tower.InstanceId) return false;
            instanceId = tower.InstanceId;
            target = view.transform;
            return true;
        }

        private void PrepareTowers(BattleShopContext context, Tables tables, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_playerTowerView == null || _opponentTowerView == null || _playerTowerView == _opponentTowerView)
                throw new InvalidOperationException("请为商店入口指定两个不同的场景 TowerView：我方塔和敌方塔。");
            var config = tables.TbTower.Get(_towerConfigId);
            if (string.IsNullOrWhiteSpace(config.Prefab)) throw new InvalidOperationException("Tower 配置缺少预制体资源名。");
            var session = new TowerBattleSession(context.BattleId, context.Registry, config.MaxHp, config.Id);
            _towers = session;
            session.SettlementRequested += OnTowerSettlementRequested;
            try
            {
                // 场景拥有视图；每局只更换实体绑定，不覆盖作者设置的 Transform。
                _playerTowerView.Bind(session.PlayerTower);
                _opponentTowerView.Bind(session.OpponentTower);
            }
            catch { ClearTowers(); throw; }
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
            if (_playerTowerView != null) _playerTowerView.Unbind();
            if (_opponentTowerView != null) _opponentTowerView.Unbind();
            if (session != null)
            {
                session.SettlementRequested -= OnTowerSettlementRequested;
                session.Dispose();
            }
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
