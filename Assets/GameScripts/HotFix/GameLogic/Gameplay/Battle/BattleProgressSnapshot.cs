using TEngine;

namespace GameLogic
{
    /// <summary>按对局标识隔离的展示数据；时间唯一来自战斗时间轴。</summary>
    public readonly struct BattleProgressSnapshot
    {
        public long BattleId { get; }
        public int CurrentWave { get; }
        public double ElapsedSeconds { get; }

        public BattleProgressSnapshot(long battleId, int currentWave, double elapsedSeconds)
        {
            BattleId = battleId;
            CurrentWave = currentWave;
            ElapsedSeconds = elapsedSeconds;
        }
    }

    public static class BattleProgressEvents
    {
        public static readonly int Changed = RuntimeId.ToRuntimeId("BattleProgressEvents.Changed");
    }
}
