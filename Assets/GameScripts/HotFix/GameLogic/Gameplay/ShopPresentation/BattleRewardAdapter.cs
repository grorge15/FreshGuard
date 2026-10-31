using System;
using System.Collections.Generic;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    public readonly struct BattleCurrencyFlightEvent
    {
        public long BattleId { get; }
        public BattleSide Recipient { get; }
        public Vector3 WorldOrigin { get; }
        public int IconCount { get; }
        public BattleCurrencyFlightEvent(long battleId, BattleSide recipient, Vector3 worldOrigin, int iconCount = 4)
        {
            BattleId = battleId;
            Recipient = recipient;
            WorldOrigin = worldOrigin;
            IconCount = iconCount;
        }
    }

    public readonly struct BattleRewardEvent
    {
        public long BattleId { get; }
        public BattleSide Recipient { get; }
        public ShopRewardSource Source { get; }
        public int SourceInstanceId { get; }
        public bool HasWorldOrigin { get; }
        public Vector3 WorldOrigin { get; }
        public BattleRewardEvent(long battleId, BattleSide recipient, ShopRewardSource source, int sourceInstanceId)
        {
            BattleId = battleId;
            Recipient = recipient;
            Source = source;
            SourceInstanceId = sourceInstanceId;
            HasWorldOrigin = false;
            WorldOrigin = default;
        }
        public BattleRewardEvent(long battleId, BattleSide recipient, ShopRewardSource source,
            int sourceInstanceId, Vector3 worldOrigin) : this(battleId, recipient, source, sourceInstanceId)
        {
            HasWorldOrigin = true;
            WorldOrigin = worldOrigin;
        }
    }

    /// <summary>接收战斗击杀与奖励事实，使用移除快照中的配置金额并交给经济模块去重。</summary>
    public sealed class BattleRewardAdapter : IDisposable
    {
        public const int REWARD_EVENT = 0x53485257;
        public const int CURRENCY_FLIGHT_EVENT = 0x47434658;
        private readonly BattleShopContext _context;
        private readonly Func<BattleSide, int, Vector3?> _enemyRewardOrigin;
        private bool _disposed;
        private readonly Queue<BattleRewardEvent> _pending = new Queue<BattleRewardEvent>();
        private readonly Queue<EnemyKillReward> _pendingEnemyKills = new Queue<EnemyKillReward>();
        private readonly struct EnemyKillReward
        {
            public EntityRemovalSnapshot Removal { get; }
            public Vector3? WorldOrigin { get; }
            public EnemyKillReward(EntityRemovalSnapshot removal, Vector3? worldOrigin)
            { Removal = removal; WorldOrigin = worldOrigin; }
        }

        public BattleRewardAdapter(BattleShopContext context, Func<BattleSide, int, Vector3?> enemyRewardOrigin = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _enemyRewardOrigin = enemyRewardOrigin;
            GameEvent.AddEventListener<BattleRewardEvent>(REWARD_EVENT, OnReward);
            _context.Battle.Combat.TargetResolved += OnTargetResolved;
        }
        private void OnTargetResolved(CombatDamageContext damage)
        {
            if (_disposed || _context.IsEnded || _context.IsEndRequested ||
                damage.Action.BattleId != _context.BattleId || !damage.Result.IsKilled) return;
            var removal = damage.Removal;
            if (removal.Kind != EntityKind.Enemy || removal.RemovalReason != EntityRemovalReason.Killed ||
                removal.InstanceId != damage.Result.TargetInstanceId || removal.EnemyKillSP <= 0) return;
            // 实体此时已归池，只读取移除快照；奖励归属为敌人所在战场。
            var reward = new EnemyKillReward(removal, _enemyRewardOrigin?.Invoke(removal.Side, removal.InstanceId));
            if (GrantEnemyKill(reward) == ShopOperationResult.Busy) _pendingEnemyKills.Enqueue(reward);
        }
        private ShopOperationResult GrantEnemyKill(EnemyKillReward reward)
        {
            var removal = reward.Removal;
            var result = _context.Rewards.TryGrantEnemyKill(_context.BattleId, removal.InstanceId, removal.Side, removal.EnemyKillSP);
            // 位置与击杀快照一起保留，事务重试时不再查询已经回收的敌人。
            if (result == ShopOperationResult.Success && reward.WorldOrigin.HasValue)
                Present(removal.Side, reward.WorldOrigin.Value, removal.EnemyKillSP);
            return result;
        }
        private void OnReward(BattleRewardEvent reward)
        {
            if (_disposed || reward.BattleId != _context.BattleId || _context.IsEnded) return;
            if (Grant(reward) == ShopOperationResult.Busy) _pending.Enqueue(reward);
        }
        private ShopOperationResult Grant(BattleRewardEvent reward)
        {
            var result = _context.Rewards.TryGrant(reward.BattleId, reward.Recipient, reward.Source, reward.SourceInstanceId);
            // 成功入账后才播放；重复、失败和等待重试的事实不会提前产生飞币。
            if (result == ShopOperationResult.Success && reward.HasWorldOrigin &&
                (reward.Source == ShopRewardSource.NormalGlass || reward.Source == ShopRewardSource.ColoredGlass) &&
                !_context.IsEnded && !_context.IsEndRequested)
                Present(reward.Recipient, reward.WorldOrigin);
            return result;
        }

        private void Present(BattleSide recipient, Vector3 origin, int iconCount = 4)
        {
            if (!_disposed && !_context.IsEnded && !_context.IsEndRequested)
                GameEvent.Send(CURRENCY_FLIGHT_EVENT, new BattleCurrencyFlightEvent(_context.BattleId, recipient, origin, iconCount));
        }

        /// <summary>事务通知内到达的奖励在下一帧重试，去重仍由经济上下文负责。</summary>
        public void Advance()
        {
            if (_disposed || _context.IsEnded || _context.IsEndRequested)
            { _pending.Clear(); _pendingEnemyKills.Clear(); return; }
            var kills = _pendingEnemyKills.Count;
            while (kills-- > 0 && _pendingEnemyKills.Count > 0)
            {
                var removal = _pendingEnemyKills.Dequeue();
                if (GrantEnemyKill(removal) == ShopOperationResult.Busy)
                {
                    _pendingEnemyKills.Enqueue(removal);
                    break;
                }
            }
            var count = _pending.Count;
            while (count-- > 0 && _pending.Count > 0)
            {
                var reward = _pending.Dequeue();
                if (Grant(reward) == ShopOperationResult.Busy)
                {
                    _pending.Enqueue(reward);
                    break;
                }
            }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pending.Clear();
            _pendingEnemyKills.Clear();
            GameEvent.RemoveEventListener<BattleRewardEvent>(REWARD_EVENT, OnReward);
            _context.Battle.Combat.TargetResolved -= OnTargetResolved;
        }
    }
}
