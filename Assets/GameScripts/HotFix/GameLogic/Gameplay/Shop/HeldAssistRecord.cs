using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>玩家当天持有的助力。战斗快照不保存该对象引用。</summary>
    [Serializable]
    public sealed class HeldAssistRecord
    {
        [SerializeField] private int _buffId;
        [SerializeField] private int _level;
        [SerializeField] private int _slot;
        [SerializeField] private long _expiresUtcTicks;
        public int BuffId => _buffId;
        public int Level => _level;
        public int Slot => _slot;
        public long ExpiresUtcTicks => _expiresUtcTicks;

        public HeldAssistRecord(int buffId, int level, int slot, long expiresUtcTicks)
        {
            if (buffId <= 0 || level < 1 || slot < 0 || slot >= MysteriousStorePreviewSession.SlotCount ||
                expiresUtcTicks <= 0 || expiresUtcTicks > DateTimeOffset.MaxValue.Ticks)
                throw new ArgumentOutOfRangeException(nameof(buffId), "助力身份、等级、槽位或到期时间无效。");
            _buffId = buffId; _level = level; _slot = slot; _expiresUtcTicks = expiresUtcTicks;
        }

        public bool IsExpired(DateTimeOffset now) => _expiresUtcTicks <= now.UtcTicks;
        public HeldAssistRecord Copy() => new HeldAssistRecord(BuffId, Level, Slot, ExpiresUtcTicks);
        public static DateTimeOffset NextBeijingMidnight(DateTimeOffset now)
        {
            var beijing = now.ToOffset(TimeSpan.FromHours(8));
            return new DateTimeOffset(beijing.Date.AddDays(1), TimeSpan.FromHours(8)).ToUniversalTime();
        }
    }
}
