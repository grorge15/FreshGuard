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
        private long _order;
        public bool IsDisposed { get; private set; }
        public int Count => _items.Count;
        internal double NextBoundary
        {
            get
            {
                var delay = double.PositiveInfinity;
                foreach (var buff in _items)
                {
                    delay = Math.Min(delay, buff.Remaining);
                    if (buff.Model.TickInterval > 0f)
                        delay = Math.Min(delay, Math.Max(0.0000001d, buff.Model.TickInterval - buff.TickElapsed));
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
            Mutate(() =>
            {
                var existing = Find(model.Id, source);
                if (existing != null)
                {
                    existing.Stacks = Math.Min(model.MaxStacks, existing.Stacks + 1);
                    existing.Remaining = duration; existing.Parameters = copy;
                    existing.TickElapsed = 0d;
                    Dispatch(() => existing.Behavior.OnOccur(this, existing));
                    return;
                }
                var buff = new BuffObj(model, source, duration, copy, behavior ?? new BuffBehavior()) { Order = ++_order };
                _items.Add(buff);
                Dispatch(() => buff.Behavior.OnOccur(this, buff));
            });
        }
        public void Remove(BuffObj buff)
        {
            if (buff == null) return;
            Mutate(() => { if (_items.Remove(buff)) Dispatch(() => buff.Behavior.OnRemoved(this, buff)); });
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
                        try { buff.Behavior.OnRemoved(this, buff); }
                        catch (Exception exception) { if (failure == null) failure = exception; }
                    if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                });
            });
        }
        public void Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (IsDisposed) return;
            Dispatch(() =>
            {
                foreach (var buff in _items.ToArray())
                {
                    if (IsDisposed) break;
                    var active = Math.Min(deltaTime, buff.Remaining);
                    buff.Remaining = Math.Max(0f, buff.Remaining - deltaTime);
                    if (buff.Model.TickInterval > 0f)
                    {
                        buff.TickElapsed += active;
                        while (!IsDisposed && buff.TickElapsed + 0.0000001d >= buff.Model.TickInterval)
                        {
                            buff.TickElapsed -= buff.Model.TickInterval;
                            buff.Behavior.OnTick(this, buff);
                        }
                    }
                    if (buff.Remaining <= 0f) Mutate(() =>
                    {
                        // Tick中同来源刷新可能先排队；提交过期移除时重新检查，不能删掉已续期实例。
                        if (buff.Remaining <= 0f && _items.Remove(buff)) Dispatch(() => buff.Behavior.OnRemoved(this, buff));
                    });
                }
            });
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
