using System;
using System.Collections.Generic;

namespace GameLogic
{
    public sealed class CombatOutcomeDraft
    {
        private readonly Dictionary<CombatModifierGroup, double> _percents = new Dictionary<CombatModifierGroup, double>();
        public CombatActionSnapshot Action { get; }
        public CombatOutcomeTag Tags => Action.OutcomeTags;
        public IReadOnlyList<int> TargetInstanceIds { get; }
        public int HitCount => TargetInstanceIds.Count;
        public int BaseDamage => Action.BaseDamage;

        public CombatOutcomeDraft(CombatActionSnapshot action, IReadOnlyList<int> targets = null)
        {
            Action = action ?? throw new ArgumentNullException(nameof(action));
            TargetInstanceIds = targets == null ? action.TargetInstanceIds : new List<int>(targets).AsReadOnly();
            if (action.Percents != null)
                foreach (var pair in action.Percents) _percents.Add(pair.Key, pair.Value);
        }

        public void AddPercent(CombatModifierGroup group, float percent)
        {
            if (float.IsNaN(percent) || float.IsInfinity(percent)) throw new ArgumentOutOfRangeException(nameof(percent));
            _percents.TryGetValue(group, out var current);
            _percents[group] = current + percent;
        }

        public int GetFinalDamage()
        {
            double value = BaseDamage;
            // 固定分组顺序，避免注册顺序引起浮点计算差异。
            var groups = new List<CombatModifierGroup>(_percents.Keys);
            groups.Sort();
            foreach (var group in groups) value *= Math.Max(0d, 1d + _percents[group]);
            if (value <= 0d) return 0;
            if (value >= int.MaxValue) return int.MaxValue;
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        internal IReadOnlyDictionary<CombatModifierGroup, double> CopyPercents() =>
            new Dictionary<CombatModifierGroup, double>(_percents);
    }

    public readonly struct CombatTargetResolution
    {
        public int TargetInstanceId { get; }
        public int RequestedDamage { get; }
        public int AppliedDamage { get; }
        public DamageResult DamageResult { get; }
        public bool IsKilled => DamageResult == DamageResult.Killed;
        public bool IsHit => DamageResult != DamageResult.Ignored;
        public bool IsEliteOrBoss { get; }

        public CombatTargetResolution(int targetInstanceId, int requestedDamage, DamageResult damageResult,
            int appliedDamage = 0, bool isEliteOrBoss = false)
        {
            TargetInstanceId = targetInstanceId; RequestedDamage = requestedDamage; DamageResult = damageResult;
            AppliedDamage = appliedDamage; IsEliteOrBoss = isEliteOrBoss;
        }
    }

    public sealed class CombatResolutionResult
    {
        public CombatActionSnapshot Action { get; }
        public int FinalDamage { get; }
        public CombatOutcomeTag ActualTags { get; }
        public IReadOnlyList<CombatTargetResolution> Targets { get; }
        public int HitCount { get; }

        public CombatResolutionResult(CombatOutcomeDraft outcome, IReadOnlyList<CombatTargetResolution> targets)
        {
            if (outcome == null || targets == null) throw new ArgumentNullException(nameof(outcome));
            Action = outcome.Action; FinalDamage = outcome.GetFinalDamage();
            Targets = new List<CombatTargetResolution>(targets).AsReadOnly();
            foreach (var target in Targets) if (target.IsHit) HitCount++;
            ActualTags = HitCount > 0 ? outcome.Tags : CombatOutcomeTag.None;
        }
    }

    public interface ICombatResolutionPort
    {
        CombatResolutionResult Resolve(CombatOutcomeDraft outcome);
    }

    /// <summary>可选的命中前目标查询，让密集火力等动态条件读取仍有效的目标数量。</summary>
    public interface ICombatTargetQuery
    {
        IReadOnlyList<int> GetValidTargets(CombatActionSnapshot action);
    }
}
