using System;
using System.Collections.Generic;
using TEngine;

namespace GameLogic
{
    public readonly struct BattleRewardEvent
    {
        public long BattleId { get; }
        public BattleSide Recipient { get; }
        public ShopRewardSource Source { get; }
        public int SourceInstanceId { get; }
        public BattleRewardEvent(long battleId, BattleSide recipient, ShopRewardSource source, int sourceInstanceId)
        {
            BattleId = battleId;
            Recipient = recipient;
            Source = source;
            SourceInstanceId = sourceInstanceId;
        }
    }

    /// <summary>接收战斗击杀与奖励事实，使用移除快照中的配置金额并交给经济模块去重。</summary>
    public sealed class BattleRewardAdapter : IDisposable
    {
        public const int REWARD_EVENT = 0x53485257;
        private readonly BattleShopContext _context;
        private bool _disposed;
        private readonly Queue<BattleRewardEvent> _pending = new Queue<BattleRewardEvent>();
        private readonly Queue<EntityRemovalSnapshot> _pendingEnemyKills = new Queue<EntityRemovalSnapshot>();
        public BattleRewardAdapter(BattleShopContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
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
            if (GrantEnemyKill(removal) == ShopOperationResult.Busy) _pendingEnemyKills.Enqueue(removal);
        }
        private ShopOperationResult GrantEnemyKill(EntityRemovalSnapshot removal) =>
            _context.Rewards.TryGrantEnemyKill(_context.BattleId, removal.InstanceId, removal.Side, removal.EnemyKillSP);
        private void OnReward(BattleRewardEvent reward)
        {
            if (_disposed || reward.BattleId != _context.BattleId || _context.IsEnded) return;
            if (Grant(reward) == ShopOperationResult.Busy) _pending.Enqueue(reward);
        }
        private ShopOperationResult Grant(BattleRewardEvent reward) =>
            _context.Rewards.TryGrant(reward.BattleId, reward.Recipient, reward.Source, reward.SourceInstanceId);

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
