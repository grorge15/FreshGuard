using System;

namespace GameLogic
{
    public interface ICombatStatusPort
    {
        bool TryApplySlow(int targetInstanceId, int sourceInstanceId, float slowRatio, float duration);
    }

    /// <summary>敌人状态适配层。技能只依赖端口，不接触池化实体。</summary>
    public sealed class EnemyCombatStatusPort : ICombatStatusPort
    {
        private readonly BattleRuntime _battle;

        public EnemyCombatStatusPort(BattleRuntime battle)
        { _battle = battle ?? throw new ArgumentNullException(nameof(battle)); }

        public bool TryApplySlow(int targetInstanceId, int sourceInstanceId, float slowRatio, float duration)
        {
            if (_battle.IsDisposed || !_battle.Registry.TryGet(targetInstanceId, out var entity) ||
                !(entity is EnemyEntity enemy) || !enemy.IsAlive) return false;
            return enemy.TryApplySlow(sourceInstanceId, slowRatio, duration);
        }
    }
}
