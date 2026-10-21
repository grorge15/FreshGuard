using System;
using System.Collections.Generic;

namespace GameLogic
{
    public readonly struct CombatModifierContext
    {
        public long BattleId { get; }
        public float BattleTime { get; }
        public CombatHistory History { get; }
        public CombatModifierPhase Phase { get; }

        internal CombatModifierContext(long battleId, float battleTime, CombatHistory history, CombatModifierPhase phase)
        { BattleId = battleId; BattleTime = battleTime; History = history; Phase = phase; }
    }

    public interface ICombatModifier
    {
        /// <summary>返回独立配置副本，冻结等级与数值。可共享本方计数等状态接收者，但不可共享可变配置或实体引用。</summary>
        ICombatModifier CaptureForAction();
        void Modify(CombatOutcomeDraft outcome, CombatModifierContext context);
        void OnResolved(CombatResolutionResult result, CombatModifierContext context);
        void AdvanceTime(float deltaTime);
    }

    /// <summary>仅明确需要命中时判断的规则实现此标记接口；冻结配置后的 Modify 读取命中时上下文。</summary>
    public interface IDynamicCombatModifier : ICombatModifier { }

    public sealed class CombatModifierPipeline
    {
        internal CombatActionSnapshot Capture(object owner, long sequence, CombatActionSnapshot action,
            ICombatModifier[] modifiers, CombatModifierContext context, Action ensureActive)
        {
            var draft = new CombatOutcomeDraft(action);
            var dynamicModifiers = new List<ICombatModifier>();
            var observers = new List<ICombatModifier>();
            foreach (var modifier in modifiers)
            {
                var frozen = modifier.CaptureForAction();
                ensureActive();
                if (frozen == null || ReferenceEquals(frozen, modifier))
                    throw new InvalidOperationException("修改器必须返回独立的配置快照。");
                observers.Add(frozen);
                if (modifier is IDynamicCombatModifier) dynamicModifiers.Add(frozen);
                else { frozen.Modify(draft, context); ensureActive(); }
            }
            return action.Lock(owner, sequence, draft, dynamicModifiers.ToArray(), observers.ToArray());
        }

        internal void ModifyAtResolution(CombatOutcomeDraft draft, CombatModifierContext context, Action ensureActive)
        {
            foreach (var modifier in draft.Action.DynamicModifiers)
            { modifier.Modify(draft, context); ensureActive(); }
        }
    }

    /// <summary>通用百分比示例；匹配任一指定结果标签。所有机器人伤害用三个伤害标签组合并限定来源。</summary>
    public sealed class PercentCombatModifier : ICombatModifier
    {
        public CombatOutcomeTag Tags { get; }
        public CombatModifierGroup Group { get; }
        public float Percent { get; }
        public CombatSourceKind? SourceKind { get; }

        public PercentCombatModifier(CombatOutcomeTag tags, CombatModifierGroup group, float percent,
            CombatSourceKind? sourceKind = CombatSourceKind.Robot)
        {
            if (tags == CombatOutcomeTag.None) throw new ArgumentOutOfRangeException(nameof(tags));
            if (float.IsNaN(percent) || float.IsInfinity(percent)) throw new ArgumentOutOfRangeException(nameof(percent));
            Tags = tags; Group = group; Percent = percent; SourceKind = sourceKind;
        }

        public void Modify(CombatOutcomeDraft outcome, CombatModifierContext context)
        {
            if ((outcome.Tags & Tags) != 0 && (!SourceKind.HasValue || outcome.Action.SourceKind == SourceKind.Value))
                outcome.AddPercent(Group, Percent);
        }
        public ICombatModifier CaptureForAction() => new PercentCombatModifier(Tags, Group, Percent, SourceKind);
        public void OnResolved(CombatResolutionResult result, CombatModifierContext context) { }
        public void AdvanceTime(float deltaTime) { }
    }
}
