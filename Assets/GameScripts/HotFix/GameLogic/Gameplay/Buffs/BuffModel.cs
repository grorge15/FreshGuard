using System;
using System.Collections.Generic;

namespace GameLogic
{
    public sealed class BuffModel
    {
        public int Id { get; }
        public string BehaviorKey { get; }
        public int MaxStacks { get; }
        public int Priority { get; }
        public float TickInterval { get; }
        public BuffModel(int id, string behaviorKey, int maxStacks = 1, int priority = 0, float tickInterval = 0f)
        {
            EntityValidation.RequirePositive(id, nameof(id));
            EntityValidation.RequirePositive(maxStacks, nameof(maxStacks));
            EntityValidation.RequireFiniteNonNegative(tickInterval, nameof(tickInterval));
            if (string.IsNullOrWhiteSpace(behaviorKey)) throw new ArgumentException(nameof(behaviorKey));
            Id = id; BehaviorKey = behaviorKey; MaxStacks = maxStacks; Priority = priority; TickInterval = tickInterval;
        }
    }
    public sealed class BuffObj
    {
        internal const double TimeTolerance = 0.0000001d;
        public long InstanceId { get; internal set; }
        public BuffModel Model { get; }
        public SkillOwnerRef Source { get; }
        public int Stacks { get; internal set; } = 1;
        public float Remaining { get => (float)TimeRemaining; internal set => TimeRemaining = value; }
        public bool IsPermanent => double.IsPositiveInfinity(TimeRemaining);
        internal double TimeRemaining;
        public double TickInterval { get; internal set; }
        public double NextTickRemaining { get; internal set; }
        public double RemainingTickCount => TickInterval <= 0d || NextTickRemaining > TimeRemaining + TimeTolerance
            ? 0d : Math.Max(0d, 1d + Math.Floor((TimeRemaining - NextTickRemaining + TimeTolerance) / TickInterval));
        public IReadOnlyDictionary<string, float> Parameters { get; internal set; }
        internal long Order;
        internal IBuffBehavior Behavior;
        internal BuffObj(BuffModel model, SkillOwnerRef source, float duration,
            IReadOnlyDictionary<string, float> parameters, IBuffBehavior behavior, int stacks = 1)
        {
            Model = model; Source = source; Remaining = duration; Behavior = behavior; Stacks = stacks;
            Parameters = Copy(parameters);
            TickInterval = model.TickInterval;
            Behavior.OnCreate(this);
            if (double.IsNaN(TickInterval) || double.IsInfinity(TickInterval) || TickInterval < 0d)
                throw new ArgumentOutOfRangeException(nameof(TickInterval));
            NextTickRemaining = TickInterval;
        }
        internal static IReadOnlyDictionary<string, float> Copy(IReadOnlyDictionary<string, float> values)
        {
            var copy = new Dictionary<string, float>();
            if (values != null) foreach (var pair in values)
            {
                if (float.IsNaN(pair.Value) || float.IsInfinity(pair.Value)) throw new ArgumentOutOfRangeException(nameof(values));
                copy.Add(pair.Key, pair.Value);
            }
            return new System.Collections.ObjectModel.ReadOnlyDictionary<string, float>(copy);
        }
        public float Get(string key, float fallback = 0f) => Parameters.TryGetValue(key, out var value) ? value : fallback;
    }
    public interface IBuffBehavior
    {
        // 候选实例创建时只初始化/校验数据；应用被拒绝时不会触发OnOccur。
        void OnCreate(BuffObj buff);
        BuffApplyDecision SelectApplication(BuffContainer container, BuffObj incoming);
        void OnOccur(BuffContainer container, BuffObj buff);
        void OnRemoved(BuffContainer container, BuffObj buff);
        void OnTick(BuffContainer container, BuffObj buff);
        void OnCast(BuffContainer container, BuffObj buff, SkillCastContext context);
        void OnAttack(BuffContainer container, BuffObj buff, CombatOutcomeDraft attack);
        void OnHit(BuffContainer container, BuffObj buff, CombatDamageContext context);
        void OnBeHurt(BuffContainer container, BuffObj buff, CombatDamageContext context);
        void OnKill(BuffContainer container, BuffObj buff, CombatDamageContext context);
        void OnBeKilled(BuffContainer container, BuffObj buff, CombatDamageContext context);
    }
    public class BuffBehavior : IBuffBehavior
    {
        public virtual void OnCreate(BuffObj buff) { }
        public virtual BuffApplyDecision SelectApplication(BuffContainer container, BuffObj incoming)
        {
            var existing = container.Find(incoming.Model.Id, incoming.Source);
            return existing == null ? BuffApplyDecision.Add : BuffApplyDecision.Refresh(existing);
        }
        public virtual void OnOccur(BuffContainer container, BuffObj buff) { }
        public virtual void OnRemoved(BuffContainer container, BuffObj buff) { }
        public virtual void OnTick(BuffContainer container, BuffObj buff) { }
        public virtual void OnCast(BuffContainer container, BuffObj buff, SkillCastContext context) { }
        public virtual void OnAttack(BuffContainer container, BuffObj buff, CombatOutcomeDraft attack) { }
        public virtual void OnHit(BuffContainer container, BuffObj buff, CombatDamageContext context) { }
        public virtual void OnBeHurt(BuffContainer container, BuffObj buff, CombatDamageContext context) { }
        public virtual void OnKill(BuffContainer container, BuffObj buff, CombatDamageContext context) { }
        public virtual void OnBeKilled(BuffContainer container, BuffObj buff, CombatDamageContext context) { }
    }
}
