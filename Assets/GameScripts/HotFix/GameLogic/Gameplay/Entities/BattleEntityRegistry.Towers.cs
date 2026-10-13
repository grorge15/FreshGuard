using System;
using System.Collections.Generic;

namespace GameLogic
{
    public sealed partial class BattleEntityRegistry
    {
        private readonly Dictionary<BattleSide, int> _towerBySide = new Dictionary<BattleSide, int>();
        private bool _towerDamageStopped;
        private bool _towerDamageBatchInProgress;

        public TowerEntity CreateTower(int configId, BattleSide side, int maxHp)
        {
            ThrowIfDisposed();
            EntityValidation.RequirePositive(configId, nameof(configId));
            EntityValidation.RequireSide(side);
            EntityValidation.RequirePositive(maxHp, nameof(maxHp));
            if (_towerBySide.ContainsKey(side)) throw new InvalidOperationException("每个阵营只能登记一座塔。");
            var nextId = checked(_nextInstanceId + 1);
            var tower = new TowerEntity(_nextInstanceId, configId, side, maxHp);
            _entities.Add(tower.InstanceId, tower);
            _towerBySide.Add(side, tower.InstanceId);
            _nextInstanceId = nextId;
            return tower;
        }

        public bool TryGetTower(BattleSide side, out TowerEntity tower)
        {
            tower = null;
            int instanceId;
            Entity entity;
            if (!_towerBySide.TryGetValue(side, out instanceId) || !TryGet(instanceId, out entity)) return false;
            tower = entity as TowerEntity;
            return tower != null;
        }

        /// <summary>伤害为已结算的正整数；塔死亡留到对局流程清理，不在通知中回收。</summary>
        public DamageResult ApplyTowerDamage(int instanceId, int damage)
        {
            Entity entity;
            if (_towerDamageStopped || _towerDamageBatchInProgress || !TryGet(instanceId, out entity) || !(entity is TowerEntity tower))
                return DamageResult.Ignored;
            return tower.ApplyDamage(damage);
        }

        internal bool ApplyTowerDamageBatch(int playerDamage, int opponentDamage)
        {
            if (_disposed || _towerDamageStopped || _towerDamageBatchInProgress ||
                (playerDamage <= 0 && opponentDamage <= 0) ||
                !TryGetTower(BattleSide.Player, out var player) || !TryGetTower(BattleSide.Opponent, out var opponent)) return false;
            _towerDamageBatchInProgress = true;
            try
            {
                var playerNotification = player.CommitDamage(playerDamage);
                var opponentNotification = opponent.CommitDamage(opponentDamage);
                // 先发送致命伤，非致命生命通知也应观察到已经锁定的终局。
                if (opponentNotification.Result == DamageResult.Killed)
                {
                    opponent.PublishDamage(opponentNotification);
                    player.PublishDamage(playerNotification);
                }
                else
                {
                    player.PublishDamage(playerNotification);
                    opponent.PublishDamage(opponentNotification);
                }
                return playerNotification.Result != DamageResult.Ignored || opponentNotification.Result != DamageResult.Ignored;
            }
            finally { _towerDamageBatchInProgress = false; }
        }

        internal void StopTowerDamage() { _towerDamageStopped = true; }
    }
}
