using System;

namespace GameLogic
{
    public enum BattleSettlementResult { Victory, Defeat }
    public enum BattleSettlementReason { TowerDestroyed, FinalWaveCleared }

    public readonly struct BattleSettlementSnapshot
    {
        public long BattleId { get; }
        public int? TowerInstanceId { get; }
        public BattleSide? DefeatedSide { get; }
        public int? TowerHp { get; }
        public BattleSettlementResult Result { get; }
        public BattleSettlementReason Reason { get; }
        public int PlayerTowerHp { get; }
        public int OpponentTowerHp { get; }

        internal BattleSettlementSnapshot(long battleId, TowerHealthSnapshot death, int playerTowerHp, int opponentTowerHp)
        {
            BattleId = battleId;
            TowerInstanceId = death.InstanceId;
            DefeatedSide = death.Side;
            TowerHp = death.CurrentHp;
            PlayerTowerHp = playerTowerHp;
            OpponentTowerHp = opponentTowerHp;
            Result = opponentTowerHp == 0 ? BattleSettlementResult.Victory : BattleSettlementResult.Defeat;
            Reason = BattleSettlementReason.TowerDestroyed;
        }

        internal BattleSettlementSnapshot(long battleId, int playerTowerHp, int opponentTowerHp)
        {
            BattleId = battleId;
            TowerInstanceId = null;
            DefeatedSide = null;
            TowerHp = null;
            PlayerTowerHp = playerTowerHp;
            OpponentTowerHp = opponentTowerHp;
            Result = BattleSettlementResult.Victory;
            Reason = BattleSettlementReason.FinalWaveCleared;
        }
    }

    public static class BattleSettlementEvents
    {
        public const int Requested = 0x4253544C;
    }

    /// <summary>独立的双方塔生命周期。只请求结算，不操作商店、场景或结算 UI。</summary>
    public sealed class TowerBattleSession : IDisposable
    {
        private readonly long _battleId;
        private readonly BattleEntityRegistry _registry;
        private bool _disposed;
        private bool _applyingDamage;
        public TowerEntity PlayerTower { get; }
        public TowerEntity OpponentTower { get; }
        public bool IsEnded { get; private set; }
        public event Action<BattleSettlementSnapshot> SettlementRequested;
        public TowerBattleSession(long battleId, BattleEntityRegistry registry, int maxHp, int configId = 3001)
        {
            if (battleId <= 0) throw new ArgumentOutOfRangeException(nameof(battleId));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            EntityValidation.RequirePositive(maxHp, nameof(maxHp));
            EntityValidation.RequirePositive(configId, nameof(configId));
            TowerEntity existing;
            if (registry.TryGetTower(BattleSide.Player, out existing) || registry.TryGetTower(BattleSide.Opponent, out existing))
                throw new InvalidOperationException("本局已存在塔，不能重复装配。");
            _battleId = battleId;
            _registry = registry;
            PlayerTower = registry.CreateTower(configId, BattleSide.Player, maxHp);
            try { OpponentTower = registry.CreateTower(configId, BattleSide.Opponent, maxHp); }
            catch
            {
                EntityRemovalSnapshot ignored;
                registry.Remove(PlayerTower.InstanceId, EntityRemovalReason.Manual, out ignored);
                throw;
            }
            PlayerTower.Died += OnTowerDied;
            OpponentTower.Died += OnTowerDied;
        }

        public DamageResult ApplyDamage(long battleId, BattleSide side, int damage)
        {
            if (_disposed || IsEnded || _applyingDamage || battleId != _battleId ||
                (side != BattleSide.Player && side != BattleSide.Opponent)) return DamageResult.Ignored;
            _applyingDamage = true;
            try { return _registry.ApplyTowerDamage(side == BattleSide.Player ? PlayerTower.InstanceId : OpponentTower.InstanceId, damage); }
            finally { _applyingDamage = false; }
        }

        /// <summary>同一逻辑抵塔时刻的双方伤害。后续时刻不能合并进本批次。</summary>
        public bool ApplyDamageBatch(long battleId, int playerDamage, int opponentDamage)
        {
            if (_disposed || IsEnded || _applyingDamage || battleId != _battleId) return false;
            _applyingDamage = true;
            try { return _registry.ApplyTowerDamageBatch(playerDamage, opponentDamage); }
            finally { _applyingDamage = false; }
        }

        /// <summary>由战斗入口确认本局全部出兵和清空后调用；不在伤害通知栈中抢先判胜。</summary>
        public bool TryCompleteWaveClear(long battleId)
        {
            if (_disposed || IsEnded || _applyingDamage || battleId != _battleId ||
                !PlayerTower.IsAlive || !OpponentTower.IsAlive) return false;
            IsEnded = true;
            _registry.StopTowerDamage();
            TowerNotifications.Publish(SettlementRequested,
                new BattleSettlementSnapshot(_battleId, PlayerTower.CurrentHp, OpponentTower.CurrentHp));
            return true;
        }

        private void OnTowerDied(TowerHealthSnapshot death)
        {
            if (_disposed || IsEnded || death.CurrentHp != 0 ||
                (death.InstanceId != PlayerTower.InstanceId && death.InstanceId != OpponentTower.InstanceId)) return;
            IsEnded = true;
            _registry.StopTowerDamage();
            // 双方同时死亡时兼容字段也指向敌方，结果明确为玩家胜利。
            if (PlayerTower.CurrentHp == 0 && OpponentTower.CurrentHp == 0)
                death = new TowerHealthSnapshot(OpponentTower, OpponentTower.CurrentHp);
            TowerNotifications.Publish(SettlementRequested,
                new BattleSettlementSnapshot(_battleId, death, PlayerTower.CurrentHp, OpponentTower.CurrentHp));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            IsEnded = true;
            _registry.StopTowerDamage();
            PlayerTower.Died -= OnTowerDied;
            OpponentTower.Died -= OnTowerDied;
            SettlementRequested = null;
            EntityRemovalSnapshot ignored;
            _registry.Remove(PlayerTower.InstanceId, EntityRemovalReason.BattleEnded, out ignored);
            _registry.Remove(OpponentTower.InstanceId, EntityRemovalReason.BattleEnded, out ignored);
        }
    }
}
