using System;
using System.Linq;

namespace GameLogic
{
    /// <summary>各层只保存来源和数值；目标身份从所属容器读取，不捕获池化实体。</summary>
    public sealed class PoisonBuffBehavior : BuffBehavior
    {
        private readonly BattleRuntime _battle;
        public PoisonBuffBehavior(BattleRuntime battle) { _battle = battle ?? throw new ArgumentNullException(nameof(battle)); }

        public override void OnCreate(BuffObj buff)
        {
            var interval = buff.Get("poisonInterval");
            var cap = buff.Get("poisonMaxLayers");
            var damage = buff.Get("poisonDamage");
            if (interval <= 0f || cap < 1f || cap >= int.MaxValue || cap != Math.Truncate(cap) ||
                damage < 0f || damage >= int.MaxValue || damage != Math.Truncate(damage))
                throw new ArgumentException("中毒间隔、层数或伤害无效。");
            buff.TickInterval = interval;
        }

        public override BuffApplyDecision SelectApplication(BuffContainer container, BuffObj incoming)
        {
            var layers = container.Items.Where(b => b.Model.Id == incoming.Model.Id).ToArray();
            if (layers.Length < (int)incoming.Get("poisonMaxLayers")) return BuffApplyDecision.Add;
            var weakest = layers.OrderBy(RemainingDamage).ThenBy(b => b.Order).First();
            return RemainingDamage(incoming) > RemainingDamage(weakest)
                ? BuffApplyDecision.Replace(weakest) : BuffApplyDecision.Reject;
        }

        private static double RemainingDamage(BuffObj buff) => buff.Get("poisonDamage") * buff.RemainingTickCount;

        public override void OnTick(BuffContainer container, BuffObj buff)
        {
            if (_battle.IsDisposed || !_battle.IsBattleValid() || !_battle.CanCastNow()) return;
            _battle.Combat.ResolvePeriodicDamage(buff.Source, container.Owner, (int)buff.Get("skillId", 1507),
                checked((int)buff.Get("poisonDamage")));
        }
    }
}
