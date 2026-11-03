using System;

namespace GameLogic
{
    public enum BattleMatchPhase { Matching, Success, Entering, Cancelled, Failed }

    /// <summary>匹配按固定前台时长结束；成功界面承载加载并至少展示一秒。</summary>
    public sealed class BattleMatchSession
    {
        public string EntryId { get; }
        public BattleMatchPhase Phase { get; private set; } = BattleMatchPhase.Matching;
        public float MatchingSeconds { get; private set; }
        public float MatchingDuration { get; }
        public float SuccessSeconds { get; private set; }
        public bool IsReady { get; private set; }
        public BattleMatchSession(string entryId) : this(entryId, 2f) { }
        public BattleMatchSession(string entryId, float matchingDuration)
        {
            if (string.IsNullOrEmpty(entryId)) throw new ArgumentException(nameof(entryId));
            if (float.IsNaN(matchingDuration) || float.IsInfinity(matchingDuration) || matchingDuration < 1f || matchingDuration > 2f)
                throw new ArgumentOutOfRangeException(nameof(matchingDuration));
            EntryId = entryId;
            MatchingDuration = matchingDuration;
        }
        public void MarkReady()
        {
            if (Phase == BattleMatchPhase.Matching || Phase == BattleMatchPhase.Success) IsReady = true;
        }
        public void Advance(float delta, bool foreground)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (!foreground) return;
            if (Phase == BattleMatchPhase.Matching)
            {
                MatchingSeconds = Math.Min(MatchingDuration, MatchingSeconds + delta);
                if (MatchingSeconds >= MatchingDuration) Phase = BattleMatchPhase.Success;
            }
            else if (Phase == BattleMatchPhase.Success)
            {
                SuccessSeconds += delta;
                if (SuccessSeconds >= 1f && IsReady) Phase = BattleMatchPhase.Entering;
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
