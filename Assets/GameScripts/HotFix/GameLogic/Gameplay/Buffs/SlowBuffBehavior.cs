using System;

namespace GameLogic
{
    /// <summary>减速生效、刷新和移除时更新移动速度；移动代码只读取结果。</summary>
    public sealed class SlowBuffBehavior : BuffBehavior
    {
        private readonly BattleRuntime _battle;
        public SlowBuffBehavior(BattleRuntime battle)
        { _battle = battle ?? throw new ArgumentNullException(nameof(battle)); }

        public override void OnOccur(BuffContainer container, BuffObj buff) => UpdateSpeed(container);
        public override void OnRemoved(BuffContainer container, BuffObj buff) => UpdateSpeed(container);

        private void UpdateSpeed(BuffContainer container)
        {
            var owner = container.Owner;
            // 终局清理也要恢复属性；只在本局原容器上操作，不缓存池化实体。
            if (owner.BattleId != _battle.BattleId || !owner.EntityId.HasValue ||
                !_battle.Registry.TryGet(owner.EntityId.Value, out var entity) || !(entity is EnemyEntity enemy) ||
                !enemy.OwnerRef.Equals(owner) || !ReferenceEquals(enemy.Buffs, container)) return;

            var strongest = 0f;
            var receivedControl = Math.Max(0f, Math.Min(1f, enemy.Configuration?.ControlResist ?? 1f));
            foreach (var effect in container.Items)
            {
                if (!(effect.Behavior is SlowBuffBehavior) || effect.Remaining <= 0f) continue;
                var slow = Math.Max(0f, Math.Min(1f, effect.Get("slow")));
                strongest = Math.Max(strongest, slow * receivedControl);
            }
            enemy.SetMoveSpeed(enemy.BaseSpeed * Math.Max(0.5f, 1f - strongest));
        }
    }
}
