using System;
using System.Linq;

namespace GameLogic
{
    /// <summary>装盒仅阻止自主移动，各来源分别计时，不改写减速维护的速度。</summary>
    public sealed class StoreIsolationBuffBehavior : BuffBehavior
    {
        private readonly BattleRuntime _battle;
        public StoreIsolationBuffBehavior(BattleRuntime battle)
        { _battle = battle ?? throw new ArgumentNullException(nameof(battle)); }

        public override void OnOccur(BuffContainer container, BuffObj buff) => Refresh(container, buff.Model.EffectPrefab);
        public override void OnRemoved(BuffContainer container, BuffObj buff) => Refresh(container, buff.Model.EffectPrefab);

        private void Refresh(BuffContainer container, string prefab)
        {
            var owner = container.Owner;
            if (owner.BattleId != _battle.BattleId || !owner.EntityId.HasValue ||
                !_battle.Registry.TryGet(owner.EntityId.Value, out var entity) || !(entity is EnemyEntity enemy) ||
                !enemy.OwnerRef.Equals(owner) || !ReferenceEquals(enemy.Buffs, container)) return;
            var active = container.Items.FirstOrDefault(b => b.Behavior is StoreIsolationBuffBehavior && b.Remaining > 0f);
            _battle.SetEnemyIsolation(enemy, active != null, active?.Model.EffectPrefab ?? prefab);
        }
    }
}
