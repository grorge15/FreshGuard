using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>只记录精英/Boss 的机器人命中；每个来源保存最近一次时间，按战斗时间淘汰。</summary>
    public sealed class CombatHistory
    {
        private readonly Dictionary<int, Dictionary<int, float>> _hits = new Dictionary<int, Dictionary<int, float>>();
        public float RetentionSeconds { get; }

        public CombatHistory(float retentionSeconds = 30f)
        {
            EntityValidation.RequireFiniteNonNegative(retentionSeconds, nameof(retentionSeconds));
            if (retentionSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(retentionSeconds));
            RetentionSeconds = retentionSeconds;
        }

        public void RecordHit(int targetId, int robotId, float time, bool isEliteOrBoss)
        {
            EntityValidation.RequirePositive(targetId, nameof(targetId));
            EntityValidation.RequirePositive(robotId, nameof(robotId));
            EntityValidation.RequireFiniteNonNegative(time, nameof(time));
            if (!isEliteOrBoss) return;
            if (!_hits.TryGetValue(targetId, out var robots))
                _hits.Add(targetId, robots = new Dictionary<int, float>());
            if (!robots.TryGetValue(robotId, out var previous) || time > previous) robots[robotId] = time;
        }

        public int CountDistinctRobotHits(int targetId, float now, float window)
        {
            EntityValidation.RequireFiniteNonNegative(now, nameof(now));
            EntityValidation.RequireFiniteNonNegative(window, nameof(window));
            if (window > RetentionSeconds) throw new ArgumentOutOfRangeException(nameof(window));
            var count = 0;
            if (_hits.TryGetValue(targetId, out var robots))
                foreach (var time in robots.Values) if (time <= now && time >= now - window) count++;
            return count;
        }

        public bool HasDistinctRobotHits(int targetId, int required, float now, float window)
        {
            EntityValidation.RequirePositive(required, nameof(required));
            return CountDistinctRobotHits(targetId, now, window) >= required;
        }

        internal void Prune(float now)
        {
            foreach (var targetId in new List<int>(_hits.Keys))
            {
                var robots = _hits[targetId];
                foreach (var robotId in new List<int>(robots.Keys))
                    if (robots[robotId] < now - RetentionSeconds) robots.Remove(robotId);
                if (robots.Count == 0) _hits.Remove(targetId);
            }
        }

        public void Clear() => _hits.Clear();
    }
}
