using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLogic
{
    /// <summary>回调观察稳定快照；本轮内增删统一延后提交。</summary>
    public sealed class BuffContainer : IDisposable
    {
        private readonly List<BuffObj> _items = new List<BuffObj>();
        private readonly Queue<Action> _pending = new Queue<Action>();
        private int _dispatchDepth;
        private bool _flushing;
        private bool _advancing;
        private long _order;
        public SkillOwnerRef Owner { get; internal set; }
        internal bool SuppressRemovedCallbacks;
        public bool IsDisposed { get; private set; }
        public int Count => _items.Count;
        internal double NextBoundary
        {
            get
            {
                var delay = double.PositiveInfinity;
                foreach (var buff in _items)
                {
                    delay = Math.Min(delay, Math.Max(0d, buff.TimeRemaining));
                    if (buff.TickInterval > 0d)
                        delay = Math.Min(delay, Math.Max(0d, buff.NextTickRemaining));
                }
                return delay;
            }
        }
        public IReadOnlyList<BuffObj> Items => _items.ToArray();
        public BuffObj Find(int id, SkillOwnerRef source) => _items.Find(b => b.Model.Id == id && b.Source.Equals(source));
        public void Add(BuffModel model, SkillOwnerRef source, float duration,
            IReadOnlyDictionary<string, float> parameters = null, IBuffBehavior behavior = null)
        {
            if (IsDisposed) return;
            if (model == null) throw new ArgumentNullException(nameof(model));
            EntityValidation.RequireFiniteNonNegative(duration, nameof(duration));
            if (duration <= 0f) throw new ArgumentOutOfRangeException(nameof(duration));
            var copy = BuffObj.Copy(parameters);
            if (model.BehaviorKey == "poison")
            {
                ApplyPoison(model, source, duration, copy, behavior);
                return;
            }
            Mutate(() =>
            {
                if (IsDisposed) return;
                var existing = Find(model.Id, source);
                if (existing != null)
                {
                    existing.Stacks = Math.Min(model.MaxStacks, existing.Stacks + 1);
                    existing.Remaining = duration; existing.Parameters = copy;
                    existing.NextTickRemaining = existing.TickInterval;
                    Dispatch(() => existing.Behavior.OnOccur(this, existing));
                    return;
                }
                var buff = Create(model, source, duration, copy, behavior);
                _items.Add(buff);
                Dispatch(() => buff.Behavior.OnOccur(this, buff));
            });
        }

        private BuffObj Create(BuffModel model, SkillOwnerRef source, float duration,
            IReadOnlyDictionary<string, float> parameters, IBuffBehavior behavior)
        {
            var order = checked(++_order);
            return new BuffObj(model, source, duration, parameters, behavior ?? new BuffBehavior())
                { Order = order, InstanceId = order };
        }

        private void ApplyPoison(BuffModel model, SkillOwnerRef source, float duration,
            IReadOnlyDictionary<string, float> parameters, IBuffBehavior behavior)
        {
            var candidate = new BuffObj(model, source, duration, parameters, behavior ?? new BuffBehavior());
            var cap = candidate.Get("poisonMaxLayers");
            var damage = candidate.Get("poisonDamage");
            if (candidate.TickInterval <= 0d || cap < 1f || cap >= int.MaxValue || cap != Math.Truncate(cap) ||
                damage < 0f || damage >= int.MaxValue || damage != Math.Truncate(damage))
                throw new ArgumentException("中毒间隔、层数或伤害无效。", nameof(parameters));
            Mutate(() => Dispatch(() =>
            {
                if (IsDisposed) return;
                var layers = _items.Where(b => b.Model.Id == model.Id).ToArray();
                if (layers.Length >= (int)cap)
                {
                    var weakest = layers.OrderBy(b => b.RemainingPoisonDamage).ThenBy(b => b.Order).First();
                    if (candidate.RemainingPoisonDamage <= weakest.RemainingPoisonDamage) return;
                    _items.Remove(weakest);
                    // 替换的移除与新层插入位于同一提交内，回调中的增删继续排队。
                    Dispatch(() => weakest.Behavior.OnRemoved(this, weakest));
                    if (IsDisposed) return;
                }
                candidate.Order = checked(++_order); candidate.InstanceId = candidate.Order;
                _items.Add(candidate);
                Dispatch(() => candidate.Behavior.OnOccur(this, candidate));
            }));
        }
        public void Remove(BuffObj buff)
        {
            if (buff == null) return;
            Mutate(() => { if (_items.Remove(buff) && !SuppressRemovedCallbacks) Dispatch(() => buff.Behavior.OnRemoved(this, buff)); });
        }
        public void Clear()
        {
            Mutate(() =>
            {
                var items = _items.ToArray(); _items.Clear();
                Dispatch(() =>
                {
                    Exception failure = null;
                    foreach (var buff in items)
                    {
                        if (SuppressRemovedCallbacks) break;
                        try { buff.Behavior.OnRemoved(this, buff); }
                        catch (Exception exception) { if (failure == null) failure = exception; }
                    }
                    if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                });
            });
        }
        public void Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (IsDisposed || _advancing) return;
            _advancing = true;
            try
            {
                var remaining = (double)deltaTime;
                var first = true;
                while (!IsDisposed && _items.Count > 0 && (first || remaining > 0d))
                {
                    first = false;
                    var step = Math.Min(remaining, NextBoundary);
                    var items = _items.ToArray();
                    foreach (var buff in items)
                    {
                        buff.TimeRemaining = Math.Max(0d, buff.TimeRemaining - step);
                        if (buff.TickInterval > 0d) buff.NextTickRemaining -= step;
                    }
                    // 每个事件时刻提交一次回调变更；清理或刷新立即影响后续事件。
                    foreach (var buff in items)
                    {
                        if (IsDisposed) break;
                        if (!_items.Contains(buff) || buff.TickInterval <= 0d ||
                            buff.NextTickRemaining > BuffObj.TimeTolerance) continue;
                        buff.NextTickRemaining += buff.TickInterval;
                        Dispatch(() => buff.Behavior.OnTick(this, buff));
                    }
                    foreach (var buff in items)
                        if (!IsDisposed && buff.TimeRemaining <= BuffObj.TimeTolerance) Remove(buff);
                    remaining = Math.Max(0d, remaining - step);
                }
            }
            finally { _advancing = false; }
        }
        public void OnCast(SkillCastContext context)
        {
            if (IsDisposed) return;
            Dispatch(() =>
            {
                foreach (var buff in _items.OrderByDescending(b => b.Model.Priority).ThenBy(b => b.Order).ToArray())
                {
                    if (context.IsCancelled || IsDisposed) break;
                    buff.Behavior.OnCast(this, buff, context);
                }
            });
        }
        internal void OnAttack(CombatOutcomeDraft attack, Func<bool> active)
            => DispatchCombat((behavior, buff) => behavior.OnAttack(this, buff, attack), active);
        internal void OnHit(CombatDamageContext context, Func<bool> active)
            => DispatchCombat((behavior, buff) => behavior.OnHit(this, buff, context), active);
        internal void OnBeHurt(CombatDamageContext context, Func<bool> active)
            => DispatchCombat((behavior, buff) => behavior.OnBeHurt(this, buff, context), active);
        internal void OnKill(CombatDamageContext context, Func<bool> active)
            => DispatchCombat((behavior, buff) => behavior.OnKill(this, buff, context), active);
        internal void OnBeKilled(CombatDamageContext context, Func<bool> active)
            => DispatchCombat((behavior, buff) => behavior.OnBeKilled(this, buff, context), active);

        private void DispatchCombat(Action<IBuffBehavior, BuffObj> callback, Func<bool> active)
        {
            if (IsDisposed) return;
            Dispatch(() =>
            {
                foreach (var buff in _items.OrderByDescending(b => b.Model.Priority).ThenBy(b => b.Order).ToArray())
                {
                    if (IsDisposed || !active()) break;
                    callback(buff.Behavior, buff);
                }
            });
        }

        private void Mutate(Action action)
        {
            if (_dispatchDepth > 0 || _flushing) { _pending.Enqueue(action); return; }
            action(); Flush();
        }
        private void Dispatch(Action action)
        {
            _dispatchDepth++;
            try { action(); }
            finally { _dispatchDepth--; if (_dispatchDepth == 0) Flush(); }
        }
        private void Flush()
        {
            if (_flushing || _dispatchDepth > 0) return;
            _flushing = true;
            try { while (_pending.Count > 0) _pending.Dequeue()(); }
            finally { _flushing = false; }
        }
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true; _pending.Clear();
            Clear();
        }
    }
}
