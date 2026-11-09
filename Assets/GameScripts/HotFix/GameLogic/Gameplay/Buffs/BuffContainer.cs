using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLogic
{
    /// <summary>回调观察稳定快照；本轮内增删统一延后提交。</summary>
    public sealed class BuffContainer : IDisposable
    {
        private readonly List<BuffObj> _items = new List<BuffObj>();
        private BuffMutationQueue _queue = new BuffMutationQueue();
        private BuffManager _manager;
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
        internal void Bind(BuffManager manager, BuffMutationQueue queue)
        { _manager = manager; _queue = queue; }

        // 兼容旧调用方式，也必须经过不可变请求和统一提交队列。
        public void Add(BuffModel model, SkillOwnerRef source, float duration,
            IReadOnlyDictionary<string, float> parameters = null, IBuffBehavior behavior = null)
        {
            if (IsDisposed) return;
            EntityValidation.RequireFiniteNonNegative(duration, nameof(duration));
            if (duration <= 0f) throw new ArgumentOutOfRangeException(nameof(duration));
            Apply(new AddBuffInfo(model, source, Owner, duration, durationSetTo: true,
                param: BuffObj.Copy(parameters)), behavior ?? new BuffBehavior());
        }
        public void Apply(AddBuffInfo info, IBuffBehavior behavior = null)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            if (IsDisposed || !info.Carrier.Equals(Owner)) return;
            if (_manager != null) { _manager.Submit(info, behavior); return; }
            Mutate(() => ApplyNow(info, behavior ?? new BuffBehavior()));
        }
        internal void ApplyNow(AddBuffInfo info, IBuffBehavior behavior)
        {
            // 已销毁的容器仅允许清理，排队中的玩法添加必须失效。
            if ((IsDisposed && info.Kind == BuffChangeKind.Apply) || !info.Carrier.Equals(Owner)) return;
            Dispatch(() =>
            {
                if (info.Kind == BuffChangeKind.Clear) { ClearNow(); return; }
                var selected = info.InstanceId.HasValue
                    ? _items.Find(b => b.InstanceId == info.InstanceId && b.Model.Id == info.Model.Id && b.Source.Equals(info.Caster))
                    : Find(info.Model.Id, info.Caster);
                if (info.Kind == BuffChangeKind.Remove) { RemoveNow(selected); return; }
                // 指定实例/减层不走新增策略，避免对独立层错误地新增或替换。
                if (info.InstanceId.HasValue || info.AddStack <= 0)
                {
                    if (selected != null) UpdateExisting(info, selected);
                    return;
                }
                var initialTime = Lifetime(info, null);
                var candidate = new BuffObj(info.Model, info.Caster,
                    (float)(initialTime > 0d ? initialTime : selected?.TimeRemaining ?? 0d),
                    Parameters(info, selected), behavior, Math.Min(info.Model.MaxStacks, info.AddStack));
                var decision = candidate.Behavior.SelectApplication(this, candidate);
                if (IsDisposed || decision.Mode == BuffApplyMode.Reject) return;
                var existing = decision.Existing;
                if (decision.Mode == BuffApplyMode.Refresh || decision.Mode == BuffApplyMode.Replace)
                {
                    if (!_items.Contains(existing)) throw new InvalidOperationException("Buff应用引用了当前容器之外的实例。");
                    if (decision.Mode == BuffApplyMode.Refresh) { UpdateExisting(info, existing); return; }
                }
                // 没有有效的新寿命时不能创建新层，也不能先删除旧层。
                if (initialTime <= 0d) return;
                candidate.TimeRemaining = initialTime;
                if (decision.Mode == BuffApplyMode.Replace)
                {
                    RemoveNow(existing);
                    if (IsDisposed) return;
                }
                candidate.Order = checked(++_order); candidate.InstanceId = candidate.Order;
                _items.Add(candidate);
                candidate.Behavior.OnOccur(this, candidate);
            });
        }
        private void UpdateExisting(AddBuffInfo info, BuffObj existing)
        {
            var stacks = (long)existing.Stacks + info.AddStack;
            var time = Lifetime(info, existing);
            if (stacks <= 0 || time <= 0d) { RemoveNow(existing); return; }
            // 先用候选校验参数，失败不改变已有实例。沿用原Behavior的规则。
            var candidate = new BuffObj(existing.Model, existing.Source, (float)time,
                Parameters(info, existing), existing.Behavior, (int)Math.Min(existing.Model.MaxStacks, stacks));
            var intervalChanged = existing.TickInterval != candidate.TickInterval;
            existing.Stacks = candidate.Stacks; existing.TimeRemaining = time;
            existing.Parameters = candidate.Parameters; existing.TickInterval = candidate.TickInterval;
            if (info.DurationSetTo || intervalChanged) existing.NextTickRemaining = existing.TickInterval;
            existing.Behavior.OnOccur(this, existing);
        }
        private static double Lifetime(AddBuffInfo info, BuffObj existing)
            => info.Permanent ? double.PositiveInfinity : info.DurationSetTo ? info.Time
                : (existing?.TimeRemaining ?? 0d) + info.Time;
        private static IReadOnlyDictionary<string, float> Parameters(AddBuffInfo info, BuffObj existing)
        {
            if (info.Param == null) return existing?.Parameters;
            if (!info.MergeParameters || existing == null) return info.Param;
            var merged = new Dictionary<string, float>();
            foreach (var pair in existing.Parameters) merged[pair.Key] = pair.Value;
            foreach (var pair in info.Param) merged[pair.Key] = pair.Value;
            return merged;
        }
        public void Remove(BuffObj buff)
        {
            if (buff == null || !_items.Contains(buff)) return;
            var info = AddBuffInfo.Remove(Owner, buff);
            // 生命周期清理允许在终局/死亡期间执行；它同样排队，不经过玩法添加入口。
            Mutate(() => ApplyNow(info, null));
        }
        private void RemoveNow(BuffObj buff)
        {
            if (buff != null && _items.Remove(buff) && !SuppressRemovedCallbacks) buff.Behavior.OnRemoved(this, buff);
        }
        public void Clear()
        {
            var info = AddBuffInfo.Clear(Owner);
            Mutate(() => ApplyNow(info, null));
        }
        private void ClearNow()
        {
            var items = _items.ToArray(); _items.Clear();
            Exception failure = null;
            foreach (var buff in items)
            {
                if (SuppressRemovedCallbacks) break;
                try { buff.Behavior.OnRemoved(this, buff); }
                catch (Exception exception) { if (failure == null) failure = exception; }
            }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
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
        internal void OnAttackLaunched(SkillCastSnapshot cast, Func<bool> active)
            => DispatchCombat((behavior, buff) => behavior.OnAttackLaunched(this, buff, cast), active);
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

        private void Mutate(Action action) => _queue.Mutate(action);
        private void Dispatch(Action action) => _queue.Dispatch(action);
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Clear();
        }
    }
}
