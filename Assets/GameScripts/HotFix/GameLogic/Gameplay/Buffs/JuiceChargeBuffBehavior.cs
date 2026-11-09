using System;
using System.Linq;

namespace GameLogic
{
    public sealed class JuiceChargeBuffBehavior : BuffBehavior
    {
        private readonly BattleRuntime _battle;
        public int JuiceCount { get; private set; }
        public bool Pending { get; private set; }
        public JuiceChargeBuffBehavior(BattleRuntime battle) { _battle = battle; }
        public override void OnCast(BuffContainer container, BuffObj buff, SkillCastContext context)
        {
            if (!context.Request.Owner.Equals(container.Owner) || !buff.Source.Equals(container.Owner)) return;
            if (context.Request.SkillId == 1603)
            {
                if (Pending) return;
                var value = context.Parameters["juiceRequired"];
                if (value < 1 || value >= int.MaxValue || value != Math.Truncate(value))
                    throw new InvalidOperationException("JuiceRequired必须为正整数。");
                var required = (int)value;
                var position = context.Request.Target.Position;
                context.DeferUntilAccepted(() =>
                {
                    if (Pending || !IsCurrent(container, buff)) return;
                    JuiceCount = Math.Min(required, JuiceCount + 1);
                    if (JuiceCount < required) return;
                    Pending = true;
                    _battle.TriggeredSkills.Enqueue(container.Owner, buff.InstanceId, buff.Model.TriggerSkillId, required, position);
                });
            }
            else if (context.Request.SkillId == buff.Model.TriggerSkillId)
            {
                if (context.Request.Trigger != SkillTriggerSource.Buff || context.Request.SourceBuffInstanceId != buff.InstanceId ||
                    !Pending || !_battle.TriggeredSkills.TryGet(container.Owner, out var pending) ||
                    pending.BuffInstanceId != buff.InstanceId || JuiceCount < pending.Cost)
                { context.Cancel(); return; }
                context.DeferUntilAccepted(() =>
                {
                    if (!IsCurrent(container, buff)) return;
                    JuiceCount -= pending.Cost;
                    Pending = false;
                    _battle.TriggeredSkills.Cancel(container.Owner, buff.InstanceId);
                });
            }
        }
        private bool IsCurrent(BuffContainer container, BuffObj buff) => !_battle.IsDisposed &&
            _battle.TryResolveOwner(container.Owner, out _) && container.Items.Contains(buff);
        public override void OnRemoved(BuffContainer container, BuffObj buff)
        { _battle.TriggeredSkills.Cancel(container.Owner, buff.InstanceId); Pending = false; JuiceCount = 0; }
    }
}
