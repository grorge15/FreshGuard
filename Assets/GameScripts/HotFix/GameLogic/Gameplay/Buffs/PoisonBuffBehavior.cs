using System;

namespace GameLogic
{
    /// <summary>各层只保存来源和数值；目标身份从所属容器读取，不捕获池化实体。</summary>
    public sealed class PoisonBuffBehavior : BuffBehavior
    {
        private readonly BattleRuntime _battle;
        public PoisonBuffBehavior(BattleRuntime battle) { _battle = battle ?? throw new ArgumentNullException(nameof(battle)); }
        public override void OnTick(BuffContainer container, BuffObj buff)
        {
            if (_battle.IsDisposed || !_battle.IsBattleValid() || !_battle.CanCastNow()) return;
            _battle.Combat.ResolvePeriodicDamage(buff.Source, container.Owner, (int)buff.Get("skillId", 1507),
                checked((int)buff.Get("poisonDamage")));
        }
    }
}
