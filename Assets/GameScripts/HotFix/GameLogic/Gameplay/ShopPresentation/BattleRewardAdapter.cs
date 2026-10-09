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

    /// <summary>后续战斗结算方发送已确认的事实，单局适配器只负责接收和交给经济模块去重。</summary>
    public sealed class BattleRewardAdapter : IDisposable
    {
        public const int REWARD_EVENT = 0x53485257;
        private readonly BattleShopContext _context;
        private bool _disposed;
        private readonly Queue<BattleRewardEvent> _pending = new Queue<BattleRewardEvent>();
        public BattleRewardAdapter(BattleShopContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            GameEvent.AddEventListener<BattleRewardEvent>(REWARD_EVENT, OnReward);
        }
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
            if (_disposed || _context.IsEnded) { _pending.Clear(); return; }
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
            GameEvent.RemoveEventListener<BattleRewardEvent>(REWARD_EVENT, OnReward);
        }
    }
}
