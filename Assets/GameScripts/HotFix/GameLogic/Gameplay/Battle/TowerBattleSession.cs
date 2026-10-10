using System;

namespace GameLogic
{
    public readonly struct BattleSettlementSnapshot
    {
        public long BattleId { get; }
        public int TowerInstanceId { get; }
        public BattleSide DefeatedSide { get; }
        public int TowerHp { get; }

        internal BattleSettlementSnapshot(long battleId, TowerHealthSnapshot death)
        {
            BattleId = battleId;
            TowerInstanceId = death.InstanceId;
            DefeatedSide = death.Side;
            TowerHp = death.CurrentHp;
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
            if (_disposed || IsEnded || battleId != _battleId ||
                (side != BattleSide.Player && side != BattleSide.Opponent)) return DamageResult.Ignored;
            return _registry.ApplyTowerDamage(side == BattleSide.Player ? PlayerTower.InstanceId : OpponentTower.InstanceId, damage);
        }

        private void OnTowerDied(TowerHealthSnapshot death)
        {
            if (_disposed || IsEnded || death.CurrentHp != 0 ||
                (death.InstanceId != PlayerTower.InstanceId && death.InstanceId != OpponentTower.InstanceId)) return;
            IsEnded = true;
            _registry.StopTowerDamage();
            TowerNotifications.Publish(SettlementRequested, new BattleSettlementSnapshot(_battleId, death));
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
