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
        public BuffModel Model { get; }
        public SkillOwnerRef Source { get; }
        public int Stacks { get; internal set; } = 1;
        public float Remaining { get; internal set; }
        public IReadOnlyDictionary<string, float> Parameters { get; internal set; }
        internal double TickElapsed;
        internal long Order;
        internal IBuffBehavior Behavior;
        internal BuffObj(BuffModel model, SkillOwnerRef source, float duration,
            IReadOnlyDictionary<string, float> parameters, IBuffBehavior behavior)
        {
            Model = model; Source = source; Remaining = duration; Behavior = behavior;
            Parameters = Copy(parameters);
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
        void OnOccur(BuffContainer container, BuffObj buff);
        void OnRemoved(BuffContainer container, BuffObj buff);
        void OnTick(BuffContainer container, BuffObj buff);
        void OnCast(BuffContainer container, BuffObj buff, SkillCastContext context);
    }
    public class BuffBehavior : IBuffBehavior
    {
        public virtual void OnOccur(BuffContainer container, BuffObj buff) { }
        public virtual void OnRemoved(BuffContainer container, BuffObj buff) { }
        public virtual void OnTick(BuffContainer container, BuffObj buff) { }
        public virtual void OnCast(BuffContainer container, BuffObj buff, SkillCastContext context) { }
    }
}
