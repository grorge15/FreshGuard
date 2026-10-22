using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>敌人身上的临时战斗状态。减速按来源保存，当前速度取有效条目中的最强值。</summary>
    public sealed class EnemyCombatStatusState
    {
        private readonly Dictionary<int, SlowEntry> _slowEntries = new Dictionary<int, SlowEntry>();

        public int SlowEntryCount => _slowEntries.Count;

        public bool TryApplySlow(int sourceInstanceId, float configuredRatio, float duration, float controlResist)
        {
            EntityValidation.RequirePositive(sourceInstanceId, nameof(sourceInstanceId));
            RequireRatio(configuredRatio, nameof(configuredRatio));
            EntityValidation.RequireFiniteNonNegative(duration, nameof(duration));
            RequireRatio(controlResist, nameof(controlResist));
            if (duration <= 0f) return false;
            _slowEntries[sourceInstanceId] = new SlowEntry(configuredRatio * controlResist, duration);
            return true;
        }

        public float GetEffectiveSlowRatio()
        {
            var strongest = 0f;
            foreach (var entry in _slowEntries.Values)
                if (entry.Remaining > 0f && entry.Ratio > strongest) strongest = entry.Ratio;
            return Math.Min(1f, Math.Max(0f, strongest));
        }

        public float GetEffectiveSpeed(float baseSpeed)
        {
            EntityValidation.RequireFiniteNonNegative(baseSpeed, nameof(baseSpeed));
            return baseSpeed * Math.Max(0.5f, 1f - GetEffectiveSlowRatio());
        }

        public void AdvanceTime(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (deltaTime <= 0f) return;
            var expired = new List<int>();
            // 先复制键，再更新字典，避免状态到期与字典遍历耦合。
            var sourceIds = new List<int>(_slowEntries.Keys);
            foreach (var sourceId in sourceIds)
            {
                SlowEntry entry;
                if (!_slowEntries.TryGetValue(sourceId, out entry)) continue;
                entry.Remaining = Math.Max(0f, entry.Remaining - deltaTime);
                if (entry.Remaining <= 0f) expired.Add(sourceId);
                else _slowEntries[sourceId] = entry;
            }
            foreach (var sourceId in expired) _slowEntries.Remove(sourceId);
        }

        public void Clear() { _slowEntries.Clear(); }

        private static void RequireRatio(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(name, "比例必须在0到1之间。");
        }

        private struct SlowEntry
        {
            internal float Ratio;
            internal float Remaining;
            internal SlowEntry(float ratio, float remaining) { Ratio = ratio; Remaining = remaining; }
        }
    }
}
