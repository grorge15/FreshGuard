using System;

namespace GameLogic
{
    public enum BattleMatchPhase { Matching, Success, Entering, Cancelled, Failed }

    /// <summary>仅推进前台时间；加载完成不能跳过最低匹配和成功展示时间。</summary>
    public sealed class BattleMatchSession
    {
        public string EntryId { get; }
        public BattleMatchPhase Phase { get; private set; } = BattleMatchPhase.Matching;
        public float MatchingSeconds { get; private set; }
        public float SuccessSeconds { get; private set; }
        public bool IsReady { get; private set; }
        public BattleMatchSession(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) throw new ArgumentException(nameof(entryId));
            EntryId = entryId;
        }
        public void MarkReady() { if (Phase == BattleMatchPhase.Matching) IsReady = true; }
        public void Advance(float delta, bool foreground)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (!foreground) return;
            if (Phase == BattleMatchPhase.Matching)
            {
                MatchingSeconds += delta;
                if (MatchingSeconds >= 2f && IsReady) Phase = BattleMatchPhase.Success;
            }
            else if (Phase == BattleMatchPhase.Success)
            {
                SuccessSeconds += delta;
                if (SuccessSeconds >= 1f) Phase = BattleMatchPhase.Entering;
            }
        }
        public bool TryCancel()
        {
            if (Phase != BattleMatchPhase.Matching) return false;
            Phase = BattleMatchPhase.Cancelled;
            return true;
        }
        public void Fail() { if (Phase != BattleMatchPhase.Cancelled) Phase = BattleMatchPhase.Failed; }
    }
}
