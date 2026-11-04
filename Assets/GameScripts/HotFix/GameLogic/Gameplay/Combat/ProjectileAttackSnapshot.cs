using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>每颗弹丸共享一次发射修正，每个目标只消费一次；不保存池化实体引用。</summary>
    public sealed class ProjectileAttackSnapshot
    {
        public CombatActionSnapshot Action { get; }
        private readonly HashSet<int> _resolvedTargets = new HashSet<int>();
        internal ProjectileAttackSnapshot(CombatActionSnapshot action) { Action = action; }
        internal bool TryConsume(int targetId) => _resolvedTargets.Add(targetId);
    }
}
