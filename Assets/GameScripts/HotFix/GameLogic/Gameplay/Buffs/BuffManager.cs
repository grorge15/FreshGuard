using System;

namespace GameLogic
{
    /// <summary>Buff修改统一入口：先检查来源/承受者，再排队，提交时重新确认目标身份。</summary>
    public sealed class BuffManager
    {
        private readonly BattleRuntime _battle;
        private readonly BuffMutationQueue _queue = new BuffMutationQueue();
        internal BuffManager(BattleRuntime battle) { _battle = battle; }
        internal void Bind(BuffContainer container) => container.Bind(this, _queue);
        public void Submit(AddBuffInfo info) => Submit(info, null);
        internal void Submit(AddBuffInfo info, IBuffBehavior behavior)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            // 在入队时检查，防止后续伤害回调排队后绕开禁止派生效果的限制。
            if (!_battle.Combat.AllowsGameplayTriggers || !CanApply(info, out var target)) return;
            var container = target.Buffs;
            _queue.Mutate(() =>
            {
                if (!CanApply(info, out var current) || !ReferenceEquals(current.Buffs, container)) return;
                container.ApplyNow(info, behavior ?? (info.Kind == BuffChangeKind.Apply
                    ? _battle.BuffBehaviors.Create(info.Model.BehaviorKey) : null));
            });
        }
        private bool CanApply(AddBuffInfo info, out ISkillOwner target)
        {
            target = null;
            return !_battle.IsDisposed && _battle.IsBattleValid() &&
                (info.Kind == BuffChangeKind.Clear || info.Caster.BattleId == _battle.BattleId) &&
                _battle.TryResolveOwner(info.Carrier, out target) && !target.Buffs.IsDisposed;
        }
    }
}
