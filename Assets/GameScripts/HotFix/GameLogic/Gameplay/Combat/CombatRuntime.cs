using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>单局伤害入口：发射冻结数值，逐目标执行Buff、扣血与击杀通知。表现只订阅数值快照。</summary>
    public sealed class CombatRuntime : IDisposable
    {
        private readonly object _owner = new object();
        private BattleRuntime _battle;
        private long _sequence;
        private readonly HashSet<int> _resolving = new HashSet<int>();
        public long BattleId { get; }
        public float BattleTime { get; private set; }
        public bool IsDisposed { get; private set; }
        public event Action<CombatDamageContext> DamageApplying;
        public event Action<CombatDamageContext> TargetResolved;

        public CombatRuntime(long battleId)
        {
            if (battleId <= 0) throw new ArgumentOutOfRangeException(nameof(battleId));
            BattleId = battleId;
        }

        internal void Bind(BattleRuntime battle)
        {
            if (battle.BattleId != BattleId || (_battle != null && !ReferenceEquals(_battle, battle)))
                throw new InvalidOperationException("伤害入口不能跨对局绑定。");
            _battle = battle;
        }

        private bool Valid => !IsDisposed && _battle != null && !_battle.IsDisposed && _battle.IsBattleValid();
        private bool Active => Valid && _battle.CanCastNow();

        public CombatActionSnapshot CaptureAttack(CombatActionSnapshot action)
        {
            ThrowIfDisposed();
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (action.BattleId != BattleId || action.Owner != null)
                throw new InvalidOperationException("攻击必须来自当前对局且尚未锁定。");
            var draft = new CombatOutcomeDraft(action);
            DispatchSource(action, buffs => buffs.OnAttack(draft, () => Active));
            ThrowIfDisposed();
            if (draft.IsCancelled || !Active) throw new InvalidOperationException("攻击准备期间被取消或对局已结束。");
            return action.Lock(_owner, checked(++_sequence), draft);
        }

        public CombatResolutionResult Resolve(CombatActionSnapshot action)
        {
            ThrowIfDisposed();
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (action.BattleId != BattleId || !ReferenceEquals(action.Owner, _owner) || action.Consumed)
                throw new InvalidOperationException("攻击快照不属于当前运行时或已结算。");
            action.Consumed = true;
            var results = new List<CombatTargetResolution>();
            foreach (var id in action.TargetInstanceIds)
            {
                if (!Active) break;
                results.Add(ResolveTarget(action, id));
            }
            return new CombatResolutionResult(new CombatOutcomeDraft(action), results);
        }

        private CombatTargetResolution ResolveTarget(CombatActionSnapshot action, int id)
        {
            var ignored = new CombatTargetResolution(id, 0, DamageResult.Ignored);
            if (!TryGetTarget(action, id, out var enemy) || !_resolving.Add(id)) return ignored;
            try
            {
                var reference = enemy.OwnerRef;
                var context = new CombatDamageContext(new CombatOutcomeDraft(action, new[] { id }), reference);
                DispatchSource(action, buffs => buffs.OnHit(context, () => Active));
                if (!TryGetTarget(action, id, out enemy)) return ignored;
                enemy.Buffs.OnBeHurt(context, () => Active);
                if (!TryGetTarget(action, id, out enemy)) return ignored;
                var damage = context.Outcome.GetFinalDamage();
                if (damage <= 0) return ignored;
                DamageApplying?.Invoke(context);
                if (!TryGetTarget(action, id, out enemy)) return ignored;
                // 回调结束后只保存值与旧Buff容器；ApplyDamage可能同步归池并创建其他敌人。
                var applied = Math.Min(enemy.CurrentHp, damage);
                var elite = enemy.Configuration != null && enemy.Configuration.EnemyType >= 2;
                var victimBuffs = enemy.Buffs;
                context.Result = new CombatTargetResolution(id, damage,
                    damage >= enemy.CurrentHp ? DamageResult.Killed : DamageResult.Damaged, applied, elite);
                var result = _battle.Registry.ApplyDamage(id, damage, out var removal,
                    () => victimBuffs.OnBeKilled(context, () => Valid));
                context.Result = new CombatTargetResolution(id, damage, result,
                    result == DamageResult.Ignored ? 0 : applied, elite);
                context.Removal = removal;
                // 扣血已经提交，暂停不应打断事实通知；终局/销毁仍立即停止。
                if (!Valid) return context.Result;
                if (context.Result.IsKilled) DispatchSource(action, buffs => buffs.OnKill(context, () => Valid), true);
                if (Valid && context.Result.AppliedDamage > 0 && !context.Result.IsKilled)
                {
                    var source = SkillOwnerRef.Entity(BattleId, action.SourceSide, action.SourceInstanceId);
                    foreach (var effect in context.HitBuffs)
                    {
                        if (!TryGetTarget(action, id, out enemy, true) || !enemy.OwnerRef.Equals(reference)) break;
                        if (!_battle.Catalog.TryGetBuff(effect.BuffId, out var model))
                            throw new InvalidOperationException("命中效果引用了未配置Buff：" + effect.BuffId);
                        _battle.AddBuff(enemy, model, source, effect.Duration, effect.Parameters);
                    }
                }
                if (Valid) TargetResolved?.Invoke(context);
                return context.Result;
            }
            finally { _resolving.Remove(id); }
        }

        private bool TryGetTarget(CombatActionSnapshot action, int id, out EnemyEntity enemy, bool committed = false)
        {
            enemy = null;
            if (!(committed ? Valid : Active) || !_battle.Registry.TryGet(id, out var entity) || !(entity is EnemyEntity target) ||
                !target.IsAlive || target.Side != action.SourceSide) return false;
            enemy = target;
            return true;
        }

        private void DispatchSource(CombatActionSnapshot action, Action<BuffContainer> dispatch, bool committed = false)
        {
            if (!(committed ? Valid : Active)) return;
            dispatch((action.SourceSide == BattleSide.Player ? _battle.Player : _battle.Opponent).Buffs);
            if ((committed ? Valid : Active) && _battle.Registry.TryGet(action.SourceInstanceId, out var source) &&
                !source.IsRemoved && source.Side == action.SourceSide) dispatch(source.Buffs);
        }

        public void AdvanceTime(float deltaTime)
        {
            ThrowIfDisposed();
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            EntityValidation.RequireFiniteNonNegative(BattleTime + deltaTime, nameof(deltaTime));
            BattleTime += deltaTime;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            DamageApplying = null; TargetResolved = null;
            BattleTime = 0f; _resolving.Clear();
        }

        private void ThrowIfDisposed()
        { if (IsDisposed) throw new ObjectDisposedException(nameof(CombatRuntime)); }
    }
}
