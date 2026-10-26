using System;
using System.Collections.Generic;

namespace GameLogic
{
    [Flags]
    public enum CombatOutcomeTag
    {
        None = 0, SingleDamage = 1 << 0, AreaDamage = 1 << 1, DamageOverTime = 1 << 2,
        Slow = 1 << 3, Knockback = 1 << 4, Pull = 1 << 5, Energy = 1 << 6
    }
    // 同组加法、异组乘法；这是计算规则，不再单独维护另一套效果对象。
    public enum CombatModifierGroup { GlobalDamage, AreaDamage, SkillDamage, TargetCondition, Equipment }
    public enum CombatSourceKind { Robot, Enemy, Other }

    public sealed class CombatBuffApplication
    {
        public int BuffId { get; }
        public float Duration { get; }
        public IReadOnlyDictionary<string, float> Parameters { get; }
        public CombatBuffApplication(int buffId, float duration, IReadOnlyDictionary<string, float> parameters = null)
        {
            EntityValidation.RequirePositive(buffId, nameof(buffId));
            EntityValidation.RequireFiniteNonNegative(duration, nameof(duration));
            if (duration <= 0f) throw new ArgumentOutOfRangeException(nameof(duration));
            BuffId = buffId; Duration = duration; Parameters = BuffObj.Copy(parameters);
        }
    }

    /// <summary>一次伤害的同步上下文；效果只能保存其中的标识与数值，不能保存实体引用。</summary>
    public sealed class CombatDamageContext
    {
        public CombatOutcomeDraft Outcome { get; }
        public CombatActionSnapshot Action => Outcome.Action;
        public SkillOwnerRef Target { get; }
        public CombatTargetResolution Result { get; internal set; }
        public EntityRemovalSnapshot Removal { get; internal set; }
        private readonly List<CombatBuffApplication> _hitBuffs;
        internal IReadOnlyList<CombatBuffApplication> HitBuffs => _hitBuffs;
        public CombatDamageContext(CombatOutcomeDraft outcome, SkillOwnerRef target)
        {
            Outcome = outcome; Target = target;
            _hitBuffs = new List<CombatBuffApplication>(outcome.Action.HitBuffs);
        }
        public void AddBuff(CombatBuffApplication application)
            => _hitBuffs.Add(application ?? throw new ArgumentNullException(nameof(application)));
    }

    public sealed class CombatOutcomeDraft
    {
        private readonly Dictionary<CombatModifierGroup, double> _percents = new Dictionary<CombatModifierGroup, double>();
        public CombatActionSnapshot Action { get; }
        public CombatOutcomeTag Tags => Action.OutcomeTags;
        public IReadOnlyList<int> TargetInstanceIds { get; }
        public int HitCount => TargetInstanceIds.Count;
        public int BaseDamage => Action.BaseDamage;
        public bool IsCancelled { get; private set; }
        public void Cancel() => IsCancelled = true;

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
            if (IsCancelled) return 0;
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

}
